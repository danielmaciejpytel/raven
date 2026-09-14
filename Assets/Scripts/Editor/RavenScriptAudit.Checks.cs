using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Raven.Config;
using Raven.Core;
using Raven.Input;
using Raven.Manager;
using Raven.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Zenject;
using Object = UnityEngine.Object;

public static partial class RavenScriptAudit
{
    [Serializable]
    private sealed class CheckReport
    {
        public string unityVersion;
        public string scene;
        public string startedUtc;
        public string status;
        public int passed;
        public int failed;
        public List<string> checks = new List<string>();
        public List<string> runtimeErrors = new List<string>();
    }

    private static CheckReport _checks;
    private static GameObject _checkHost;
    private static double _checkDeadline;

    [MenuItem("Tools/Raven/Script Audit/Run Play Mode Checks %#F9")]
    public static void RunPlayModeChecks()
    {
        if (!Application.isPlaying || _checkHost != null)
        {
            Debug.LogWarning("Start the Level in Play Mode, finish the menu, and run one audit at a time.");
            return;
        }

        SceneContext context = Object.FindFirstObjectByType<SceneContext>();
        if (context == null || context.Container == null) throw new InvalidOperationException("No initialized SceneContext.");
        _checks = new CheckReport
        {
            unityVersion = Application.unityVersion,
            scene = context.gameObject.scene.path,
            startedUtc = DateTime.UtcNow.ToString("O"),
            status = "running"
        };
        WriteChecks();
        _checkHost = new GameObject("Raven script audit (temporary)") { hideFlags = HideFlags.DontSave };
        _checkDeadline = EditorApplication.timeSinceStartup + 120;
        Application.logMessageReceived += RecordRuntimeError;
        EditorApplication.update += CheckTimeout;
        EditorApplication.playModeStateChanged += CheckPlayState;
        _checkHost.AddComponent<CoroutinesManager>().StartCoroutine(GuardChecks(AllChecks(context)), _checks);
    }

    private static IEnumerator AllChecks(SceneContext context)
    {
        int missingScripts = 0;
        foreach (GameObject root in context.gameObject.scene.GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
        Check(missingScripts == 0, "Level has no missing MonoBehaviour scripts (including inactive objects)");
        Check(context.Container.Resolve<InputManager>().GameplayInputEnabled, "Menu has handed control to the player");
        yield return CheckPauseAndInput(context);
        yield return CheckQueriesAndCoroutines();
        yield return CheckMovementAndCombat(context);
        yield return CheckInteractions(context);
        yield return CheckNavigation(context);
    }

    // Flatten nested iterators so an exception in any check still produces a report
    // and disposes its fixture. Unity handles yielded wait instructions normally.
    private static IEnumerator GuardChecks(IEnumerator first)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(first);
        try
        {
            while (stack.Count > 0)
            {
                IEnumerator current = stack.Peek();
                bool moved = false;
                Exception failure = null;
                try { moved = current.MoveNext(); }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    Check(false, failure.ToString());
                    break;
                }
                if (!moved)
                {
                    (stack.Pop() as IDisposable)?.Dispose();
                    continue;
                }
                if (current.Current is IEnumerator nested) stack.Push(nested);
                else yield return current.Current;
            }
        }
        finally
        {
            while (stack.Count > 0)
            {
                try { (stack.Pop() as IDisposable)?.Dispose(); }
                catch (Exception error) { Check(false, "Fixture cleanup: " + error); }
            }
            FinishChecks();
        }
    }

    private static void Check(bool condition, string description)
    {
        if (condition) _checks.passed++;
        else _checks.failed++;
        _checks.checks.Add((condition ? "PASS: " : "FAIL: ") + description);
        WriteChecks();
    }

    private static void RecordRuntimeError(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            _checks.runtimeErrors.Add(message + "\n" + trace);
    }

    private static void CheckTimeout()
    {
        if (EditorApplication.timeSinceStartup <= _checkDeadline) return;
        Check(false, "Runtime checks exceeded 120 seconds");
        FinishChecks();
    }

    private static void CheckPlayState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode) return;
        Check(false, "Play Mode ended before checks finished");
        FinishChecks();
    }

    private static void FinishChecks()
    {
        if (_checks == null || _checks.status != "running") return;
        Application.logMessageReceived -= RecordRuntimeError;
        EditorApplication.update -= CheckTimeout;
        EditorApplication.playModeStateChanged -= CheckPlayState;
        _checks.status = _checks.failed == 0 && _checks.runtimeErrors.Count == 0 ? "passed" : "failed";
        WriteChecks();
        if (_checkHost != null) Object.Destroy(_checkHost);
        _checkHost = null;
        Debug.Log($"Raven script audit: {_checks.status}, {_checks.passed} passed, {_checks.failed} failed, {_checks.runtimeErrors.Count} runtime errors. Logs/RavenScriptAudit-tests.json");
    }

    private static void WriteChecks()
    {
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/RavenScriptAudit-tests.json", JsonUtility.ToJson(_checks, true));
    }

    private static IEnumerator CheckPauseAndInput(SceneContext context)
    {
        InputManager input = context.Container.Resolve<InputManager>();
        CameraManager camera = context.Container.Resolve<CameraManager>();
        PauseEndPanel panel = Object.FindFirstObjectByType<PauseEndPanel>();
        if (panel == null) throw new InvalidOperationException("Pause panel must be active for the audit.");
        GameObject hud = Field<GameObject>(panel, "_HUD");
        float previousScale = Time.timeScale;
        bool previousCursorVisible = Cursor.visible;
        CursorLockMode previousCursorLock = Cursor.lockState;
        bool panelEnabled = panel.enabled;
        bool hudActive = hud.activeSelf;
        bool aiming = Field<bool>(camera, "_isAiming");
        try
        {
            Time.timeScale = 0.35f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            panel.PauseGame();
            panel.PauseGame();
            camera.Tick();
            Check(Time.timeScale == 0f && !input.GameplayInputEnabled, "Pause is idempotent and blocks gameplay input");
            Check(input.GetMovementAxis() == Vector2.zero && input.GetMouseDelta() == Vector2.zero
                && !input.ShootButtonPressed() && !input.DashButtonPressed() && !input.AimButtonHold(), "Movement, look, shooting, dash and aim are gated during pause");
            Check(Field<bool>(camera, "_isAiming") == aiming, "Pause preserves the camera aim state");
            panel.BUTTON_Resume();
            Check(Mathf.Approximately(Time.timeScale, 0.35f) && hud.activeSelf == hudActive
                && Cursor.visible && Cursor.lockState == CursorLockMode.None, "Resume restores prior time scale, HUD and cursor");
            panel.PauseGame();
            panel.enabled = false;
            Check(Mathf.Approximately(Time.timeScale, 0.35f) && !Field<bool>(panel, "_panelActive"), "Disabling a paused panel releases its pause and active flag");
            panel.enabled = true;
            panel.PauseGame();
            Check(Time.timeScale == 0f, "Re-enabled pause panel can pause again");
            panel.BUTTON_Resume();
        }
        finally
        {
            panel.BUTTON_Resume();
            panel.enabled = panelEnabled;
            hud.SetActive(hudActive);
            Time.timeScale = previousScale;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        var pauseControls = Field<Controls>(input, "_controls");
        var oldDevices = pauseControls.devices;
        Keyboard previousKeyboard = Keyboard.current;
        var pauseKeyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            pauseControls.devices = new InputDevice[] { pauseKeyboard };
            var updatePanel = typeof(PauseEndPanel).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            InputSystem.QueueStateEvent(pauseKeyboard, new KeyboardState(Key.Escape));
            InputSystem.Update();
            bool escapePerformed = input.EscTrigerred();
            updatePanel.Invoke(panel, null);
            Check(escapePerformed && Time.timeScale == 0f, "Escape input binding opens the live pause panel through its Update");
            InputSystem.QueueStateEvent(pauseKeyboard, new KeyboardState());
            InputSystem.Update();
            InputSystem.QueueStateEvent(pauseKeyboard, new KeyboardState(Key.Escape));
            InputSystem.Update();
            updatePanel.Invoke(panel, null);
            Check(Mathf.Approximately(Time.timeScale, previousScale) && !Field<bool>(panel, "_panelActive"),
                "Escape input binding resumes the live pause panel while gameplay input is blocked");
        }
        finally
        {
            panel.BUTTON_Resume();
            pauseControls.devices = oldDevices;
            InputSystem.RemoveDevice(pauseKeyboard);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        }

        // A dedicated device and camera isolate small-pointer input from the player.
        var fixture = new GameObject("Audit camera fixture");
        var pointer = InputSystem.AddDevice<Mouse>();
        var config = ScriptableObject.CreateInstance<MovementConfig>();
        CameraManager isolatedCamera = null;
        try
        {
            var isolatedInput = fixture.AddComponent<InputManager>();
            Field<Controls>(isolatedInput, "_controls").asset.devices = new InputDevice[] { pointer };
            isolatedInput.CanInput = true;
            typeof(MovementConfig).GetField("_fppMouseSensitivity", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(config, 5f);
            var aim = new GameObject("Aim");
            aim.transform.SetParent(fixture.transform);
            var target = new GameObject("Target");
            target.transform.SetParent(fixture.transform);
            isolatedCamera = new CameraManager(isolatedInput, aim, null, fixture, fixture.transform, target, config);
            InputSystem.QueueDeltaStateEvent(pointer.delta, new Vector2(0.25f, 0.1f));
            InputSystem.Update();
            Check(isolatedInput.IsPointerLook, "Pointer input is recognized separately from stick input");
            typeof(CameraManager).GetMethod("ShootCameraRotation", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(isolatedCamera, null);
            Check(Mathf.Abs(Field<float>(isolatedCamera, "_cinemachineTargetYaw") - 0.25f * 5f / 60f) < 0.0001f,
                "Subpixel pointer movement rotates the aiming camera with calibrated displacement gain");
        }
        finally
        {
            isolatedCamera?.Dispose();
            InputSystem.RemoveDevice(pointer);
            Object.Destroy(fixture);
            Object.Destroy(config);
        }
        yield return null;
    }

    private static IEnumerator CheckQueriesAndCoroutines()
    {
        var fixture = new GameObject("Audit physics and coroutine fixture");
        try
        {
            Vector3 origin = new Vector3(10000f, 10000f, 10000f);
            for (int i = 0; i < 20; i++)
            {
                var box = new GameObject("Query collider " + i);
                box.transform.SetParent(fixture.transform);
                box.transform.position = origin + Vector3.forward * i * 0.5f;
                box.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;
            }
            Physics.SyncTransforms();
            Collider[] overlap = new Collider[2];
            Check(PhysicsQueries.OverlapSphere(origin, 12f, ref overlap) == 20, "Overlap query grows a saturated buffer and returns all 20 colliders");
            Collider[] reused = overlap;
            Check(PhysicsQueries.OverlapSphere(origin, 12f, ref overlap) == 20 && ReferenceEquals(reused, overlap), "Subsequent overlap query reuses its expanded buffer");
            RaycastHit[] hits = new RaycastHit[2];
            Check(PhysicsQueries.Raycast(origin - Vector3.forward, Vector3.forward, ref hits, 12f) == 20, "Ray query grows a saturated buffer and returns all 20 colliders");

            CoroutinesManager manager = fixture.AddComponent<CoroutinesManager>();
            object owner = new object();
            var lookup = Field<Dictionary<object, HashSet<Coroutine>>>(manager, "coroutinesLookup");
            manager.StartCoroutine(AuditImmediate(), owner);
            Check(lookup.Count == 0, "Synchronously completed coroutine retains no publisher");
            manager.StartCoroutine(AuditOneFrame(), owner);
            yield return null;
            yield return null;
            Check(lookup.Count == 0, "Naturally completed coroutine releases publisher tracking");
            bool callbackRan = false;
            manager.StartCoroutine(AuditDelayed(() => callbackRan = true), owner);
            manager.DestroyPublisher(owner);
            yield return new WaitForSeconds(0.1f);
            Check(!callbackRan && lookup.Count == 0, "DestroyPublisher cancels work and removes ownership");
            manager.StartCoroutine(AuditDelayed(() => callbackRan = true), owner);
            manager.enabled = false;
            yield return new WaitForSeconds(0.1f);
            Check(!callbackRan && lookup.Count == 0, "Disabling coroutine manager cancels work and clears tracking");
        }
        finally { Object.Destroy(fixture); }
    }

    private static IEnumerator AuditImmediate() { yield break; }
    private static IEnumerator AuditOneFrame() { yield return null; }
    private static IEnumerator AuditDelayed(Action callback)
    {
        yield return new WaitForSeconds(0.05f);
        callback();
    }
}
