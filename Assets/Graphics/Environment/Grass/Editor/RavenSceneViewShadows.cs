using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Scene navigation needs a longer range than the gameplay camera. Restore the
// pipeline setting after each Scene View render so it never becomes a build setting.
[InitializeOnLoad]
internal static class RavenSceneViewShadows
{
    private struct SavedState
    {
        public UniversalRenderPipelineAsset asset;
        public float distance;
    }

    private static readonly Dictionary<Camera, SavedState> saved = new Dictionary<Camera, SavedState>();

    static RavenSceneViewShadows()
    {
        RenderPipelineManager.beginCameraRendering += Begin;
        RenderPipelineManager.endCameraRendering += End;
        AssemblyReloadEvents.beforeAssemblyReload += RestoreAll;
        EditorApplication.quitting += RestoreAll;
    }

    private static void Begin(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType != CameraType.SceneView || saved.ContainsKey(camera)) return;
        var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (asset == null) return;
        saved.Add(camera, new SavedState { asset = asset, distance = asset.shadowDistance });
        asset.shadowDistance = Mathf.Max(asset.shadowDistance, 250f);
    }

    private static void End(ScriptableRenderContext context, Camera camera)
    {
        if (!saved.TryGetValue(camera, out var state)) return;
        if (state.asset != null) state.asset.shadowDistance = state.distance;
        saved.Remove(camera);
    }

    private static void RestoreAll()
    {
        foreach (var state in saved.Values)
            if (state.asset != null) state.asset.shadowDistance = state.distance;
        saved.Clear();
    }
}
