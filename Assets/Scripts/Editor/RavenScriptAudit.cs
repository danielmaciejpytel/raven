using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Raven.Input;
using Raven.Manager;
using Raven.Player;
using UnityEditor;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

// Editor-only diagnostics. Reports are written outside Assets and never save the scene.
public static partial class RavenScriptAudit
{
    [MenuItem("Tools/Raven/Script Audit/Enable Native Leak Traces %#F10")]
    public static void EnableNativeLeakTraces()
    {
        Unity.Collections.NativeLeakDetection.Mode = Unity.Collections.NativeLeakDetectionMode.EnabledWithStackTrace;
        Debug.Log("Raven script audit: native leak stack traces enabled.");
    }

    [MenuItem("Tools/Raven/Script Audit/Use Standard Native Leak Detection %#F11")]
    public static void UseStandardNativeLeakDetection()
    {
        Unity.Collections.NativeLeakDetection.Mode = Unity.Collections.NativeLeakDetectionMode.Enabled;
    }

    [MenuItem("Tools/Raven/Script Audit/Reload Scripts for Leak Check")]
    public static void ReloadScriptsForLeakCheck()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            Debug.LogWarning("Exit Play Mode and wait for compilation before checking script reload leaks.");
            return;
        }

        EnableNativeLeakTraces();
        Debug.Log("Raven script audit: requesting script reload for native leak check.");
        EditorUtility.RequestScriptReload();
    }

    [Serializable]
    private sealed class StateReport
    {
        public string unityVersion;
        public string capturedUtc;
        public string nativeLeakDetection;
        public bool playing;
        public string graphicsDevice;
        public float timeScale;
        public string scene;
        public List<string> observations = new List<string>();
    }

    [MenuItem("Tools/Raven/Script Audit/Dump Runtime State %#F8")]
    public static void DumpRuntimeState()
    {
        var report = new StateReport
        {
            unityVersion = Application.unityVersion,
            capturedUtc = DateTime.UtcNow.ToString("O"),
            nativeLeakDetection = Unity.Collections.NativeLeakDetection.Mode.ToString(),
            playing = Application.isPlaying,
            graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
            timeScale = Time.timeScale,
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path
        };

        foreach (InputManager input in Object.FindObjectsByType<InputManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.observations.Add($"Input: {input.name}, enabled={input.isActiveAndEnabled}, CanInput={input.CanInput}");
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.observations.Add($"Canvas: {HierarchyPath(canvas.transform)}, active={canvas.gameObject.activeInHierarchy}, enabled={canvas.enabled}");
        foreach (Raven.Core.Menu menu in Object.FindObjectsByType<Raven.Core.Menu>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.observations.Add($"Menu: {HierarchyPath(menu.transform)}, active={menu.isActiveAndEnabled}");
        foreach (PauseEndPanel panel in Object.FindObjectsByType<PauseEndPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            GameObject menuCamera = Field<GameObject>(panel, "_menuCamera");
            report.observations.Add($"Pause: active={panel.isActiveAndEnabled}, open={Field<bool>(panel, "_panelActive")}, menuCameraActive={menuCamera != null && menuCamera.activeSelf}");
        }
        foreach (PlayerHudReferences hud in Object.FindObjectsByType<PlayerHudReferences>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.observations.Add($"HUD: active={hud.gameObject.activeInHierarchy}, viewFinder={hud.ViewFinder != null && hud.ViewFinder.gameObject.activeInHierarchy}");
        foreach (CharacterController controller in Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.observations.Add($"Controller: {HierarchyPath(controller.transform)}, active={controller.enabled && controller.gameObject.activeInHierarchy}, position={controller.transform.position}, grounded={controller.isGrounded}");
        foreach (EnemyController enemy in Object.FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.observations.Add($"Enemy: {HierarchyPath(enemy.transform)}, active={enemy.isActiveAndEnabled}, hp={Field<float>(enemy, "_currentHealth")}, renderer={Field<SkinnedMeshRenderer>(enemy, "_gfxSkinnedMesh") != null}");

        SceneContext context = Object.FindFirstObjectByType<SceneContext>();
        if (Application.isPlaying && context != null && context.Container != null)
        {
            PlayerMovementManager movement = context.Container.TryResolve<PlayerMovementManager>();
            PlayerStatesManager states = context.Container.TryResolve<PlayerStatesManager>();
            if (movement != null)
                report.observations.Add($"Movement: dash={movement.Dash}, aim={movement.Fpp}, position={movement.PlayerTransform.position}");
            if (states != null)
                report.observations.Add($"Combat: state={states.CurrentConfig.PlayerStateName}");
        }

        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/RavenScriptAudit-state.json", JsonUtility.ToJson(report, true));
        Debug.Log("Raven script audit: runtime state written to Logs/RavenScriptAudit-state.json");
    }

    private static T Field<T>(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field == null) throw new MissingFieldException(instance.GetType().FullName, name);
        return (T)field.GetValue(instance);
    }

    private static string HierarchyPath(Transform target)
    {
        string path = target.name;
        for (Transform parent = target.parent; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;
        return path;
    }
}
