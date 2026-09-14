#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Cinemachine;
using UnityEngine.InputSystem;

namespace Raven.Editor
{
    public static class CinemachineUpgradeUtility
    {
        private static Transform FindSceneCamera(string currentName, string legacyName)
        {
            foreach (var camera in Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (camera.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()
                    && (camera.name == currentName || camera.name == legacyName)) return camera.transform;
            return null;
        }
        [MenuItem("Tools/Upgrade Cinemachine Cameras")]
        public static void PerformUpgrade()
        {
            var allSubAssets = AssetDatabase.LoadAllAssetsAtPath("Assets/Scripts/Core/Input/Controls.inputactions");
            InputActionReference lookActionRef = null;
            foreach (var sub in allSubAssets)
            {
                if (sub is InputActionReference iar && iar.name == "Player/CameraLook")
                {
                    lookActionRef = iar;
                    break;
                }
            }
            Debug.Log($"Found Player/CameraLook action ref: {lookActionRef != null}");

            var mainCamGO = (GameObject.Find("MainCamera") ?? GameObject.Find("Main Camera"));
            var playerGO = GameObject.Find("Player");
            var tppCameraLookGO = GameObject.Find("TppCameraLook");
            var shootLockGO = GameObject.Find("ShootCameraLock");
            var sceneContextGO = GameObject.Find("SceneContext");
            var menuCamGO = GameObject.Find("MenuCamera");

            if (menuCamGO != null)
            {
                var cmChild = menuCamGO.transform.Find("cm");
                if (cmChild != null) Object.DestroyImmediate(cmChild.gameObject);

                var menuCmCam = menuCamGO.GetComponent<CinemachineCamera>();
                if (menuCmCam == null) menuCmCam = menuCamGO.AddComponent<CinemachineCamera>();
                menuCmCam.Priority = 30;
                Debug.Log("MenuCamera configured in scene.");
            }

            CinemachineCamera tppCmCam = null;

            if (mainCamGO != null)
            {
                var shootCamTr = FindSceneCamera("ShootCamera", "Shoot Camera");
                if (shootCamTr != null)
                {
                    var shootCamGO = shootCamTr.gameObject;
                    shootCamGO.SetActive(false);

                    var cmChild = shootCamGO.transform.Find("cm");
                    if (cmChild != null) Object.DestroyImmediate(cmChild.gameObject);

                    var cmCam = shootCamGO.GetComponent<CinemachineCamera>();
                    if (cmCam == null) cmCam = shootCamGO.AddComponent<CinemachineCamera>();
                    cmCam.Priority = 20;
                    cmCam.StandbyUpdate = CinemachineCamera.StandbyUpdateMode.RoundRobin;
                    cmCam.Follow = shootLockGO != null ? shootLockGO.transform : null;
                    cmCam.LookAt = null;

                    var tppFollow = shootCamGO.GetComponent<CinemachineThirdPersonFollow>();
                    if (tppFollow == null) tppFollow = shootCamGO.AddComponent<CinemachineThirdPersonFollow>();
                    tppFollow.Damping = new Vector3(0.1f, 0.5f, 0.3f);
                    tppFollow.ShoulderOffset = new Vector3(0.25f, -0.13f, -0.6f);
                    tppFollow.VerticalArmLength = 0.3f;
                    tppFollow.CameraSide = 1f;
                    tppFollow.CameraDistance = 2.5f;
                    tppFollow.AvoidObstacles.Enabled = false;
                    tppFollow.AvoidObstacles.CameraRadius = 0.2f;

                    Debug.Log("Shoot Camera configured in scene.");
                }

                var tppCamTr = FindSceneCamera("TppVirtualCamera", "Tpp Virtual Camera");
                if (tppCamTr != null)
                {
                    var tppCamGO = tppCamTr.gameObject;

                    for (int i = tppCamGO.transform.childCount - 1; i >= 0; i--)
                    {
                        Object.DestroyImmediate(tppCamGO.transform.GetChild(i).gameObject);
                    }

                    tppCmCam = tppCamGO.GetComponent<CinemachineCamera>();
                    if (tppCmCam == null) tppCmCam = tppCamGO.AddComponent<CinemachineCamera>();
                    tppCmCam.Priority = 10;
                    tppCmCam.StandbyUpdate = CinemachineCamera.StandbyUpdateMode.RoundRobin;
                    tppCmCam.Follow = playerGO != null ? playerGO.transform : null;
                    tppCmCam.LookAt = tppCameraLookGO != null ? tppCameraLookGO.transform : null;
                    tppCmCam.Target.CustomLookAtTarget = true;

                    var existingInputControllers = tppCamGO.GetComponents<CinemachineInputAxisController>();
                    for (int i = 0; i < existingInputControllers.Length; i++)
                    {
                        Object.DestroyImmediate(existingInputControllers[i]);
                    }

                    var orbital = tppCamGO.GetComponent<CinemachineOrbitalFollow>();
                    if (orbital == null) orbital = tppCamGO.AddComponent<CinemachineOrbitalFollow>();
                    orbital.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.ThreeRing;
                    orbital.Orbits = new Cinemachine3OrbitRig.Settings
                    {
                        Top = new Cinemachine3OrbitRig.Orbit { Height = 4.5f, Radius = 4.0f },
                        Center = new Cinemachine3OrbitRig.Orbit { Height = 2.2f, Radius = 5.0f },
                        Bottom = new Cinemachine3OrbitRig.Orbit { Height = 0.6f, Radius = 3.5f },
                        SplineCurvature = 0.5f
                    };
                    orbital.TrackerSettings.PositionDamping = new Vector3(1f, 1f, 1f);
                    orbital.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.LockToTargetOnAssign;
                    orbital.RecenteringTarget = CinemachineOrbitalFollow.ReferenceFrames.TrackingTarget;
                    orbital.HorizontalAxis.Range = new Vector2(-180f, 180f);
                    orbital.HorizontalAxis.Wrap = true;
                    orbital.HorizontalAxis.Recentering.Enabled = false;
                    orbital.VerticalAxis.Range = new Vector2(0f, 1f);
                    orbital.VerticalAxis.Center = 0.5f;
                    orbital.VerticalAxis.Wrap = false;
                    orbital.VerticalAxis.Value = 0.5f;
                    orbital.VerticalAxis.Recentering.Enabled = false;

                    var composer = tppCamGO.GetComponent<CinemachineRotationComposer>();
                    if (composer == null) composer = tppCamGO.AddComponent<CinemachineRotationComposer>();
                    composer.Composition.ScreenPosition = new Vector2(0f, 0.12f);
                    composer.Composition.DeadZone.Size = Vector2.zero;
                    composer.Composition.HardLimits.Size = new Vector2(0.8f, 0.8f);
                    composer.Damping = Vector2.zero;

                    var inputAxis = tppCamGO.AddComponent<CinemachineInputAxisController>();
                    inputAxis.AutoEnableInputs = true;
                    inputAxis.SynchronizeControllers();
                    var controllers = inputAxis.Controllers;
                    for (int i = 0; i < controllers.Count; i++)
                    {
                        var ctrl = controllers[i];
                        if (ctrl.Name == "Look Orbit X")
                        {
                            ctrl.Enabled = true;
                            ctrl.Input.InputAction = lookActionRef;
                            ctrl.Input.Gain = 0.05f;
                            ctrl.Input.CancelDeltaTime = true;
                            ctrl.Driver.AccelTime = 0f;
                            ctrl.Driver.DecelTime = 0f;
                        }
                        else if (ctrl.Name == "Look Orbit Y")
                        {
                            ctrl.Enabled = true;
                            ctrl.Input.InputAction = lookActionRef;
                            ctrl.Input.Gain = -0.0006f;
                            ctrl.Input.CancelDeltaTime = true;
                            ctrl.Driver.AccelTime = 0f;
                            ctrl.Driver.DecelTime = 0f;
                        }
                        else if (ctrl.Name == "Orbit Scale")
                        {
                            ctrl.Enabled = false;
                        }
                        controllers[i] = ctrl;
                    }

                    var deoccluder = tppCamGO.GetComponent<CinemachineDeoccluder>();
                    if (deoccluder == null) deoccluder = tppCamGO.AddComponent<CinemachineDeoccluder>();
                    deoccluder.CollideAgainst = LayerMask.GetMask("Default", "Enviro", "Terrain", "Laver");
                    deoccluder.IgnoreTag = "Player";
                    deoccluder.MinimumDistanceFromTarget = 0.5f;
                    deoccluder.AvoidObstacles.Enabled = true;
                    deoccluder.AvoidObstacles.CameraRadius = 0.2f;
                    deoccluder.AvoidObstacles.Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PreserveCameraHeight;
                    deoccluder.AvoidObstacles.Damping = 0.25f;
                    deoccluder.AvoidObstacles.DampingWhenOccluded = 0.25f;

                    Debug.Log("Tpp Virtual Camera configured in scene.");
                }
            }

            if (sceneContextGO != null && tppCmCam != null)
            {
                var installer = sceneContextGO.GetComponent<Raven.Core.Installer.PlayerInstaller>();
                if (installer != null)
                {
                    var so = new SerializedObject(installer);
                    var prop = so.FindProperty("_tppCamera");
                    if (prop != null)
                    {
                        prop.objectReferenceValue = tppCmCam;
                        so.ApplyModifiedProperties();
                        Debug.Log("PlayerInstaller _tppCamera re-wired in scene.");
                    }
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("Level1 scene saved.");

            ValidateSetup();

            string prefabPath = "Assets/Prefabs/Core/Main Camera.prefab";
            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot != null)
            {
                try
                {
                    var shootCamTr = prefabRoot.transform.Find("Shoot Camera");
                    if (shootCamTr != null)
                    {
                        var shootCamGO = shootCamTr.gameObject;
                        shootCamGO.SetActive(false);

                        var cmChild = shootCamGO.transform.Find("cm");
                        if (cmChild != null) Object.DestroyImmediate(cmChild.gameObject);

                        var cmCam = shootCamGO.GetComponent<CinemachineCamera>();
                        if (cmCam == null) cmCam = shootCamGO.AddComponent<CinemachineCamera>();
                        cmCam.Priority = 20;
                        cmCam.StandbyUpdate = CinemachineCamera.StandbyUpdateMode.RoundRobin;

                        var tppFollow = shootCamGO.GetComponent<CinemachineThirdPersonFollow>();
                        if (tppFollow == null) tppFollow = shootCamGO.AddComponent<CinemachineThirdPersonFollow>();
                        tppFollow.Damping = new Vector3(0.1f, 0.5f, 0.3f);
                        tppFollow.ShoulderOffset = new Vector3(1.0f, -0.1f, -0.6f);
                        tppFollow.VerticalArmLength = 0.4f;
                        tppFollow.CameraSide = 1f;
                        tppFollow.CameraDistance = 2.5f;
                        tppFollow.AvoidObstacles.Enabled = false;
                        tppFollow.AvoidObstacles.CameraRadius = 0.2f;

                        Debug.Log("Shoot Camera configured in prefab.");
                    }

                    var tppCamTr = prefabRoot.transform.Find("Tpp Virtual Camera");
                    if (tppCamTr != null)
                    {
                        var tppCamGO = tppCamTr.gameObject;

                        for (int i = tppCamGO.transform.childCount - 1; i >= 0; i--)
                        {
                            Object.DestroyImmediate(tppCamGO.transform.GetChild(i).gameObject);
                        }

                        var cmCam = tppCamGO.GetComponent<CinemachineCamera>();
                        if (cmCam == null) cmCam = tppCamGO.AddComponent<CinemachineCamera>();
                        cmCam.Priority = 10;
                        cmCam.StandbyUpdate = CinemachineCamera.StandbyUpdateMode.RoundRobin;

                        var existingInputControllers = tppCamGO.GetComponents<CinemachineInputAxisController>();
                        for (int i = 0; i < existingInputControllers.Length; i++)
                        {
                            Object.DestroyImmediate(existingInputControllers[i]);
                        }

                        var orbital = tppCamGO.GetComponent<CinemachineOrbitalFollow>();
                        if (orbital == null) orbital = tppCamGO.AddComponent<CinemachineOrbitalFollow>();
                        orbital.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.ThreeRing;
                        orbital.Orbits = new Cinemachine3OrbitRig.Settings
                        {
                            Top = new Cinemachine3OrbitRig.Orbit { Height = 4.5f, Radius = 4.0f },
                            Center = new Cinemachine3OrbitRig.Orbit { Height = 2.2f, Radius = 5.0f },
                            Bottom = new Cinemachine3OrbitRig.Orbit { Height = 0.6f, Radius = 3.5f },
                            SplineCurvature = 0.5f
                        };
                        orbital.TrackerSettings.PositionDamping = new Vector3(1f, 1f, 1f);
                        orbital.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.LockToTargetOnAssign;
                        orbital.RecenteringTarget = CinemachineOrbitalFollow.ReferenceFrames.TrackingTarget;
                        orbital.HorizontalAxis.Range = new Vector2(-180f, 180f);
                        orbital.HorizontalAxis.Wrap = true;
                        orbital.HorizontalAxis.Recentering.Enabled = false;
                        orbital.VerticalAxis.Range = new Vector2(0f, 1f);
                        orbital.VerticalAxis.Center = 0.5f;
                        orbital.VerticalAxis.Wrap = false;
                        orbital.VerticalAxis.Value = 0.5f;
                        orbital.VerticalAxis.Recentering.Enabled = false;

                        var composer = tppCamGO.GetComponent<CinemachineRotationComposer>();
                        if (composer == null) composer = tppCamGO.AddComponent<CinemachineRotationComposer>();
                        composer.Composition.ScreenPosition = new Vector2(0f, 0.12f);
                        composer.Composition.DeadZone.Size = Vector2.zero;
                        composer.Composition.HardLimits.Size = new Vector2(0.8f, 0.8f);
                        composer.Damping = Vector2.zero;

                        var inputAxis = tppCamGO.AddComponent<CinemachineInputAxisController>();
                        inputAxis.AutoEnableInputs = true;
                        inputAxis.SynchronizeControllers();
                        var controllers = inputAxis.Controllers;
                        for (int i = 0; i < controllers.Count; i++)
                        {
                            var ctrl = controllers[i];
                            if (ctrl.Name == "Look Orbit X")
                            {
                                ctrl.Enabled = true;
                                ctrl.Input.InputAction = lookActionRef;
                                ctrl.Input.Gain = 0.05f;
                                ctrl.Input.CancelDeltaTime = true;
                                ctrl.Driver.AccelTime = 0f;
                                ctrl.Driver.DecelTime = 0f;
                            }
                            else if (ctrl.Name == "Look Orbit Y")
                            {
                                ctrl.Enabled = true;
                                ctrl.Input.InputAction = lookActionRef;
                                ctrl.Input.Gain = -0.0006f;
                                ctrl.Input.CancelDeltaTime = true;
                                ctrl.Driver.AccelTime = 0f;
                                ctrl.Driver.DecelTime = 0f;
                            }
                            else if (ctrl.Name == "Orbit Scale")
                            {
                                ctrl.Enabled = false;
                            }
                            controllers[i] = ctrl;
                        }

                        var deoccluder = tppCamGO.GetComponent<CinemachineDeoccluder>();
                        if (deoccluder == null) deoccluder = tppCamGO.AddComponent<CinemachineDeoccluder>();
                        deoccluder.CollideAgainst = LayerMask.GetMask("Default", "Enviro", "Terrain", "Laver");
                        deoccluder.IgnoreTag = "Player";
                        deoccluder.MinimumDistanceFromTarget = 0.5f;
                        deoccluder.AvoidObstacles.Enabled = true;
                        deoccluder.AvoidObstacles.CameraRadius = 0.2f;
                        deoccluder.AvoidObstacles.Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PreserveCameraHeight;
                        deoccluder.AvoidObstacles.Damping = 0.25f;
                        deoccluder.AvoidObstacles.DampingWhenOccluded = 0.25f;

                        Debug.Log("Tpp Virtual Camera configured in prefab.");
                    }

                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                    Debug.Log("Main Camera prefab saved successfully.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        [MenuItem("Tools/Validate Cinemachine Setup")]
        public static void ValidateSetup()
        {
            var mainCam = (GameObject.Find("MainCamera") ?? GameObject.Find("Main Camera"));
            if (mainCam != null)
            {
                var brain = mainCam.GetComponent<CinemachineBrain>();
                Debug.Log($"[Validation] Main Camera Brain: {(brain != null ? brain.GetType().FullName : "null")}");

                var tpp = FindSceneCamera("TppVirtualCamera", "Tpp Virtual Camera");
                if (tpp != null)
                {
                    Debug.Log($"[Validation] Tpp Virtual Camera child count: {tpp.childCount}");
                    foreach (var c in tpp.GetComponents<Component>())
                    {
                        Debug.Log($"[Validation]   Tpp comp: {(c != null ? c.GetType().FullName : "null")}");
                    }
                    var cmCam = tpp.GetComponent<CinemachineCamera>();
                    if (cmCam != null)
                    {
                        Debug.Log($"[Validation]   cmCam.Follow: {(cmCam.Follow != null ? cmCam.Follow.name : "null")}, LookAt: {(cmCam.LookAt != null ? cmCam.LookAt.name : "null")}, CustomLookAt: {cmCam.Target.CustomLookAtTarget}");
                    }
                    var orbital = tpp.GetComponent<CinemachineOrbitalFollow>();
                    if (orbital != null)
                    {
                        Debug.Log($"[Validation]   orbital.OrbitStyle: {orbital.OrbitStyle}, BindingMode: {orbital.TrackerSettings.BindingMode}, Radius Top: {orbital.Orbits.Top.Radius}, Mid: {orbital.Orbits.Center.Radius}, Bot: {orbital.Orbits.Bottom.Radius}");
                        Debug.Log($"[Validation]   HorizontalAxis: Value={orbital.HorizontalAxis.Value}, Range={orbital.HorizontalAxis.Range}, Wrap={orbital.HorizontalAxis.Wrap}, Recentering={orbital.HorizontalAxis.Recentering.Enabled}");
                        Debug.Log($"[Validation]   VerticalAxis: Value={orbital.VerticalAxis.Value}, Range={orbital.VerticalAxis.Range}, Wrap={orbital.VerticalAxis.Wrap}, Recentering={orbital.VerticalAxis.Recentering.Enabled}");
                    }
                    var inputAxis = tpp.GetComponent<CinemachineInputAxisController>();
                    if (inputAxis != null)
                    {
                        Debug.Log($"[Validation]   inputAxis controllers count: {inputAxis.Controllers.Count}");
                        for (int i = 0; i < inputAxis.Controllers.Count; i++)
                        {
                            var ctrl = inputAxis.Controllers[i];
                            Debug.Log($"[Validation]     ctrl[{i}]: {ctrl.Name}, Enabled: {ctrl.Enabled}, Action: {(ctrl.Input.InputAction != null ? ctrl.Input.InputAction.name : "null")}, Gain: {ctrl.Input.Gain}, CancelDeltaTime: {ctrl.Input.CancelDeltaTime}, Accel: {ctrl.Driver.AccelTime}, Decel: {ctrl.Driver.DecelTime}");
                        }
                    }
                }

                var shoot = FindSceneCamera("ShootCamera", "Shoot Camera");
                if (shoot != null)
                {
                    Debug.Log($"[Validation] Shoot Camera activeSelf: {shoot.gameObject.activeSelf}");
                    foreach (var c in shoot.GetComponents<Component>())
                    {
                        Debug.Log($"[Validation]   Shoot comp: {(c != null ? c.GetType().FullName : "null")}");
                    }
                    var cmCam = shoot.GetComponent<CinemachineCamera>();
                    if (cmCam != null)
                    {
                        Debug.Log($"[Validation]   shoot cmCam.Follow: {(cmCam.Follow != null ? cmCam.Follow.name : "null")}, Priority: {cmCam.Priority}");
                    }
                }
            }

            var sceneContext = GameObject.Find("SceneContext");
            if (sceneContext != null)
            {
                var installer = sceneContext.GetComponent<Raven.Core.Installer.PlayerInstaller>();
                if (installer != null)
                {
                    var so = new SerializedObject(installer);
                    var prop = so.FindProperty("_tppCamera");
                    Debug.Log($"[Validation] PlayerInstaller._tppCamera: {(prop.objectReferenceValue != null ? prop.objectReferenceValue.name : "null")}");
                }
            }
        }
    }
}
#endif
