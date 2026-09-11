using System.Collections.Generic;
using Raven.Player.Core;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

namespace Raven.Player.Editor
{
    /// <summary>
    /// Editor menu for creating player system assets and validating setup.
    /// </summary>
    public class PlayerControllerEditorMenu
    {
        [MenuItem("Raven/Player System/Create Player Controller Config")]
        public static void CreatePlayerControllerConfig()
        {
            var config = ScriptableObject.CreateInstance<PlayerControllerConfig>();
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Player Controller Config",
                "PlayerControllerConfig",
                "asset",
                "Choose a location");

            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.CreateAsset(config, path);
                AssetDatabase.SaveAssets();
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = config;
                Debug.Log($"Created PlayerControllerConfig at {path}");
            }
        }

        [MenuItem("Raven/Player System/Setup Player System/Fresh (No Legacy)")]
        public static void SetupPlayerSystemFresh()
        {
            var installer = new GameObject("PlayerSystemInstaller")
                .AddComponent<PlayerControllerInstaller>();
            var bootstrapper = FindObjectOfType<PlayerControllerBootstrapper>();
            
            if (bootstrapper == null)
            {
                Debug.LogWarning("No PlayerControllerBootstrapper found in scene. Please add it to your player GameObject.");
            }

            Debug.Log("Player system installer created. Configure PlayerControllerConfig and assign it.");
        }

        [MenuItem("Raven/Player System/Setup Player System/With Legacy Support")]
        public static void SetupPlayerSystemLegacy()
        {
            var installer = new GameObject("PlayerSystemInstaller")
                .AddComponent<PlayerControllerLegacyAdapterInstaller>();
            var bootstrapper = FindObjectOfType<PlayerControllerBootstrapper>();
            
            if (bootstrapper == null)
            {
                Debug.LogWarning("No PlayerControllerBootstrapper found in scene. Please add it to your player GameObject.");
            }

            Debug.Log("Player system installer with legacy support created. Configure PlayerControllerConfig and assign it.");
        }

        [MenuItem("Raven/Player System/Validate Setup")]
        public static void ValidateSetup()
        {
            var errors = new List<string>();

            // Check for installer
            var installer = FindObjectOfType<PlayerControllerInstaller>();
            var legacyInstaller = FindObjectOfType<PlayerControllerLegacyAdapterInstaller>();
            
            if (installer == null && legacyInstaller == null)
            {
                errors.Add("❌ No PlayerControllerInstaller found in scene");
            }
            else if (installer != null)
            {
                if (installer.GetComponent<MonoInstaller>() != null)
                    Debug.Log("✓ PlayerControllerInstaller found");
            }
            else if (legacyInstaller != null)
            {
                if (legacyInstaller.GetComponent<MonoInstaller>() != null)
                    Debug.Log("✓ PlayerControllerLegacyAdapterInstaller found");
            }

            // Check for bootstrapper
            var bootstrapper = FindObjectOfType<PlayerControllerBootstrapper>();
            if (bootstrapper == null)
            {
                errors.Add("❌ No PlayerControllerBootstrapper found in scene");
            }
            else
            {
                Debug.Log("✓ PlayerControllerBootstrapper found");
            }

            // Check for required components on player
            if (bootstrapper != null)
            {
                var player = bootstrapper.gameObject;
                if (player.GetComponent<CharacterController>() == null)
                    errors.Add("❌ Player GameObject missing CharacterController");
                else
                    Debug.Log("✓ CharacterController found on player");

                if (player.GetComponent<Animator>() == null)
                    errors.Add("❌ Player GameObject missing Animator");
                else
                    Debug.Log("✓ Animator found on player");
            }

            // Report results
            if (errors.Count == 0)
            {
                Debug.Log("\n✅ Player system setup is valid!");
            }
            else
            {
                Debug.LogError($"\n⚠️ Player system setup has {errors.Count} error(s):");
                foreach (var error in errors)
                {
                    Debug.LogError(error);
                }
            }
        }
    }
}

#endif
