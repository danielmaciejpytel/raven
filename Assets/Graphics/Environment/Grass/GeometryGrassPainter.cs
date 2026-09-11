using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[ExecuteInEditMode]
public class GeometryGrassPainter : MonoBehaviour
{

    [Min(1f)]
    [Tooltip("Size of local grass cells. Each cell gets its own URP Forward light list.")]
    public float lightingCellSize = 8f;

    private sealed class GrassCell
    {
        public GameObject gameObject;
        public Mesh mesh;
        public MeshRenderer renderer;
    }

    private sealed class CellData
    {
        public readonly List<Vector3> positions = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> sizes = new List<Vector2>();
        public readonly List<Color> colors = new List<Color>();
        public readonly List<int> indices = new List<int>();
    }

    private readonly List<GrassCell> cells = new List<GrassCell>();
    private MeshRenderer sourceRenderer;
    private bool rebuildRequested;

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

    Vector3 mousePos;

    [HideInInspector]
    public Vector3 hitPosGizmo;

    Vector3 hitPos;

    [HideInInspector]
    public Vector3 hitNormal;

    private void OnEnable()
    {
        filter = GetComponent<MeshFilter>();
        sourceRenderer = GetComponent<MeshRenderer>();
        RebuildMesh();
#if UNITY_EDITOR
        SceneView.duringSceneGui -= OnScene;
        SceneView.duringSceneGui += OnScene;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        SceneView.duringSceneGui -= OnScene;
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
#if UNITY_EDITOR
        SceneView.duringSceneGui -= OnScene;
#endif
        ReleaseCells();
    }

    private void ReleaseCells()
    {
        foreach (var cell in cells)
        {
            if (cell.gameObject != null)
                cell.gameObject.SetActive(false);
            DestroyGenerated(cell.mesh);
            DestroyGenerated(cell.gameObject);
        }
        cells.Clear();
    }

    private void LateUpdate()
    {
        if (rebuildRequested)
            RebuildMesh();
        // Keep the original renderer as the source of material/layer settings.
        if (sourceRenderer == null)
            return;
        foreach (var cell in cells)
            ApplyRendererSettings(cell);
    }

    private void ApplyRendererSettings(GrassCell cell)
    {
        if (cell.renderer == null)
            return;
        cell.gameObject.layer = gameObject.layer;
        cell.renderer.enabled = sourceRenderer.enabled && isActiveAndEnabled;
        if (cell.renderer.sharedMaterial != sourceRenderer.sharedMaterial)
            cell.renderer.sharedMaterial = sourceRenderer.sharedMaterial;
        cell.renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        cell.renderer.receiveShadows = sourceRenderer.receiveShadows;
        cell.renderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
        cell.renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        cell.renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        cell.renderer.probeAnchor = sourceRenderer.probeAnchor;
        cell.renderer.allowOcclusionWhenDynamic = sourceRenderer.allowOcclusionWhenDynamic;
        cell.renderer.sortingLayerID = sourceRenderer.sortingLayerID;
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
        var groups = new Dictionary<Vector3Int, CellData>();
        float cellSize = Mathf.Max(1f, lightingCellSize);
        i = positions.Count;
        indicies.Clear();
        for (int index = 0; index < positions.Count; index++)
        {
            indicies.Add(index);
            Vector3 position = positions[index];
            var key = new Vector3Int(Mathf.FloorToInt(position.x / cellSize),
                Mathf.FloorToInt(position.y / cellSize), Mathf.FloorToInt(position.z / cellSize));
            if (!groups.TryGetValue(key, out var data))
            {
                data = new CellData();
                groups.Add(key, data);
            }
            data.indices.Add(data.positions.Count);
            data.positions.Add(position);
            data.normals.Add(index < normals.Count ? normals[index] : Vector3.up);
            data.sizes.Add(index < length.Count ? length[index] : Vector2.one);
            data.colors.Add(index < colors.Count ? colors[index] : Color.white);
        }

        int cellIndex = 0;
        foreach (var data in groups.Values)
        {
            GrassCell cell;
            if (cellIndex < cells.Count)
                cell = cells[cellIndex];
            else
            {
                var child = new GameObject("Grass lighting cell");
                child.SetActive(false);
                child.hideFlags = HideFlags.HideAndDontSave;
                child.transform.SetParent(transform, false);
                var generatedMesh = new Mesh { name = "Geometry Grass Cell", hideFlags = HideFlags.HideAndDontSave };
                child.AddComponent<MeshFilter>().sharedMesh = generatedMesh;
                cell = new GrassCell { gameObject = child, mesh = generatedMesh,
                    renderer = child.AddComponent<MeshRenderer>() };
                cells.Add(cell);
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
            ApplyRendererSettings(cell);
            cell.gameObject.SetActive(true);
            cellIndex++;
        }

        for (int index = cells.Count - 1; index >= cellIndex; index--)
        {
            cells[index].gameObject.SetActive(false);
            DestroyGenerated(cells[index].mesh);
            DestroyGenerated(cells[index].gameObject);
            cells.RemoveAt(index);
        }

        // Generated cells own the geometry; avoid drawing the old monolithic mesh as well.
        filter.sharedMesh = null;
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
            + Mathf.Abs(material.GetFloat("_GrassWidth")) * maxWidth
            + Mathf.Abs(material.GetFloat("_Rad"))
            + Mathf.Abs(material.GetFloat("_BladeForward"))
            + 3f * Mathf.Abs(material.GetFloat("_WindStrength")) + 2f;
        var bounds = target.bounds;
        bounds.Expand(2f * extent);
        target.bounds = bounds;
    }
#if UNITY_EDITOR
    public void ClearMesh()
    {
        i = 0;
        positions = new List<Vector3>();
        indicies = new List<int>();
        colors = new List<Color>();
        normals = new List<Vector3>();
        length = new List<Vector2>();
        RebuildMesh();
        EditorUtility.SetDirty(this);
    }

    void OnScene(SceneView scene)
    {
        // A queued editor callback can outlive its component during a reload.
        if (this == null)
        {
            SceneView.duringSceneGui -= OnScene;
            return;
        }

        if (!isActiveAndEnabled || scene == null || scene.camera == null || Event.current == null)
            return;

        // only allow painting while this object is selected
        if ((Selection.Contains(gameObject)))
        {

            Event e = Event.current;
            RaycastHit terrainHit;
            mousePos = e.mousePosition;
            float ppp = EditorGUIUtility.pixelsPerPoint;
            mousePos.y = scene.camera.pixelHeight - mousePos.y * ppp;
            mousePos.x *= ppp;

            // ray for gizmo(disc)
            Ray rayGizmo = scene.camera.ScreenPointToRay(mousePos);
            RaycastHit hitGizmo;

            if (Physics.Raycast(rayGizmo, out hitGizmo, 200f, hitMask.value))
            {
                hitPosGizmo = hitGizmo.point;
            }

            if (e.type == EventType.MouseDrag && e.button == 1 && toolbarInt == 0)
            {
                // place based on density
                for (int k = 0; k < density; k++)
                {

                    // brushrange
                    float t = 2f * Mathf.PI * Random.Range(0f, brushSize);
                    float u = Random.Range(0f, brushSize) + Random.Range(0f, brushSize);
                    float r = (u > 1 ? 2 - u : u);
                    Vector3 origin = Vector3.zero;

                    // place random in radius, except for first one
                    if (k != 0)
                    {
                        origin.x += r * Mathf.Cos(t);
                        origin.y += r * Mathf.Sin(t);
                    }
                    else
                    {
                        origin = Vector3.zero;
                    }

                    // add random range to ray
                    Ray ray = scene.camera.ScreenPointToRay(mousePos);
                    ray.origin += origin;

                    // if the ray hits something thats on the layer mask,  within the grass limit and within the y normal limit
                    if (Physics.Raycast(ray, out terrainHit, 200f, hitMask.value) && i < grassLimit && terrainHit.normal.y <= (1 + normalLimit) && terrainHit.normal.y >= (1 - normalLimit))
                    {
                        if ((paintMask.value & (1 << terrainHit.transform.gameObject.layer)) > 0)
                        {
                            hitPos = terrainHit.point;
                            hitNormal = terrainHit.normal;
                            if (k != 0)
                            {
                                var grassPosition = hitPos;// + Vector3.Cross(origin, hitNormal);
                                grassPosition -= this.transform.position;

                                positions.Add((grassPosition));
                                indicies.Add(i);
                                length.Add(new Vector2(sizeWidth, sizeLength));
                                // add random color variations                          
                                colors.Add(new Color(AdjustedColor.r + (Random.Range(0, 1.0f) * rangeR), AdjustedColor.g + (Random.Range(0, 1.0f) * rangeG), AdjustedColor.b + (Random.Range(0, 1.0f) * rangeB), 1));

                                //colors.Add(temp);
                                normals.Add(terrainHit.normal);
                                i++;
                            }
                            else
                            {// to not place everything at once, check if the first placed point far enough away from the last placed first one
                                if (Vector3.Distance(terrainHit.point, lastPosition) > brushSize)
                                {
                                    var grassPosition = hitPos;
                                    grassPosition -= this.transform.position;
                                    positions.Add((grassPosition));
                                    indicies.Add(i);
                                    length.Add(new Vector2(sizeWidth, sizeLength));
                                    colors.Add(new Color(AdjustedColor.r + (Random.Range(0, 1.0f) * rangeR), AdjustedColor.g + (Random.Range(0, 1.0f) * rangeG), AdjustedColor.b + (Random.Range(0, 1.0f) * rangeB), 1));
                                    normals.Add(terrainHit.normal);
                                    i++;

                                    if (origin == Vector3.zero)
                                    {
                                        lastPosition = hitPos;
                                    }
                                }
                            }
                        }

                    }

                }
                e.Use();
            }
            // removing mesh points
            if (e.type == EventType.MouseDrag && e.button == 1 && toolbarInt == 1)
            {
                Ray ray = scene.camera.ScreenPointToRay(mousePos);

                if (Physics.Raycast(ray, out terrainHit, 200f, hitMask.value))
                {
                    hitPos = terrainHit.point;
                    hitPosGizmo = hitPos;
                    hitNormal = terrainHit.normal;
                    for (int j = 0; j < positions.Count; j++)
                    {
                        Vector3 pos = positions[j];

                        pos += this.transform.position;
                        float dist = Vector3.Distance(terrainHit.point, pos);

                        // if its within the radius of the brush, remove all info
                        if (dist <= brushSize)
                        {
                            positions.RemoveAt(j);
                            colors.RemoveAt(j);
                            normals.RemoveAt(j);
                            length.RemoveAt(j);
                            indicies.RemoveAt(j);
                            i--;
                            for (int i = 0; i < indicies.Count; i++)
                            {
                                indicies[i] = i;
                            }
                        }
                    }
                }
                e.Use();
            }

            if (e.type == EventType.MouseDrag && e.button == 1 && toolbarInt == 2)
            {
                Ray ray = scene.camera.ScreenPointToRay(mousePos);

                if (Physics.Raycast(ray, out terrainHit, 200f, hitMask.value))
                {
                    hitPos = terrainHit.point;
                    hitPosGizmo = hitPos;
                    hitNormal = terrainHit.normal;
                    for (int j = 0; j < positions.Count; j++)
                    {
                        Vector3 pos = positions[j];

                        pos += this.transform.position;
                        float dist = Vector3.Distance(terrainHit.point, pos);

                        // if its within the radius of the brush, remove all info
                        if (dist <= brushSize)
                        {

                            colors[j] = (new Color(AdjustedColor.r + (Random.Range(0, 1.0f) * rangeR), AdjustedColor.g + (Random.Range(0, 1.0f) * rangeG), AdjustedColor.b + (Random.Range(0, 1.0f) * rangeB), 1));

                            length[j] = new Vector2(sizeWidth, sizeLength);

                        }
                    }
                }
                e.Use();
            }
            // Avoid allocating a new mesh on every SceneView repaint.
            if (e.type == EventType.Used)
            {
                RebuildMesh();
                EditorUtility.SetDirty(this);
                if (!Application.isPlaying)
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }

        }
    }
#endif
}
