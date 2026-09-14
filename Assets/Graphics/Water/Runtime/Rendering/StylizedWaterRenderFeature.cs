#if URP
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StylizedWater2
{
    #if UNITY_2021_1_OR_NEWER
    [DisallowMultipleRendererFeature("Stylized Water 2")]
    #endif
    public class StylizedWaterRenderFeature : ScriptableRendererFeature
    {
        public static StylizedWaterRenderFeature GetDefault()
        {
            return (StylizedWaterRenderFeature)PipelineUtilities.GetRenderFeature<StylizedWaterRenderFeature>();
        }
        
        [Serializable]
        public class ScreenSpaceReflectionSettings
        {
            public bool enable;
        }
        public ScreenSpaceReflectionSettings screenSpaceReflectionSettings = new ScreenSpaceReflectionSettings();
        
        [Tooltip("Project caustics from the main directional light.")]
        public bool directionalCaustics;
        
        public DisplacementPrePass.Settings displacementPrePassSettings = new DisplacementPrePass.Settings();
        
        private SetupConstants constantsSetup;
        #if UNITY_6000_0_OR_NEWER
        private SetupConstants.ResetGraphConstants reset = new SetupConstants.ResetGraphConstants();
        #endif
        private DisplacementPrePass displacementPass;

        public override void Create()
        {
            constantsSetup?.Dispose();
            displacementPass?.Dispose();

            constantsSetup = new SetupConstants
            {
                renderPassEvent = RenderPassEvent.BeforeRendering
            };

            displacementPass = new DisplacementPrePass
            {
                renderPassEvent = RenderPassEvent.BeforeRendering
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            constantsSetup.Setup(this);
            renderer.EnqueuePass(constantsSetup);
            #if UNITY_6000_0_OR_NEWER
            var renderGraphSettings = GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();
            if (renderGraphSettings != null && !renderGraphSettings.enableRenderCompatibilityMode)
            {
                renderer.EnqueuePass(reset);
            }
            #endif
            
            if (displacementPrePassSettings.enable)
            {
                displacementPass.Setup(displacementPrePassSettings);
                renderer.EnqueuePass(displacementPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            displacementPass?.Dispose();
            displacementPass = null;
            constantsSetup?.Dispose();
            constantsSetup = null;
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
#endif
