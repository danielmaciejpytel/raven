using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[ExecuteInEditMode]
public class GeometryGrassPainter : MonoBehaviour
{
#if UNITY_EDITOR
    public static event System.Action<GeometryGrassPainter> EditorGrassDataChanged;
#endif

    [Min(1f)]
    [Tooltip("Size of local grass cells. Each cell gets its own URP Forward light list.")]
    public float lightingCellSize = 8f;

    [SerializeField]
    [Tooltip("Saved grass geometry. After painting, update it with Raven > Grass > Bake meshes in open scenes.")]
    private GrassMeshCache meshCache;

    private string preparedSignature;

    public void PrepareMeshCacheBake()
    {
        // Material bounds can change without OnValidate on this component.
        if (rebuildRequested || preparedSignature != ComputeMeshCacheSignature())
            RebuildMesh();
    }

    public bool IsMeshCacheBakeReady
    {
        get
        {
            if (rebuildRequested || nextPendingCell < cells.Count
                || preparedSignature != ComputeMeshCacheSignature())
                return false;
            foreach (var cell in cells)
                if (cell.pendingData != null)
                    return false;
            return true;
        }
    }

    public bool AreMeshUploadsComplete
    {
        get
        {
            if (rebuildRequested || nextPendingCell < cells.Count)
                return false;
            foreach (var cell in cells)
                if (cell.pendingData != null)
                    return false;
            return true;
        }
    }

    private sealed class GrassCell
    {
        public GameObject gameObject;
        public Mesh mesh;
        public MeshRenderer renderer;
        public CellData pendingData;
        public bool readyForRendering;
        public bool ownsMesh;
        public bool hasMeshSignature;
        public Hash128 meshSignature;
    }

    private sealed class CellData
    {
        public readonly List<Vector3> positions = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> sizes = new List<Vector2>();
        public readonly List<Color> colors = new List<Color>();
        public readonly List<int> indices = new List<int>();
        public Hash128 signature;
    }

    private readonly List<GrassCell> cells = new List<GrassCell>();
    private int nextPendingCell;
    private RendererState lastRendererState;
    private bool hasRendererState;
    private struct RendererState
    {
        public int layer;
        public bool enabled;
        public Material material;
        public UnityEngine.Rendering.ShadowCastingMode shadows;
        public bool receiveShadows;
        public uint renderingLayers;
        public UnityEngine.Rendering.LightProbeUsage lightProbes;
        public UnityEngine.Rendering.ReflectionProbeUsage reflectionProbes;
        public Transform probeAnchor;
        public bool occlusion;
        public int sortingLayer;
        public int sortingOrder;
        public bool Matches(RendererState other) =>
            layer == other.layer
            && enabled == other.enabled
            && material == other.material
            && shadows == other.shadows
            && receiveShadows == other.receiveShadows
            && renderingLayers == other.renderingLayers
            && lightProbes == other.lightProbes
            && reflectionProbes == other.reflectionProbes
            && probeAnchor == other.probeAnchor
            && occlusion == other.occlusion
            && sortingLayer == other.sortingLayer
            && sortingOrder == other.sortingOrder;
    }
    private RendererState ReadRendererState() => new RendererState {
        layer = gameObject.layer,
        enabled = sourceRenderer.enabled && isActiveAndEnabled,
        material = sourceRenderer.sharedMaterial,
        shadows = sourceRenderer.shadowCastingMode,
        receiveShadows = sourceRenderer.receiveShadows,
        renderingLayers = sourceRenderer.renderingLayerMask,
        lightProbes = sourceRenderer.lightProbeUsage,
        reflectionProbes = sourceRenderer.reflectionProbeUsage,
        probeAnchor = sourceRenderer.probeAnchor,
        occlusion = sourceRenderer.allowOcclusionWhenDynamic,
        sortingLayer = sourceRenderer.sortingLayerID,
        sortingOrder = sourceRenderer.sortingOrder
    };

    private MeshRenderer sourceRenderer;
    private bool rebuildRequested;

    // Budget both geometry uploads and renderer registration across all painters.
    // Editor updates can run repeatedly without submitting a render context, so wall
    // time (or an Update tick) must never replenish the graphics staging budget.
    private const int UploadVertexBudget = 8192;
    private const int UploadCellBudget = 1;
    private const int ActivationCellBudget = 8;
    private static readonly List<GeometryGrassPainter> activePainters = new List<GeometryGrassPainter>();
    private static bool uploadBudgetAvailable = true;
    private static bool hasPendingActivation;

    private static void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
    {
        // URP also opens a context with no cameras, without submitting any rendering.
        if (cameras.Count > 0)
            uploadBudgetAvailable = true;
    }

    private static void UploadPendingMeshes()
    {
        if (!uploadBudgetAvailable)
            return;
        uploadBudgetAvailable = false;

        // Preserve the existing ambient look in scenes without baked probe data.
        Shader.SetGlobalFloat("_RavenGrassBakedProbes", LightmapSettings.lightProbes != null
            && LightmapSettings.lightProbes.count > 0 ? 1f : 0f);

        // Initial mesh uploads and the first grass/shadow draws must not share the
        // same initialization burst. This branch runs only after a render context
        // has completed since the last upload, and never uploads another batch.
        if (!HasPendingUploads())
        {
            if (hasPendingActivation)
            {
                hasPendingActivation = false;
                int remainingActivations = ActivationCellBudget;
                int remainingVertices = UploadVertexBudget;
                foreach (var painter in activePainters)
                {
                    if (painter == null || !painter.isActiveAndEnabled)
                        continue;
                    foreach (var cell in painter.cells)
                    {
                        if (cell.readyForRendering || cell.mesh == null)
                            continue;
                        if (remainingActivations == 0 || cell.mesh.vertexCount > remainingVertices)
                        {
                            hasPendingActivation = true;
                            continue;
                        }
                        cell.readyForRendering = true;
                        painter.ApplyRendererSettings(cell);
                        remainingActivations--;
                        remainingVertices -= cell.mesh.vertexCount;
                    }
                }
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    SceneView.RepaintAll();
#endif
            }
            return;
        }

        int remaining = UploadVertexBudget;
        int remainingCells = UploadCellBudget;
        foreach (var painter in activePainters)
        {
            if (painter == null || !painter.isActiveAndEnabled)
                continue;
            while (painter.nextPendingCell < painter.cells.Count)
            {
                var cell = painter.cells[painter.nextPendingCell];
                var data = cell.pendingData;
                if (data == null)
                {
                    // Authored meshes already contain the geometry. Register their
                    // renderers within the same budget without uploading it again.
                    if (cell.mesh != null && cell.gameObject == null)
                    {
                        painter.CreateCellRenderer(cell);
                        remainingCells--;
#if UNITY_EDITOR
                        if (!Application.isPlaying)
                            SceneView.RepaintAll();
#endif
                    }
                    painter.nextPendingCell++;
                    if (remainingCells <= 0)
                        return;
                    continue;
                }
                if (data.positions.Count > remaining)
                    return;
                painter.UploadCell(cell, data);
                cell.pendingData = null;
                painter.nextPendingCell++;
                remaining -= data.positions.Count;
                remainingCells--;
                if (remaining <= 0 || remainingCells <= 0)
                    return;
            }
        }
    }

    private static bool HasPendingUploads()
    {
        foreach (var painter in activePainters)
            if (painter != null && painter.isActiveAndEnabled
                && painter.nextPendingCell < painter.cells.Count)
                return true;
        return false;
    }

#if UNITY_EDITOR
    private static void UploadEditorMeshes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        UploadPendingMeshes();
    }
#endif

    private void OnValidate()
    {
        // OnValidate may run off the main thread; create meshes during the next update.
        rebuildRequested = true;
    }
    MeshFilter filter;

    public Color AdjustedColor;

    [Range(1, 600000)]
    public int grassLimit = 50000;

    private Vector3 lastPosition = Vector3.zero;

    public int toolbarInt = 0;

    [SerializeField]
    List<Vector3> positions = new List<Vector3>();
    [SerializeField]
    List<Color> colors = new List<Color>();
    [SerializeField]
    List<int> indicies = new List<int>();
    [SerializeField]
    List<Vector3> normals = new List<Vector3>();
    [SerializeField]
    List<Vector2> length = new List<Vector2>();

    public bool painting;
    public bool removing;
    public bool editing;

    public int i = 0;

    public float sizeWidth = 1f;
    public float sizeLength = 1f;
    public float density = 1f;


    public float normalLimit = 1;

    public float rangeR, rangeG, rangeB;
    public LayerMask hitMask = 1;
    public LayerMask paintMask = 1;
    public float brushSize;

#if UNITY_EDITOR
    private const float SpatialCellSize = 4f;
    private Dictionary<Vector3Int, HashSet<int>> spatialIndex;
    private Matrix4x4 spatialIndexTransform;
    private bool spatialIndexReady;
    private RaycastHit currentBrushHit;
#endif

    Vector3 mousePos;

    [HideInInspector]
    public Vector3 hitPosGizmo;

    Vector3 hitPos;

    [HideInInspector]
    public Vector3 hitNormal;

#if UNITY_EDITOR
    [SerializeField] private bool showBrushPreview = true;
    private bool hasBrushHit;
    private int undoGroup = -1;
    private bool undoRecordedForStroke;
    private bool hasLastPaintPosition;
    private bool editorStrokeActive;
    private bool brushStrokeMarkedDirty;
#endif

    private void OnEnable()
    {
        filter = GetComponent<MeshFilter>();
        sourceRenderer = GetComponent<MeshRenderer>();
        if (!activePainters.Contains(this))
            activePainters.Add(this);
        RenderPipelineManager.endContextRendering -= OnEndContextRendering;
        RenderPipelineManager.endContextRendering += OnEndContextRendering;
        RebuildMesh();
#if UNITY_EDITOR
        EditorApplication.update -= UploadEditorMeshes;
        EditorApplication.update += UploadEditorMeshes;
        SceneView.duringSceneGui -= OnScene;
        SceneView.duringSceneGui += OnScene;
        Undo.undoRedoPerformed -= OnUndoRedo;
        Undo.undoRedoPerformed += OnUndoRedo;
#endif
    }

    private void OnDisable()
    {
        activePainters.Remove(this);
        if (activePainters.Count == 0)
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
#if UNITY_EDITOR
        if (activePainters.Count == 0)
            EditorApplication.update -= UploadEditorMeshes;
        SceneView.duringSceneGui -= OnScene;
        Undo.undoRedoPerformed -= OnUndoRedo;
        EndUndoStroke();
#endif
        ReleaseCells();
    }

    private static void DestroyGenerated(UnityEngine.Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }

    private void OnDestroy()
    {
        activePainters.Remove(this);
        if (activePainters.Count == 0)
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
#if UNITY_EDITOR
        if (activePainters.Count == 0)
            EditorApplication.update -= UploadEditorMeshes;
        SceneView.duringSceneGui -= OnScene;
        Undo.undoRedoPerformed -= OnUndoRedo;
        EndUndoStroke();
#endif
        ReleaseCells();
    }

    private void ReleaseCells()
    {
        foreach (var cell in cells)
        {
            if (cell.gameObject != null)
                cell.gameObject.SetActive(false);
            DestroyGenerated(cell.gameObject);
            if (cell.ownsMesh)
                DestroyGenerated(cell.mesh);
        }
        cells.Clear();
        hasRendererState = false;
        nextPendingCell = 0;
    }

    private void LateUpdate()
    {
        if (rebuildRequested
#if UNITY_EDITOR
            && (Application.isPlaying || !editorStrokeActive)
#endif
            )
            RebuildMesh();
        if (Application.isPlaying)
            UploadPendingMeshes();
        // Keep the original renderer as the source of material/layer settings.
        if (sourceRenderer == null)
            return;
        var state = ReadRendererState();
        if (!hasRendererState || !lastRendererState.Matches(state))
        {
            lastRendererState = state;
            hasRendererState = true;
            foreach (var cell in cells)
                ApplyRendererSettings(cell);
        }
    }

    private void ApplyRendererSettings(GrassCell cell)
    {
        if (cell.renderer == null)
            return;
        if (cell.gameObject.layer != (gameObject.layer))
            cell.gameObject.layer = gameObject.layer;
        bool visible = cell.readyForRendering && sourceRenderer.enabled && isActiveAndEnabled;
        if (cell.renderer.enabled != visible)
            cell.renderer.enabled = visible;
        if (cell.renderer.sharedMaterial != sourceRenderer.sharedMaterial)
            cell.renderer.sharedMaterial = sourceRenderer.sharedMaterial;
        if (cell.renderer.shadowCastingMode != (sourceRenderer.shadowCastingMode))
            cell.renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        if (cell.renderer.receiveShadows != (sourceRenderer.receiveShadows))
            cell.renderer.receiveShadows = sourceRenderer.receiveShadows;
        if (cell.renderer.renderingLayerMask != (sourceRenderer.renderingLayerMask))
            cell.renderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
        if (cell.renderer.lightProbeUsage != (sourceRenderer.lightProbeUsage))
            cell.renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        if (cell.renderer.reflectionProbeUsage != (sourceRenderer.reflectionProbeUsage))
            cell.renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        if (cell.renderer.probeAnchor != (sourceRenderer.probeAnchor))
            cell.renderer.probeAnchor = sourceRenderer.probeAnchor;
        if (cell.renderer.allowOcclusionWhenDynamic != (sourceRenderer.allowOcclusionWhenDynamic))
            cell.renderer.allowOcclusionWhenDynamic = sourceRenderer.allowOcclusionWhenDynamic;
        if (cell.renderer.sortingLayerID != (sourceRenderer.sortingLayerID))
            cell.renderer.sortingLayerID = sourceRenderer.sortingLayerID;
        if (cell.renderer.sortingOrder != (sourceRenderer.sortingOrder))
            cell.renderer.sortingOrder = sourceRenderer.sortingOrder;

    }

    // Preserve the painted object-space coordinates: the shader uses them as random seeds.
    // Local renderers prevent distant torches competing for one eight-light Forward list.
    private void RebuildMesh()
    {
        if (filter == null)
            filter = GetComponent<MeshFilter>();
        if (sourceRenderer == null)
            sourceRenderer = GetComponent<MeshRenderer>();

        rebuildRequested = false;
        nextPendingCell = 0;
        i = positions.Count;
        preparedSignature = ComputeMeshCacheSignature();
        if (TryUseMeshCache())
        {
            filter.sharedMesh = null;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                SceneView.RepaintAll();
#endif
            return;
        }
        var groups = new Dictionary<Vector3Int, CellData>();
        var batches = new List<CellData>();
        float cellSize = Mathf.Max(1f, lightingCellSize);
        indicies.Clear();
        for (int index = 0; index < positions.Count; index++)
        {
            indicies.Add(index);
            Vector3 position = positions[index];
            var key = new Vector3Int(Mathf.FloorToInt(position.x / cellSize),
                Mathf.FloorToInt(position.y / cellSize), Mathf.FloorToInt(position.z / cellSize));
            if (!groups.TryGetValue(key, out var data) || data.positions.Count >= UploadVertexBudget)
            {
                data = new CellData();
                groups[key] = data;
                batches.Add(data);
            }
            data.indices.Add(data.positions.Count);
            data.positions.Add(position);
            data.normals.Add(index < normals.Count ? normals[index] : Vector3.up);
            data.sizes.Add(index < length.Count ? length[index] : Vector2.one);
            Color color = index < colors.Count ? colors[index] : Color.white;
            data.colors.Add(color);
            data.signature.Append(position.x);
            data.signature.Append(position.y);
            data.signature.Append(position.z);
            Vector3 normal = data.normals[data.normals.Count - 1];
            data.signature.Append(normal.x);
            data.signature.Append(normal.y);
            data.signature.Append(normal.z);
            Vector2 size = data.sizes[data.sizes.Count - 1];
            data.signature.Append(size.x);
            data.signature.Append(size.y);
            data.signature.Append(color.r);
            data.signature.Append(color.g);
            data.signature.Append(color.b);
            data.signature.Append(color.a);
        }

        var boundsMaterial = sourceRenderer != null ? sourceRenderer.sharedMaterial : null;
        foreach (var data in batches)
            foreach (string property in BladeBoundsProperties)
            {
                bool present = boundsMaterial != null && boundsMaterial.HasProperty(property);
                data.signature.Append(present ? 1 : 0);
                if (present)
                    data.signature.Append(boundsMaterial.GetFloat(property));
            }

        int cellIndex = 0;
        foreach (var data in batches)
        {
            GrassCell cell;
            if (cellIndex < cells.Count)
                cell = cells[cellIndex];
            else
            {
                // Rebuilding only prepares CPU data. Native meshes and renderers are
                // created when this cell receives a share of the graphics budget.
                cell = new GrassCell();
                cells.Add(cell);
            }

            Hash128 currentSignature = cell.pendingData != null ? cell.pendingData.signature : cell.meshSignature;
            bool canKeepCurrentMesh = (cell.pendingData != null || cell.hasMeshSignature && cell.mesh != null)
                && currentSignature.Equals(data.signature);
            if (!canKeepCurrentMesh)
                cell.pendingData = data;
            cellIndex++;
        }

        for (int index = cells.Count - 1; index >= cellIndex; index--)
        {
            if (cells[index].gameObject != null)
                cells[index].gameObject.SetActive(false);
            DestroyGenerated(cells[index].gameObject);
            if (cells[index].ownsMesh)
                DestroyGenerated(cells[index].mesh);
            cells.RemoveAt(index);
        }

        // Generated cells own the geometry; avoid drawing the old monolithic mesh as well.
        filter.sharedMesh = null;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            SceneView.RepaintAll();
#endif
    }

    public string ComputeMeshCacheSignature()
    {
        // Hash the authored inputs, not object identities, so prefab instances can
        // safely share a cache until their painted geometry or bounds change.
        var signature = new Hash128();
        signature.Append("GeometryGrassCells-v1");
        signature.Append(UploadVertexBudget);
        signature.Append(Mathf.Max(1f, lightingCellSize));
        signature.Append(positions.Count);
        signature.Append(positions);
        signature.Append(normals.Count);
        signature.Append(normals);
        signature.Append(length.Count);
        signature.Append(length);
        signature.Append(colors.Count);
        signature.Append(colors);
        var renderer = sourceRenderer != null ? sourceRenderer : GetComponent<MeshRenderer>();
        var material = renderer != null ? renderer.sharedMaterial : null;
        foreach (string property in BladeBoundsProperties)
        {
            bool present = material != null && material.HasProperty(property);
            signature.Append(present ? 1 : 0);
            if (present)
                signature.Append(material.GetFloat(property));
        }
        return signature.ToString();
    }

    private static readonly string[] BladeBoundsProperties =
    {
        "_GrassHeight", "_RandomHeight", "_GrassWidth", "_Rad",
        "_BladeForward", "_WindStrength"
    };

    private bool TryUseMeshCache()
    {
        if (meshCache == null || meshCache.meshes == null
            || meshCache.signature != preparedSignature)
            return false;

        foreach (var mesh in meshCache.meshes)
            if (mesh == null)
                return false;

        bool alreadyUsingCache = cells.Count == meshCache.meshes.Length;
        for (int index = 0; alreadyUsingCache && index < cells.Count; index++)
            alreadyUsingCache = !cells[index].ownsMesh
                && cells[index].pendingData == null
                && cells[index].mesh == meshCache.meshes[index];

        if (!alreadyUsingCache)
        {
            ReleaseCells();
            foreach (var mesh in meshCache.meshes)
            {
                // Persistent asset meshes follow Unity's normal shared-mesh path;
                // there is no procedural geometry to rebuild or stage here.
                var cell = new GrassCell { mesh = mesh, readyForRendering = true };
                cells.Add(cell);
                CreateCellRenderer(cell);
            }
        }
        nextPendingCell = cells.Count;
        return true;
    }

    private void CreateCellRenderer(GrassCell cell)
    {
        if (cell.gameObject == null)
        {
            var child = new GameObject("Grass lighting cell");
            child.SetActive(false);
            child.hideFlags = HideFlags.HideAndDontSave;
            child.transform.SetParent(transform, false);
            cell.gameObject = child;
            if (cell.mesh == null)
            {
                cell.mesh = new Mesh { name = "Geometry Grass Cell", hideFlags = HideFlags.HideAndDontSave };
                cell.ownsMesh = true;
            }
            child.AddComponent<MeshFilter>().sharedMesh = cell.mesh;
            cell.renderer = child.AddComponent<MeshRenderer>();
            cell.renderer.enabled = false;
            hasPendingActivation = true;
            ApplyRendererSettings(cell);
            child.SetActive(true);
        }
    }

    private void UploadCell(GrassCell cell, CellData data)
    {
        CreateCellRenderer(cell);
        if (!cell.ownsMesh)
        {
            // Painting an instance must never modify the authored cache it shares
            // with another instance. The live preview owns its replacement mesh.
            cell.mesh = new Mesh { name = "Geometry Grass Cell", hideFlags = HideFlags.HideAndDontSave };
            cell.ownsMesh = true;
            cell.gameObject.GetComponent<MeshFilter>().sharedMesh = cell.mesh;
            cell.readyForRendering = false;
            hasPendingActivation = true;
        }
        cell.mesh.Clear();
        cell.mesh.indexFormat = data.positions.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        cell.mesh.SetVertices(data.positions);
        cell.mesh.SetIndices(data.indices, MeshTopology.Points, 0);
        cell.mesh.SetUVs(0, data.sizes);
        cell.mesh.SetColors(data.colors);
        cell.mesh.SetNormals(data.normals);
        cell.mesh.RecalculateBounds();
        ExpandBladeBounds(cell.mesh, data.sizes);
        cell.mesh.UploadMeshData(false);
        cell.meshSignature = data.signature;
        cell.hasMeshSignature = true;
        ApplyRendererSettings(cell);
        cell.gameObject.SetActive(true);
#if UNITY_EDITOR
        if (!Application.isPlaying)
            SceneView.RepaintAll();
#endif
    }

    private void ExpandBladeBounds(Mesh target, List<Vector2> sizes)
    {
        var material = sourceRenderer.sharedMaterial;
        if (material == null || !material.HasProperty("_GrassHeight"))
            return;

        float maxWidth = 1f, maxHeight = 1f;
        foreach (var scale in sizes)
        {
            maxWidth = Mathf.Max(maxWidth, Mathf.Abs(scale.x));
            maxHeight = Mathf.Max(maxHeight, Mathf.Abs(scale.y));
        }
        float extent = Mathf.Abs(material.GetFloat("_GrassHeight")) * maxHeight
            * (1f + Mathf.Abs(material.GetFloat("_RandomHeight")))
            + Mathf.Max(Mathf.Abs(material.GetFloat("_GrassWidth")), 0.25f) * 0.05f * maxWidth
            + Mathf.Abs(material.GetFloat("_Rad"))
            + Mathf.Abs(material.GetFloat("_BladeForward"))
            + 3f * Mathf.Abs(material.GetFloat("_WindStrength")) * 0.025f + 2f;
        var bounds = target.bounds;
        bounds.Expand(2f * extent);
        target.bounds = bounds;
    }
#if UNITY_EDITOR
    public void ClearMesh()
    {
        Undo.RegisterCompleteObjectUndo(this, "Clear Painted Grass");
        i = 0;
        hasLastPaintPosition = false;
        positions = new List<Vector3>();
        indicies = new List<int>();
        colors = new List<Color>();
        normals = new List<Vector3>();
        length = new List<Vector2>();
        BuildSpatialIndex();
        RebuildMesh();
        EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        EditorGrassDataChanged?.Invoke(this);
    }

    private Vector3Int SpatialCell(Vector3 worldPosition)
    {
        return new Vector3Int(
            Mathf.FloorToInt(worldPosition.x / SpatialCellSize),
            Mathf.FloorToInt(worldPosition.y / SpatialCellSize),
            Mathf.FloorToInt(worldPosition.z / SpatialCellSize));
    }

    private void BuildSpatialIndex()
    {
        if (spatialIndex == null)
            spatialIndex = new Dictionary<Vector3Int, HashSet<int>>();
        else
            spatialIndex.Clear();
        for (int index = 0; index < positions.Count; index++)
            AddToSpatialIndex(index, transform.TransformPoint(positions[index]));
        spatialIndexTransform = transform.localToWorldMatrix;
        spatialIndexReady = true;
    }

    private void EnsureSpatialIndex()
    {
        if (!spatialIndexReady || spatialIndexTransform != transform.localToWorldMatrix)
            BuildSpatialIndex();
    }

    private void AddToSpatialIndex(int index, Vector3 worldPosition)
    {
        Vector3Int key = SpatialCell(worldPosition);
        if (spatialIndex == null)
            spatialIndex = new Dictionary<Vector3Int, HashSet<int>>();
        if (!spatialIndex.TryGetValue(key, out var points))
        {
            points = new HashSet<int>();
            spatialIndex.Add(key, points);
        }
        points.Add(index);
    }

    private void RemoveFromSpatialIndex(int index, Vector3 worldPosition)
    {
        Vector3Int key = SpatialCell(worldPosition);
        if (spatialIndex == null || !spatialIndex.TryGetValue(key, out var points))
            return;
        points.Remove(index);
        if (points.Count == 0)
            spatialIndex.Remove(key);
    }

    private List<int> GetSpatialCandidates(Vector3 center, float radius)
    {
        EnsureSpatialIndex();
        float r = Mathf.Max(0f, radius);
        Vector3Int min = SpatialCell(center - Vector3.one * r);
        Vector3Int max = SpatialCell(center + Vector3.one * r);
        var result = new List<int>();
        for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
                for (int z = min.z; z <= max.z; z++)
                    if (spatialIndex.TryGetValue(new Vector3Int(x, y, z), out var points))
                        result.AddRange(points);
        return result;
    }

    private void RemovePointSwapBack(int index)
    {
        int last = positions.Count - 1;
        Vector3 removedWorld = transform.TransformPoint(positions[index]);
        RemoveFromSpatialIndex(index, removedWorld);
        if (index != last)
        {
            Vector3 lastWorld = transform.TransformPoint(positions[last]);
            RemoveFromSpatialIndex(last, lastWorld);
            positions[index] = positions[last];
            colors[index] = colors[last];
            normals[index] = normals[last];
            length[index] = length[last];
            AddToSpatialIndex(index, lastWorld);
        }
        positions.RemoveAt(last);
        colors.RemoveAt(last);
        normals.RemoveAt(last);
        length.RemoveAt(last);
        if (indicies.Count > last)
            indicies.RemoveAt(last);
        i = positions.Count;
    }

    private void OnScene(SceneView scene)
    {
        if (this == null)
            return;
        if (!isActiveAndEnabled || scene == null || scene.camera == null || Event.current == null)
            return;

        Event e = Event.current;
        bool selected = Selection.Contains(gameObject);
        if (e.type == EventType.MouseUp && e.button == 0)
        {
            editorStrokeActive = false;
            EndUndoStroke();
            if (rebuildRequested)
                RebuildMesh();
        }
        if (e.type == EventType.MouseDown && e.button == 0 && e.shift && selected)
        {
            hasLastPaintPosition = false;
            editorStrokeActive = true;
        }

        if (!selected)
        {
            if (editorStrokeActive)
            {
                editorStrokeActive = false;
                EndUndoStroke();
                if (rebuildRequested) RebuildMesh();
            }
            hasBrushHit = false;
            return;
        }

        bool pointerMoved = e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.MouseDown;
        if (pointerMoved)
        {
            mousePos = e.mousePosition;
            float ppp = EditorGUIUtility.pixelsPerPoint;
            mousePos.y = scene.camera.pixelHeight - mousePos.y * ppp;
            mousePos.x *= ppp;
            Ray pointerRay = scene.camera.ScreenPointToRay(mousePos);
            hasBrushHit = Physics.Raycast(pointerRay, out RaycastHit pointerHit, 200f, hitMask.value);
            if (hasBrushHit)
            {
                currentBrushHit = pointerHit;
                hitPosGizmo = pointerHit.point;
                hitNormal = pointerHit.normal;
            }
        }
        if (e.type == EventType.MouseLeaveWindow)
            hasBrushHit = false;
        if (e.type == EventType.Repaint)
            DrawBrushPreview();

        if (e.type == EventType.Layout && e.shift)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        bool stroke = e.type == EventType.MouseDrag && e.button == 0 && e.shift;
        if (stroke && !editorStrokeActive)
        {
            editorStrokeActive = true;
            hasLastPaintPosition = false;
        }
        if (stroke && !spatialIndexReady)
            EnsureSpatialIndex();
        RaycastHit centerHit = currentBrushHit;
        if (!stroke || !hasBrushHit)
            return;

        bool changed = false;
        if (toolbarInt == 0)
        {
            if ((paintMask.value & (1 << centerHit.transform.gameObject.layer)) != 0
                && centerHit.normal.y >= 1f - normalLimit && centerHit.normal.y <= 1f + normalLimit)
            {
                Vector3 normal = centerHit.normal;
                Vector3 tangent = Vector3.Cross(normal, Vector3.forward);
                if (tangent.sqrMagnitude < 0.001f)
                    tangent = Vector3.Cross(normal, Vector3.right);
                tangent.Normalize();
                Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
                int count = Mathf.Max(1, Mathf.CeilToInt(density));
                bool addCenter = !hasLastPaintPosition || Vector3.Distance(centerHit.point, lastPosition) > Mathf.Max(0.01f, brushSize * 0.35f);
                for (int k = 0; k < count && i < grassLimit; k++)
                {
                    Vector3 candidate = centerHit.point;
                    if (k > 0)
                    {
                        float angle = Random.value * Mathf.PI * 2f;
                        float radius = Mathf.Sqrt(Random.value) * Mathf.Max(0f, brushSize);
                        candidate += (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * radius;
                    }
                    else if (!addCenter)
                        continue;

                    Ray sampleRay = new Ray(candidate + normal * 200f, -normal);
                    if (!Physics.Raycast(sampleRay, out RaycastHit sample, 400f, hitMask.value)
                        || (paintMask.value & (1 << sample.transform.gameObject.layer)) == 0
                        || sample.normal.y < 1f - normalLimit || sample.normal.y > 1f + normalLimit)
                        continue;
                    RecordUndoForStroke("Paint Grass");
                    positions.Add(transform.InverseTransformPoint(sample.point));
                    AddToSpatialIndex(positions.Count - 1, sample.point);
                    indicies.Add(i++);
                    length.Add(new Vector2(sizeWidth, sizeLength));
                    colors.Add(new Color(AdjustedColor.r + Random.value * rangeR, AdjustedColor.g + Random.value * rangeG, AdjustedColor.b + Random.value * rangeB, 1f));
                    normals.Add(sample.normal);
                    changed = true;
                }
                if (addCenter)
                {
                    lastPosition = centerHit.point;
                    hasLastPaintPosition = true;
                }
            }
        }
        else if (toolbarInt == 1)
        {
            float radiusSquared = brushSize * brushSize;
            var candidates = new HashSet<int>(GetSpatialCandidates(centerHit.point, brushSize));
            while (candidates.Count > 0)
            {
                int j = -1;
                foreach (int candidate in candidates) { j = candidate; break; }
                candidates.Remove(j);
                if (j < 0 || j >= positions.Count)
                    continue;
                Vector3 worldPosition = transform.TransformPoint(positions[j]);
                if ((centerHit.point - worldPosition).sqrMagnitude > radiusSquared)
                    continue;
                RecordUndoForStroke("Remove Grass");
                int last = positions.Count - 1;
                if (j != last && candidates.Contains(last))
                    candidates.Add(j);
                RemovePointSwapBack(j);
                changed = true;
            }
        }
        else if (toolbarInt == 2)
        {
            float radiusSquared = brushSize * brushSize;
            foreach (int j in GetSpatialCandidates(centerHit.point, brushSize))
            {
                if (j >= positions.Count || (centerHit.point - transform.TransformPoint(positions[j])).sqrMagnitude > radiusSquared)
                    continue;
                Color color = new Color(AdjustedColor.r + Random.value * rangeR, AdjustedColor.g + Random.value * rangeG, AdjustedColor.b + Random.value * rangeB, 1f);
                Vector2 size = new Vector2(sizeWidth, sizeLength);
                if (colors[j] == color && length[j] == size)
                    continue;
                RecordUndoForStroke("Edit Grass");
                colors[j] = color;
                length[j] = size;
                changed = true;
            }
        }

        if (changed)
        {
            rebuildRequested = true;
            if (!brushStrokeMarkedDirty)
            {
                brushStrokeMarkedDirty = true;
                EditorUtility.SetDirty(this);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
                EditorGrassDataChanged?.Invoke(this);
            }
        }
        e.Use();
    }

    private void DrawBrushPreview()
    {
        if (!showBrushPreview || !hasBrushHit)
            return;
        Color oldColor = Handles.color;
        Handles.color = toolbarInt == 1 ? new Color(1f, 0.25f, 0.2f, 0.85f)
            : toolbarInt == 2 ? new Color(1f, 0.75f, 0.15f, 0.85f)
            : new Color(0.2f, 0.9f, 0.45f, 0.85f);
        float radius = Mathf.Max(0.01f, brushSize);
        Handles.DrawWireDisc(hitPosGizmo, hitNormal, radius);
        Handles.Label(hitPosGizmo + hitNormal * 0.08f, toolbarInt == 1 ? "Remove" : toolbarInt == 2 ? "Edit" : "Paint");
        Handles.color = oldColor;
    }

    private void RecordUndoForStroke(string label)
    {
        if (undoRecordedForStroke)
            return;
        Undo.IncrementCurrentGroup();
        undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(label);
        Undo.RegisterCompleteObjectUndo(this, label);
        undoRecordedForStroke = true;
    }

    private void OnUndoRedo()
    {
        if (!Selection.Contains(gameObject))
            return;
        BuildSpatialIndex();
        rebuildRequested = true;
        EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        EditorGrassDataChanged?.Invoke(this);
        SceneView.RepaintAll();
    }

    private void EndUndoStroke()
    {
        if (undoGroup >= 0)
            Undo.CollapseUndoOperations(undoGroup);
        undoGroup = -1;
        undoRecordedForStroke = false;
        brushStrokeMarkedDirty = false;
    }

#endif
}
