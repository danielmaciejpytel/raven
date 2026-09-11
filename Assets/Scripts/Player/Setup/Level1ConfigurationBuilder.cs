using Raven.Config;
using Raven.Player.Core;
using UnityEngine;

namespace Raven.Player.Setup
{
    /// <summary>
    /// Helper to quickly create all necessary configuration assets for Level1.
    /// </summary>
#if UNITY_EDITOR
    using UnityEditor;

    public class Level1ConfigurationBuilder
    {
        [MenuItem("Raven/Level1 Setup/Create Missing Configs")]
        public static void CreateMissingConfigs()
        {
            // Create directories if they don't exist
            var assetPath = "Assets/Configs";
            if (!AssetDatabase.IsValidFolder(assetPath))
                AssetDatabase.CreateFolder("Assets", "Configs");

            assetPath = "Assets/Configs/Player";
            if (!AssetDatabase.IsValidFolder(assetPath))
                AssetDatabase.CreateFolder("Assets/Configs", "Player");

            // Create PlayerControllerConfig
            var configPath = "Assets/Configs/Player/Level1PlayerControllerConfig.asset";
            if (AssetDatabase.LoadAssetAtPath<PlayerControllerConfig>(configPath) == null)
            {
                var config = ScriptableObject.CreateInstance<PlayerControllerConfig>();
                AssetDatabase.CreateAsset(config, configPath);
                Debug.Log($"✅ Created PlayerControllerConfig at {configPath}");
            }
            else
            {
                Debug.Log($"ℹ️ PlayerControllerConfig already exists at {configPath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
#endif
}
