using UnityEngine.Rendering.Universal;
using UnityEngine;
using System;
using MF.SSGI.Settings;


namespace MF.SSGI {
    public class SSGIFeature : ScriptableRendererFeature {
        [Serializable]
        public class SSGISettings {
            [Tooltip(
                "EDITOR ONLY: By default SSGI uses the camera.actualRenderingPath. According to its documentation this returns 'Forward' when the platform doesn't support Deferred.\n" +
                "That's great and all, but in-editor you can still switch to Deferred and it will look way better. By setting this to True, you can override the 'camera.actualRenderingPath' and go with Deferred regardless")]
            public bool UseDeferredRendering = false;
            public RenderPassEvent RenderPassEvent = RenderPassEvent.BeforeRenderingTransparents;

            [Space]
            [Header("Core settings")]
            [Tooltip("How far can we see SSGI? Make sure the 'Min' value is always lower then the 'Max'")]
            public float SSGIRangeMin = 10;
            [Tooltip("How far can we see SSGI? Make sure the 'Min' value is always lower then the 'Max'")]
            public float SSGIRangeMax = 25;
            [Tooltip("In world-units: How far will shadows be calculated? Since Raymarching is expensive, keep this as low as possible")]
            public float ShadowMinDistace = 2.5f;
            [Tooltip("In world-units: How far will shadows be calculated? Since Raymarching is expensive, keep this as low as possible")]
            public float ShadowMaxDistace = 10f;
            [Tooltip("'ScanDepth2DRangeFactor * SSGIRangeMax' will be the focus point to which the 2D-scan range is fixed, beyond the area around each pixel sampled will decrease in size. This helps stabalize larger scenes")]
            [Range(0.01f, 0.5f)] public float ScanDepth2DRangeFactor = 0.15f;
            [Tooltip("Fixed percentage of the screen used to sample the pixels. Note: 'Search 2D Noise' (Advanced settings) will be added increasing the range as well")]
            [Range(0.01f, 1f)] public float Orthographic2DRangeFactor = 0.15f;

            [Space]
            [Header("Basic settings")]
            [Tooltip("Basic settings, enough options to create i.e. Low/Medium/High variants")]
            public SSGIQualitySettings Quality;
            [Tooltip("Lighting artists will spend most time here I'm sure...")]
            public SSGILightingSettings Lighting;

            [Space]
            [Header("Advanced settings")]
            [Tooltip("Advanced settings, don't touch unless you know what you are doing :P")]
            public SSGIAdvancedSettings Advanced;
            [Tooltip("Advanced settings, don't touch unless you know what you are doing :P")]
            public SSGIRaymarchSettings Raymarch;
            [Tooltip("Advanced settings, don't touch unless you know what you are doing :P")]
            public SSGIFallbackSettings Fallback;

            [Space]
            [Header("RUNTIME DEBUGGING")]
            [Range(0f, 1f)] public float DebugScreenCoverage = 1f;
            public bool DebugMotionVectors = false;
            public bool DebugReprojection = false;
            public bool DebugAlbedo = false;
        }


        public static bool ShowInSceneView = true;
        public static bool ShowToggleIconInSceneView = true;
        public static bool SSGIActive = true;


        public SSGISettings Settings { get => settings; }

        [SerializeField] private bool showInSceneView = true;
        [SerializeField] private bool showToggleIconInSceneView = true;
        [SerializeField] private SSGISettings settings;
        
        private SSGIPass pass;
        private RavenDistortionResetPass distortionReset = new RavenDistortionResetPass();
        private RavenDistortionBackgroundPass distortionBackground;
        private RavenDistortionResetPass waterCompositionReset = new RavenDistortionResetPass();

        public override void Create() {
            if(settings == null) settings = new SSGISettings();

#if UNITY_EDITOR
            ApplyStaticSettings();
            LinkScriptableObjects();
#endif
            pass?.Dispose(); pass = new SSGIPass(settings);
        }

        protected override void Dispose(bool disposing) { pass?.Dispose(); pass=null; }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            waterCompositionReset.renderPassEvent = (RenderPassEvent)((int)settings.RenderPassEvent - 1);
            renderer.EnqueuePass(waterCompositionReset);
            pass.Setup(renderer);
            renderer.EnqueuePass(pass);
            if (renderingData.cameraData.camera.GetComponent<SSGICamera>() != null || renderingData.cameraData.isSceneViewCamera) {
                if (distortionBackground == null) distortionBackground = new RavenDistortionBackgroundPass();
                distortionBackground.Setup(renderer, settings.RenderPassEvent);
                renderer.EnqueuePass(distortionBackground);
                renderer.EnqueuePass(distortionReset);
            }
        }



#if UNITY_EDITOR
        private void OnValidate() {
            ApplyStaticSettings();
            LinkScriptableObjects();
            UnityEditor.SceneView.RepaintAll();
        }

        private void ApplyStaticSettings() {
            ShowInSceneView = showInSceneView;
            ShowToggleIconInSceneView = showToggleIconInSceneView;
        }

        private void LinkScriptableObjects() {
            if(settings == null) { return; }
            
            if (!settings.Quality) {
                settings.Quality = (SSGIQualitySettings)UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Resources/MFSSGI/Settings/QualityReprojected/SSGIReprojected2MediumQualitySettings.asset", typeof(SSGIQualitySettings));
            }
            if (!settings.Lighting) {
                settings.Lighting = (SSGILightingSettings)UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Resources/MFSSGI/Settings/Shared/SSGILightingSettings.asset", typeof(SSGILightingSettings));
            }
            if (!settings.Advanced) {
                settings.Advanced = (SSGIAdvancedSettings)UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Resources/MFSSGI/Settings/Shared/SSGIAdvancedSettings.asset", typeof(SSGIAdvancedSettings));
            }
            if (!settings.Raymarch) {
                settings.Raymarch = (SSGIRaymarchSettings)UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Resources/MFSSGI/Settings/Shared/SSGIRaymarchSettings.asset", typeof(SSGIRaymarchSettings));
            }
            if (!settings.Fallback) {
                settings.Fallback = (SSGIFallbackSettings)UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Resources/MFSSGI/Settings/Shared/SSGIFallbackSettings.asset", typeof(SSGIFallbackSettings));
            }
        }
#endif
    }
}
