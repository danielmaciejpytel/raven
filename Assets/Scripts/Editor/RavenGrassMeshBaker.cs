using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class RavenGrassMeshBaker
{
    private const string Folder = "Assets/Graphics/Environment/Grass/Baked";
    private static readonly HashSet<GeometryGrassPainter> AutoBakeQueue = new HashSet<GeometryGrassPainter>();
    private static double nextAutoBakeCheck;
    private static bool autoBakeUpdateRegistered;

    static RavenGrassMeshBaker()
    {
        Selection.selectionChanged += OnSelectionChanged;
        GeometryGrassPainter.EditorGrassDataChanged += MarkPainterChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode)
            return;

        var painters = UnityEngine.Object.FindObjectsByType<GeometryGrassPainter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (painters.Length == 0)
            return;

        try
        {
            BakePainters(painters, saveScenes: true);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("Raven grass: Play Mode start was cancelled because grass meshes were not ready to bake. Wait for grass mesh uploads to finish, then enter Play Mode again.");
            EditorApplication.isPlaying = false;
        }
    }

    public static void MarkPainterChanged(GeometryGrassPainter painter)
    {
        if (painter != null)
        {
            AutoBakeQueue.Add(painter);
            OnSelectionChanged();
        }
    }

    private static void OnSelectionChanged()
    {
        if (!autoBakeUpdateRegistered && AutoBakeQueue.Any(painter => painter != null && !Selection.Contains(painter.gameObject)))
        {
            EditorApplication.update += ProcessAutoBakeQueue;
            autoBakeUpdateRegistered = true;
        }
    }

    private static void ProcessAutoBakeQueue()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (EditorApplication.timeSinceStartup < nextAutoBakeCheck)
            return;
        nextAutoBakeCheck = EditorApplication.timeSinceStartup + 0.5d;

        var exitedPainters = AutoBakeQueue
            .Where(painter => painter == null || !Selection.Contains(painter.gameObject))
            .ToArray();
        if (exitedPainters.Length == 0)
        {
            EditorApplication.update -= ProcessAutoBakeQueue;
            autoBakeUpdateRegistered = false;
            return;
        }

        var validPainters = exitedPainters.Where(painter => painter != null).ToArray();
        if (validPainters.Any(painter => !painter.AreMeshUploadsComplete))
            return;

        foreach (var painter in exitedPainters)
            AutoBakeQueue.Remove(painter);
        EditorApplication.update -= ProcessAutoBakeQueue;
        autoBakeUpdateRegistered = false;

        try
        {
            BakePainters(validPainters, saveScenes: false);
            if (validPainters.Length > 0)
                Debug.Log("Raven grass: automatically baked changed geometry after leaving the grass painter. Save the scene to keep the updated cache reference.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            foreach (var painter in validPainters)
                AutoBakeQueue.Add(painter);
        }
    }

    [MenuItem("Raven/Grass/Bake meshes in open scenes")]
    public static void BakeOpenScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Bake grass outside Play Mode.");

        var painters = UnityEngine.Object.FindObjectsByType<GeometryGrassPainter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        BakePainters(painters, saveScenes: true);
    }

    private static void BakePainters(IEnumerable<GeometryGrassPainter> painters, bool saveScenes)
    {

        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Graphics/Environment/Grass", "Baked");

        var scenes = new HashSet<Scene>();
        int baked = 0;
        foreach (var painter in painters.Where(painter => painter != null))
        {
            var scene = painter.gameObject.scene;
            if (!scene.IsValid() || !scene.path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                continue;

            painter.PrepareMeshCacheBake();
            var meshes = painter.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.transform.parent == painter.transform && f.name == "Grass lighting cell")
                .Select(f => f.sharedMesh).ToArray();
            var serialized = new SerializedObject(painter);
            int seedCount = serialized.FindProperty("positions").arraySize;
            if (!painter.IsMeshCacheBakeReady || meshes.Any(m => m == null)
                || meshes.Sum(m => m.vertexCount) != seedCount)
                throw new InvalidOperationException("Wait for grass geometry to finish preparing: " + painter.name);

            var id = GlobalObjectId.GetGlobalObjectIdSlow(painter);
            string path = Folder + "/Grass_" + id.assetGUID + "_" + id.targetObjectId + "_" + id.targetPrefabId + ".asset";
            var cache = AssetDatabase.LoadAssetAtPath<GrassMeshCache>(path);
            if (cache == null)
            {
                cache = ScriptableObject.CreateInstance<GrassMeshCache>();
                cache.name = painter.name + " baked geometry";
                AssetDatabase.CreateAsset(cache, path);
            }

            var previous = cache.meshes ?? Array.Empty<Mesh>();
            var stored = new Mesh[meshes.Length];
            for (int i = 0; i < meshes.Length; i++)
            {
                var destination = i < previous.Length ? previous[i] : null;
                if (destination == null)
                {
                    destination = UnityEngine.Object.Instantiate(meshes[i]);
                    destination.hideFlags = HideFlags.None;
                    destination.name = "Grass cell " + i;
                    AssetDatabase.AddObjectToAsset(destination, cache);
                }
                else if (destination != meshes[i])
                {
                    EditorUtility.CopySerialized(meshes[i], destination);
                    destination.hideFlags = HideFlags.None;
                    destination.name = "Grass cell " + i;
                }
                stored[i] = destination;
                EditorUtility.SetDirty(destination);
            }
            for (int i = meshes.Length; i < previous.Length; i++)
                if (previous[i] != null) UnityEngine.Object.DestroyImmediate(previous[i], true);

            cache.meshes = stored;
            cache.signature = painter.ComputeMeshCacheSignature();
            EditorUtility.SetDirty(cache);
            AssetDatabase.SaveAssetIfDirty(cache);
            serialized.FindProperty("meshCache").objectReferenceValue = cache;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(painter);
            EditorUtility.SetDirty(painter);
            EditorSceneManager.MarkSceneDirty(scene);
            scenes.Add(scene);
            baked++;
        }

        if (saveScenes)
            foreach (var scene in scenes)
                EditorSceneManager.SaveScene(scene);
        Debug.Log("Raven grass: saved persistent geometry for " + baked + " painters.");
    }
}
