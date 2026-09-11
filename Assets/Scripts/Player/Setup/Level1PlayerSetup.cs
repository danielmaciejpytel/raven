using Raven.Player.Core;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Raven.Player.Setup
{
    /// <summary>
    /// Automatic setup for Level1 scene with the new PlayerController.
    /// Run this once to configure everything correctly.
    /// </summary>
    public class Level1PlayerSetup
    {
#if UNITY_EDITOR
        [MenuItem("Raven/Level1 Setup/Auto Configure Player System")]
        public static void AutoConfigureLevel1()
        {
            // Find or create installer
            var installerGO = GameObject.Find("PlayerSystemInstaller");
            if (installerGO == null)
            {
                installerGO = new GameObject("PlayerSystemInstaller");
                Debug.Log("Created PlayerSystemInstaller GameObject");
            }

            var installer = installerGO.GetComponent<PlayerControllerInstaller>();
            if (installer == null)
            {
                installer = installerGO.AddComponent<PlayerControllerInstaller>();
                Debug.Log("Added PlayerControllerInstaller component");
            }

            // Find or create config
            var configPath = "Assets/Configs/Player/Level1PlayerControllerConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<PlayerControllerConfig>(configPath);

            if (config == null)
            {
                config = ScriptableObject.CreateInstance<PlayerControllerConfig>();
                AssetDatabase.CreateAsset(config, configPath);
                Debug.Log($"Created PlayerControllerConfig at {configPath}");
            }

            // Auto-detect and assign references
            AutoAssignReferences(config);

            // Assign config to installer
            var installerType = installer.GetType();
            var field = installerType.GetField("_playerControllerConfig", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (field != null)
            {
                field.SetValue(installer, config);
                Debug.Log("Assigned PlayerControllerConfig to installer");
            }

            // Add bootstrapper to player if not present
            var playerGO = config.PlayerGameObject;
            if (playerGO != null)
            {
                var bootstrapper = playerGO.GetComponent<PlayerControllerBootstrapper>();
                if (bootstrapper == null)
                {
                    playerGO.AddComponent<PlayerControllerBootstrapper>();
                    Debug.Log("Added PlayerControllerBootstrapper to player");
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("\n✅ Level1 PlayerController setup complete! Hit Play to test.");
        }

        private static void AutoAssignReferences(PlayerControllerConfig config)
        {
            // Find player
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                player = GameObject.Find("Player");
            }

            if (player != null)
            {
                var playerField = config.GetType().GetField("_playerGameObject", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (playerField != null)
                {
                    playerField.SetValue(config, player);
                    Debug.Log($"✓ Assigned Player: {player.name}");
                }

                // Get animator
                var animator = player.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = player.GetComponentInChildren<Animator>();
                }
                if (animator != null)
                {
                    var animatorField = config.GetType().GetField("_playerAnimator", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (animatorField != null)
                    {
                        animatorField.SetValue(config, animator);
                        Debug.Log($"✓ Assigned Animator");
                    }
                }

                // Get ground check
                var groundCheck = player.transform.Find("GroundCheck");
                if (groundCheck == null)
                {
                    // Create ground check if it doesn't exist
                    var gc = new GameObject("GroundCheck");
                    gc.transform.SetParent(player.transform);
                    gc.transform.localPosition = new Vector3(0, -1, 0);
                    groundCheck = gc.transform;
                    Debug.Log("Created GroundCheck transform");
                }
                if (groundCheck != null)
                {
                    var gcField = config.GetType().GetField("_groundCheckTransform", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (gcField != null)
                    {
                        gcField.SetValue(config, groundCheck);
                        Debug.Log($"✓ Assigned GroundCheck");
                    }
                }
            }
            else
            {
                Debug.LogWarning("Could not find Player GameObject. Please manually assign in PlayerControllerConfig.");
            }

            // Find camera
            var camera = Camera.main?.transform;
            if (camera != null)
            {
                var cameraField = config.GetType().GetField("_cameraTransform", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (cameraField != null)
                {
                    cameraField.SetValue(config, camera);
                    Debug.Log($"✓ Assigned Main Camera");
                }
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Raven/Level1 Setup/Validate Level1 Configuration")]
        public static void ValidateLevel1Setup()
        {
            Debug.Log("\n" + new string('=', 50));
            Debug.Log("LEVEL1 PLAYER SYSTEM VALIDATION");
            Debug.Log(new string('=', 50));

            var errors = new System.Collections.Generic.List<string>();
            var warnings = new System.Collections.Generic.List<string>();

            // Check installer
            var installer = GameObject.FindGameObjectWithTag("PlayerSystemInstaller")?.GetComponent<PlayerControllerInstaller>()
                ?? GameObject.Find("PlayerSystemInstaller")?.GetComponent<PlayerControllerInstaller>();

            if (installer == null)
            {
                errors.Add("❌ PlayerControllerInstaller not found in scene");
            }
            else
            {
                Debug.Log("✅ PlayerControllerInstaller found");
            }

            // Check bootstrapper
            var bootstrapper = FindObjectOfType<PlayerControllerBootstrapper>();
            if (bootstrapper == null)
            {
                errors.Add("❌ PlayerControllerBootstrapper not found in scene");
            }
            else
            {
                Debug.Log("✅ PlayerControllerBootstrapper found");
            }

            // Check player
            var player = GameObject.FindGameObjectWithTag("Player") ?? GameObject.Find("Player");
            if (player == null)
            {
                errors.Add("❌ Player GameObject not found");
            }
            else
            {
                Debug.Log($"✅ Player found: {player.name}");

                if (player.GetComponent<CharacterController>() == null)
                {
                    errors.Add($"❌ {player.name} missing CharacterController component");
                }
                else
                {
                    Debug.Log("  ✓ Has CharacterController");
                }

                if (player.GetComponent<Animator>() == null && player.GetComponentInChildren<Animator>() == null)
                {
                    errors.Add($"❌ {player.name} missing Animator component");
                }
                else
                {
                    Debug.Log("  ✓ Has Animator");
                }
            }

            // Check ground tags
            var groundObjects = FindObjectsOfType<GameObject>()
                .FindAll(go => go.CompareTag("Ground"));
            
            if (groundObjects.Count == 0)
            {
                warnings.Add("⚠️  No objects tagged with 'Ground' - ground detection may fail");
            }
            else
            {
                Debug.Log($"✅ Found {groundObjects.Count} ground objects");
            }

            // Check configs
            var configPath = "Assets/Configs/Player/Level1PlayerControllerConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<PlayerControllerConfig>(configPath);
            if (config == null)
            {
                warnings.Add($"⚠️  PlayerControllerConfig not found at {configPath}");
            }
            else
            {
                Debug.Log($"✅ PlayerControllerConfig found");
            }

            // Summary
            Debug.Log(new string('=', 50));
            if (errors.Count > 0)
            {
                Debug.LogError($"Found {errors.Count} ERROR(S):");
                foreach (var error in errors)
                {
                    Debug.LogError(error);
                }
            }

            if (warnings.Count > 0)
            {
                Debug.LogWarning($"Found {warnings.Count} WARNING(S):");
                foreach (var warning in warnings)
                {
                    Debug.LogWarning(warning);
                }
            }

            if (errors.Count == 0 && warnings.Count == 0)
            {
                Debug.Log("\n🎉 ALL CHECKS PASSED! Level1 is ready to use the new PlayerController!");
            }

            Debug.Log(new string('=', 50) + "\n");
        }

        [MenuItem("Raven/Level1 Setup/Show Setup Instructions")]
        public static void ShowSetupInstructions()
        {
            const string instructions = @"
╔════════════════════════════════════════════════════════════════════╗
║                 LEVEL1 PLAYER CONTROLLER SETUP                     ║
╠════════════════════════════════════════════════════════════════════╣
║                                                                    ║
║ OPTION 1: AUTOMATIC SETUP (Recommended)                          ║
║ ────────────────────────────────────────                         ║
║ 1. Open Level1 scene                                             ║
║ 2. Raven → Level1 Setup → Auto Configure Player System           ║
║ 3. Check the Console for results                                 ║
║ 4. Hit Play to test                                              ║
║                                                                   ║
║ OPTION 2: MANUAL SETUP (If automatic fails)                      ║
║ ──────────────────────────────────────────                       ║
║ 1. Create empty GameObject: "PlayerSystemInstaller"              ║
║ 2. Add PlayerControllerInstaller component                       ║
║ 3. Create PlayerControllerConfig asset:                          ║
║    Assets → Configs → Player → Controller Config                 ║
║ 4. In PlayerControllerConfig, assign:                            ║
║    - Player Game Object (your player)                            ║
║    - Player Animator (from player)                               ║
║    - Camera Transform (Main Camera)                              ║
║    - Ground Check Transform (create empty at player feet)        ║
║    - Movement Config (existing)                                  ║
║    - Player Data Config (existing)                               ║
║ 5. Assign PlayerControllerConfig to installer                    ║
║ 6. Add PlayerControllerBootstrapper to player GameObject          ║
║ 7. Hit Play to test                                              ║
║                                                                   ║
║ VERIFICATION:                                                     ║
║ ─────────────                                                     ║
║ • Raven → Level1 Setup → Validate Level1 Configuration           ║
║ • Check Console for any errors or warnings                       ║
║ • If all green checkmarks appear, you're ready!                  ║
║                                                                   ║
║ EXPECTED LOGS ON PLAY:                                           ║
║ ─────────────────────                                            ║
║ ✓ PlayerControllerInstaller: All player subsystems registered... ║
║ ✓ PlayerControllerBootstrapper: Player system initialized...     ║
║                                                                   ║
╚════════════════════════════════════════════════════════════════════╝
";
            Debug.Log(instructions);
        }
#endif
    }
}
