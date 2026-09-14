using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System.Linq;

namespace MF.SSGI {
    public class SSGIPass : ScriptableRenderPass {



        //---------------------- STATIC
        public enum SSGIPassType {
            ThicknessMaskPrePass,
            ThicknessMask,
            SSGIObjects,
            ScreenCapture,
            LightCapture,
            WorldPositions,
            WorldPosDepth,
            Normals,
            NormalsDepth,
            SSGIColor,
            SSGIShadow,
            SSGILightDir,
            PreDenoised1Color,
            PreDenoised1Shadow,
            PreDenoised1LightDir,
            PreDenoised2Color,
            PreDenoised2Shadow,
            PreDenoised2LightDir,
            FinalDenoisedColor,
            FinalDenoisedShadow,
            FinalDenoisedLightDir,
            TAAHistory,
        }

        public class DebugRTData {
            public Camera Camera;
            public SSGIPassType Type;
            public bool WasHandled = false;
        }

        public class RTWrapper {
            public Camera Cam;
            public SSGIPassType Type;

            public RenderTexture RT1;
            public RenderTexture RT2;

            public void Dispose() {
                if (RT1) {
                    SSGIGraphCommands.Forget(RT1); GameObject.DestroyImmediate(RT1);
                }
                if (RT2) {
                    SSGIGraphCommands.Forget(RT2); GameObject.DestroyImmediate(RT2);
                }

                RT1 = null;
                RT2 = null;
                Cam = null;
            }
        }

        public class ReflectionProbeWrapper {
            public ReflectionProbe Probe;
            public SSGIReflectionProbeOverride Override;
            public float Fade;

            public ReflectionProbeWrapper(ReflectionProbe probe) {
                Probe = probe;
                Override = probe.GetComponent<SSGIReflectionProbeOverride>();
            }
        }

        public static List<SSGIObject> RequestedObjects = new List<SSGIObject>();
        private static Dictionary<RenderTexture, DebugRTData> debugRenderTextures = new Dictionary<RenderTexture, DebugRTData>();
        private static Dictionary<Camera, List<ReflectionProbeWrapper>> reflectionProbeWrappers = new Dictionary<Camera, List<ReflectionProbeWrapper>>();
        private static List<RTWrapper> rtWrappers = new List<RTWrapper>();
        private static Dictionary<Camera, int> frameCountTable = new Dictionary<Camera, int>();


        public static void SetDebugRT(RenderTexture rt, Camera cam, SSGIPassType type) {
            if (debugRenderTextures.ContainsKey(rt)) {
                debugRenderTextures[rt].Camera = cam;
                debugRenderTextures[rt].Type = type;
                debugRenderTextures[rt].WasHandled = false;
            } else {
                debugRenderTextures.Add(rt, new DebugRTData() {
                    Camera = cam,
                    Type = type
                });
            }
        }

        public static void RemoveDebugRT(RenderTexture rt) {
            if (rt == null || debugRenderTextures == null) { return; }
            if (debugRenderTextures.ContainsKey(rt)) {
                debugRenderTextures.Remove(rt);
            }
        }



        //---------------------- INSTANCE
        //Fixed settings
        public const string UNITY_RTNAME_DEPTH = "_CameraDepthTexture";
        public const string UNITY_RTNAME_NORMAL = "_CameraNormalsTexture";

        //Members
        private Material thicknessMaskMaterialBack;
        private Material thicknessMaskMaterialFront;
        private Material ssgiExpandVerticesMaterial;
        private Material ssgiObjectMaterial;
        private Material captureLightMaterial;
        private Material worldPosMaterial;
        private Material captureNormalsMaterial;
        private Material scanEnvironmentMaterial;
        private Material denoiseImageMaterial;
        private Material taaShadowMaterial;
        private Material blitFinalImageMaterial;

        private ScriptableRenderer renderer;
        private SSGIFeature.SSGISettings settings;

        private List<int> rtsToRelease = new List<int>();
        private FilteringSettings thicknessFilterSettings;
        private List<ShaderTagId> thicknessShaderTagIDList = new List<ShaderTagId>();
        
        private Material debugBlitMaterial;
        private RenderTextureFormat halfHDR = RenderTextureFormat.ARGBHalf;
        private RenderTextureFormat fullHDR = RenderTextureFormat.ARGBFloat;
        private bool sceneLoadedHooked = false;

        private ReflectionProbe[] allProbes;
        private List<ReflectionProbe> filteredProbes = new List<ReflectionProbe>();
        private List<ReflectionProbe> activeProbes = new List<ReflectionProbe>();
        private float probesLastCollectTimestamp;
        private Plane[] frustumPlanes = new Plane[6];
        private float lastEditorUpdateTimestamp = -1f;
        private Vector2Int[] rndNumbers;

        private int prevMultiFrameCellSize;
        private Matrix4x4 backupProjectionMatrix;
        private RenderTextureDescriptor currentCamTexDescriptor;

        
        public SSGIPass(SSGIFeature.SSGISettings settings) {
            this.settings = settings; renderPassEvent = settings.RenderPassEvent; RefreshInput();
            if (!SystemInfo.SupportsRenderTextureFormat(halfHDR)) {
                halfHDR = RenderTextureFormat.ARGBFloat;
            }

            if (!sceneLoadedHooked) {
                sceneLoadedHooked = true;
                SceneManager.sceneUnloaded -= HandleSceneUnloaded;
                SceneManager.sceneUnloaded += HandleSceneUnloaded;
            }
        }

        private SSGIGraphCommands graphCommands;
        private TextureHandle graphColor, graphDepth;
        private struct GraphFrame {
            public UniversalCameraData cameraData;
            public UniversalRenderingData rendering;
            public UniversalLightData lights;
            public CullingResults cullResults => rendering.cullResults;
        }
        private sealed class GraphPassData { public SSGIGraphCommands commands; }
        public void Setup(ScriptableRenderer source) {
            RefreshInput(); renderPassEvent=settings.RenderPassEvent;
        }
        private void RefreshInput() {
            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Motion);
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData) {
            var camera=frameData.Get<UniversalCameraData>();
            if(!SSGIFeature.SSGIActive || (!camera.isSceneViewCamera && (!camera.camera.TryGetComponent<SSGICamera>(out var enabledCamera) || !enabledCamera.enabled))) return;
            if(camera.isSceneViewCamera && !SSGIFeature.ShowInSceneView) return;
            var resources=frameData.Get<UniversalResourceData>();
            if(resources.isActiveTargetBackBuffer) return;
            currentCamTexDescriptor=camera.cameraTargetDescriptor;
            graphColor=resources.activeColorTexture; graphDepth=resources.activeDepthTexture;
            graphCommands=new SSGIGraphCommands(graph,graphColor);
            graphCommands.Bind("_CameraDepthTexture",resources.cameraDepthTexture);
            graphCommands.Bind("_CameraNormalsTexture",resources.cameraNormalsTexture);
            graphCommands.SetGlobalVector("_CameraNormalsTexture_ST",new Vector4(1,1,0,0));
            if(resources.motionVectorColor.IsValid()) {
                graphCommands.Bind("_MotionVectorTexture",resources.motionVectorColor);
            } else graphCommands.SetGlobalTexture("_MotionVectorTexture",Texture2D.blackTexture);
            if(settings.UseDeferredRendering) for(int i=0;i<2;i++) if(resources.gBuffer[i].IsValid()) graphCommands.Bind("_GBuffer"+i,resources.gBuffer[i]);
            thicknessShaderTagIDList.Clear();
            thicknessShaderTagIDList.Add(new ShaderTagId("UniversalForward"));
            thicknessShaderTagIDList.Add(new ShaderTagId("UniversalForwardOnly"));
            thicknessShaderTagIDList.Add(new ShaderTagId("UniversalGBuffer"));
            thicknessShaderTagIDList.Add(new ShaderTagId("SRPDefaultUnlit"));
            var data=new GraphFrame{cameraData=camera,rendering=frameData.Get<UniversalRenderingData>(),lights=frameData.Get<UniversalLightData>()};
            RenderSSGI(graphCommands,data);
            using(var builder=graph.AddUnsafePass<GraphPassData>("MF.SSGI Render Graph",out var passData)) {
                passData.commands=graphCommands;
                foreach(var resource in graphCommands.textures) builder.UseTexture(resource.Key,graphCommands.created.Contains(resource.Key)?AccessFlags.Write:resource.Value);
                foreach(var list in graphCommands.lists) builder.UseRendererList(list);
                // Existing shaders communicate between internal stages through globals.
                builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (GraphPassData p,UnsafeGraphContext c)=>p.commands.Execute(CommandBufferHelpers.GetNativeCommandBuffer(c.cmd)));
            }
            graphCommands=null;
        }
        public void Dispose() {
            SceneManager.sceneUnloaded-=HandleSceneUnloaded;
            foreach(var wrapper in rtWrappers)wrapper.Dispose(); rtWrappers.Clear();
            SSGIGraphCommands.ReleaseImports(); reflectionProbeWrappers.Clear(); frameCountTable.Clear();
            foreach(var field in GetType().GetFields(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance))
                if(field.FieldType==typeof(Material) && field.GetValue(this) is Material m) CoreUtils.Destroy(m);
        }
        private void RenderSSGI(SSGIGraphCommands context, GraphFrame renderingData) {
            if (!SSGIFeature.SSGIActive) { return; }

            bool isSceneCam = false;
#if UNITY_EDITOR
            //If not an in-game camera
            if (Array.IndexOf(Camera.allCameras, renderingData.cameraData.camera) == -1) {
                isSceneCam = renderingData.cameraData.camera.name != "SceneCamera";
                //Skip all camera's that are non-game camera's
                if (!SSGIFeature.ShowInSceneView || isSceneCam) {
                    return;
                }
            }
#endif
            //Fetch post-process volume components
            SSGIVolumeComponent ssgiComp = null;
            if (VolumeManager.instance != null) {
                ssgiComp = VolumeManager.instance.stack.GetComponent<SSGIVolumeComponent>();
            }
            if (!ssgiComp || !ssgiComp.active) { return; }

            //Skip if camera doesn't have the SSGICamera attached
            if (renderingData.cameraData.camera.name != "SceneCamera"){
                SSGICamera camComp = renderingData.cameraData.camera.GetComponent<SSGICamera>();
                if (!camComp || !camComp.enabled) {
                    return;
                }
            }


            //Limit screen resolution
            float limitedResScale = 1f;
            int maxP = Mathf.Min(currentCamTexDescriptor.width, currentCamTexDescriptor.height);
            if (maxP > settings.Quality.MaxGIResolution) {
                limitedResScale = (float)settings.Quality.MaxGIResolution / (float)maxP;
            }

            //Raymarch required?
            bool doShadows = settings.Quality.UseRaymarchedShadows && settings.Raymarch && ssgiComp.ShadowIntensity.value > 0;
            bool doEncodeLightDir = settings.Quality.UseEncodedLightDirections && ssgiComp.LightDirInfluence.value > 0.0;

            Camera cam = renderingData.cameraData.camera;
            graphCommands.SetGlobalVector("_cam_world_forward", cam.transform.forward);
            if (cam.orthographic && cam.nearClipPlane < 0f) {
                graphCommands.SetGlobalVector("_cam_world_position", cam.transform.position + (cam.transform.forward * cam.nearClipPlane));
            } else {
                graphCommands.SetGlobalVector("_cam_world_position", cam.transform.position);
            }

            //Capture depth first
            CaptureWorldPositionsAndDepth(context, renderingData, limitedResScale);
            CaptureNormalsAndDepth(context, renderingData, limitedResScale);

            //Pre-render object thickness
            backupProjectionMatrix = renderingData.cameraData.camera.projectionMatrix;
            WriteSSGIObjects(context, renderingData, limitedResScale);
            if (doShadows) {
                WriteThicknessMask(context, renderingData, limitedResScale);
            }

            //Render MAIN
            CollectReflectionProbes(renderingData.cameraData.camera);
            FilterActiveReflectionProbes(renderingData.cameraData.camera, ssgiComp);
            CaptureScreenAndLight(context, renderingData, limitedResScale);
            
            //Execute SSGI
            GatherSSGI(isSceneCam, context, renderingData, limitedResScale, doShadows, ssgiComp, doEncodeLightDir);
            CaptureDebugRTSSGI(context, renderingData, doEncodeLightDir);
            Denoise(context, renderingData, limitedResScale, doEncodeLightDir);
            BlitToScreen(context, renderingData, limitedResScale, ssgiComp);
            CaptureDebugRTOthers(context, renderingData, doEncodeLightDir);

            //Release rendertextures
            SSGIGraphCommands cmd = graphCommands;
            foreach (int nameID in rtsToRelease) {
                cmd.ReleaseTemporaryRT(nameID);
            }
            rtsToRelease.Clear();
            context.ExecuteCommandBuffer(cmd);
            
        }

        private void CaptureWorldPositionsAndDepth(SSGIGraphCommands context, GraphFrame renderingData, float limitedResScale) {
            RenderTextureDescriptor descriptor = GetDescriptor(renderingData, halfHDR, 1f);

            //Setup material
            if (!worldPosMaterial) {
                worldPosMaterial = new Material(Shader.Find("MF_SSGI/DepthToWorldPos"));
            }

            //Setup command buffer
            SSGIGraphCommands cmd = graphCommands;
            RenderTexture target = GenBufferedRT(SSGIPassType.WorldPositions, renderingData.cameraData.camera, descriptor, cmd, FilterMode.Point);
            // Shader samples the URP global depth texture directly, not _MainTex.
            // A global RTHandle texture name is not a legacy temporary-RT source.
            cmd.Blit(Texture2D.blackTexture, target, worldPosMaterial);

            context.ExecuteCommandBuffer(cmd);
            
        }

        private void CaptureNormalsAndDepth(SSGIGraphCommands context, GraphFrame renderingData, float limitedResScale) {
            if (!captureNormalsMaterial) {
                captureNormalsMaterial = new Material(Shader.Find("MF_SSGI/CaptureNormals"));
            }

            if (settings.UseDeferredRendering) graphCommands.EnableKeyword("_USE_DEFERRED"); else graphCommands.DisableKeyword("_USE_DEFERRED");
            Camera cam = renderingData.cameraData.camera;
            //--- Highres version
            RenderTextureDescriptor descriptor = GetDescriptor(renderingData, halfHDR, 1f);
            int nameID_HQ = Shader.PropertyToID("_MF_SSGI_Normals_HQ");
            SSGIGraphCommands cmd = graphCommands;
            cmd.GetTemporaryRT(nameID_HQ, descriptor, FilterMode.Point);
            graphCommands.SetGlobalTexture("_WorldPositions", FetchBufferedRTwrapper(SSGIPassType.WorldPositions, cam).RT1);
            cmd.Blit(null, nameID_HQ, captureNormalsMaterial);
            rtsToRelease.Add(nameID_HQ);

            //--- Lowres version
            int nameID_LQ = Shader.PropertyToID("_MF_SSGI_Normals_LQ");
            descriptor = GetDescriptor(renderingData, halfHDR, settings.Quality.SSGIRenderScale * limitedResScale);
            descriptor.useMipMap = true;
            descriptor.autoGenerateMips = true;
            descriptor.mipCount = settings.Raymarch.RaymarchNormalDepthMipLevel;
            cmd.GetTemporaryRT(nameID_LQ, descriptor, FilterMode.Point);
            cmd.Blit(nameID_HQ, nameID_LQ);
            rtsToRelease.Add(nameID_LQ);

            //Execute
            context.ExecuteCommandBuffer(cmd);
            
        }

        private void WriteThicknessMask(SSGIGraphCommands context, GraphFrame renderingData, float limitedResScale) {
            if (!thicknessMaskMaterialBack) {
                thicknessMaskMaterialBack = new Material(Shader.Find("MF_SSGI/ThicknessMaskBack"));
            }
            if (!thicknessMaskMaterialFront) {
                thicknessMaskMaterialFront = new Material(Shader.Find("MF_SSGI/ThicknessMaskFront"));
            }

            SSGIGraphCommands cmd = graphCommands;

            Camera cam = renderingData.cameraData.camera;
            SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(thicknessShaderTagIDList, renderingData.rendering, renderingData.cameraData, renderingData.lights, sortingCriteria);
            RenderTextureDescriptor descriptor = GetDescriptor(renderingData, RenderTextureFormat.RFloat, settings.Quality.SSGIRenderScale * limitedResScale, true);

            drawingSettings.overrideMaterialPassIndex = 0;
            drawingSettings.enableDynamicBatching = true;
            drawingSettings.enableInstancing = true;

            thicknessFilterSettings = FilteringSettings.defaultValue;
            thicknessFilterSettings.layerMask = settings.Raymarch.ThicknessMaskLayers;

            //Set pivot-vs-normal: How to 'grow' the mesh when rendering the mask
            graphCommands.SetGlobalFloat("_object_thickness_pivot_vs_normal", settings.Raymarch.ObjectPivotToNormal);
            graphCommands.SetGlobalFloat("_object_min_thickness", settings.Raymarch.ObjectMinimalThickness);

            void RenderGeo() {
                //Expand and render
                if (settings.Raymarch.ObjectExpand > 0.0f) {
                    LimitFarClipPlane(renderingData, cmd, settings.ShadowMaxDistace);
                    cmd.SetGlobalFloat("_thickness_mask_expand", settings.Raymarch.ObjectExpand);
                    context.ExecuteCommandBuffer(cmd);
                    context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref thicknessFilterSettings);
                }

                //Normal size and re-render
                LimitFarClipPlane(renderingData, cmd, settings.ShadowMaxDistace);
                cmd.SetGlobalFloat("_thickness_mask_expand", 0.0f);
                context.ExecuteCommandBuffer(cmd);
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref thicknessFilterSettings);
            }

            //Pre-render custom depth-pass as Unity's depth in Orthographic messes up everything
            //Pass 1: Setup rendertarget
            descriptor.colorFormat = RenderTextureFormat.RHalf;
            int nameIDPrePass = Shader.PropertyToID("_MF_SSGI_ThicknessMask_Prepass");
            SetTempRTActive(context, cmd, descriptor, nameIDPrePass);

            //Pass 1: Start drawing
            drawingSettings.overrideMaterial = thicknessMaskMaterialBack;
            drawingSettings.overrideMaterialPassIndex = 0;
            RenderGeo();
            rtsToRelease.Add(nameIDPrePass);

            //Pass 2: Setup rendertarget
            descriptor.colorFormat = RenderTextureFormat.RGHalf;
            int nameIDPostPass = Shader.PropertyToID("_MF_SSGI_ThicknessMask");
            SetTempRTActive(context, cmd, descriptor, nameIDPostPass, Color.green, FilterMode.Point);

            //Pass 2: Ortho & slipping Depth-issue work-around
            drawingSettings.overrideMaterialPassIndex = 0;
            drawingSettings.overrideMaterial = thicknessMaskMaterialFront;
            RenderGeo();
            rtsToRelease.Add(nameIDPostPass);

            //Done
            
        }

        private void WriteSSGIObjects(SSGIGraphCommands context, GraphFrame renderingData, float limitedResScale) {
            //Test if any SSGIObjects are in the scene
            bool requiresSSGIObjectsRendering =
                settings.Advanced.UseSSGIObjectOverrides &&
                RequestedObjects.Count((item) => item && item.RequiresSSGIMaskRendering) > 0;

            graphCommands.SetGlobalFloat("_default_clip_depth_bias", settings.Advanced.DefaultClipDepthBias);
            if (requiresSSGIObjectsRendering) graphCommands.EnableKeyword("_USE_SSGI_OBJECTS"); else graphCommands.DisableKeyword("_USE_SSGI_OBJECTS");

            //Gen material
            if (!ssgiObjectMaterial) {
                ssgiObjectMaterial = new Material(Shader.Find("MF_SSGI/SSGIObjects"));
            }

            //Setup rendertarget
            SSGIGraphCommands cmd = graphCommands;
            RenderTextureDescriptor descriptor = GetDescriptor(renderingData, halfHDR, 1f, true);
            int nameID = Shader.PropertyToID("_MF_SSGI_SSGIObjects");
            SetTempRTActive(context, cmd, descriptor, nameID, Color.white, FilterMode.Point);
            LimitFarClipPlane(renderingData, cmd, settings.SSGIRangeMax);
            rtsToRelease.Add(nameID);

            //Render objects
            GeometryUtility.CalculateFrustumPlanes(renderingData.cameraData.camera, frustumPlanes);
            foreach (SSGIObject obj in RequestedObjects) {
                if (obj.RequiresSSGIMaskRendering) {
                    for (int i = 0; i < obj.AffectedRenderers.Count; i++) {
                        Renderer renderer = obj.AffectedRenderers[i];
                        if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !GeometryUtility.TestPlanesAABB(frustumPlanes, renderer.bounds)) { continue; }
                        for (int j = 0; j < obj.SubMeshCounts[i]; j++) {
                            cmd.DrawRenderer(renderer, ssgiObjectMaterial, j, 0);
                        }
                    }
                }
            }

            //Done
            cmd.SetProjectionMatrix(renderingData.cameraData.GetProjectionMatrix());
            context.ExecuteCommandBuffer(cmd);
            
        }

        private void LimitFarClipPlane(GraphFrame renderingData, SSGIGraphCommands cmd, float far) {
            Camera cam = renderingData.cameraData.camera;
            if (cam.orthographic) {
                //TODO: Limit ortho cam
            } else {
                cmd.SetProjectionMatrix(Matrix4x4.Perspective(cam.fieldOfView, cam.aspect, cam.nearClipPlane, Mathf.Min(cam.farClipPlane, far)));
            }
        }

        private void CaptureScreenAndLight(SSGIGraphCommands context, GraphFrame renderingData, float limitedResScale) {
            if (!captureLightMaterial) {
                captureLightMaterial = new Material(Shader.Find("MF_SSGI/CaptureLight"));
            }
            if (!ssgiExpandVerticesMaterial) {
                ssgiExpandVerticesMaterial = new Material(Shader.Find("MF_SSGI/ExpandVertices"));
            }
            SSGIGraphCommands cmd = graphCommands;

            //Full-res screen capture
            RenderTextureDescriptor descriptor = GetDescriptor(renderingData, halfHDR, 1f);
            int nameID = Shader.PropertyToID("_MF_SSGI_ScreenCapture");
            if (!settings.UseDeferredRendering && settings.Lighting.AlbedoDetailBoost > 0f) {
                descriptor.useMipMap = true;
                descriptor.autoGenerateMips = true;
                descriptor.mipCount = (int)Mathf.Ceil(settings.Lighting.AlbedoDetailBoostMipLevle);
            }

            cmd.GetTemporaryRT(nameID, descriptor, FilterMode.Trilinear); //2021 compatible
            cmd.Blit(graphColor, nameID);
            rtsToRelease.Add(nameID);

            //Execute context, as we need the ScreenCapture before we write the expand vertices
            context.ExecuteCommandBuffer(cmd);
            

            //Set worldposition
            RTWrapper worldPosWrapper = FetchBufferedRTwrapper(SSGIPassType.WorldPositions, renderingData.cameraData.camera);
            graphCommands.SetGlobalTexture("_WorldPositions", worldPosWrapper.RT1);
            
            //Reflection Probe Fallback shadows
            cmd.SetGlobalInt("_ssgi_refprobe_raymarch_samples", settings.Quality.ReflectionProbeFallbackIndirectShadows ? settings.Quality.ReflectionProbeFallbackRaymarchSamples : 0);

            //SSGI-res light-info capture
            descriptor = GetDescriptor(renderingData, halfHDR, settings.Quality.SSGIRenderScale * limitedResScale);
            nameID = Shader.PropertyToID("_MF_SSGI_LightCapture");
            cmd.GetTemporaryRT(nameID, descriptor, FilterMode.Point);
            cmd.Blit(graphColor, nameID, captureLightMaterial); //2021 compatible
            rtsToRelease.Add(nameID);

            //Albedo boost
            graphCommands.SetGlobalFloat("_albedo_boost", settings.Lighting.AlbedoDetailBoost);
            graphCommands.SetGlobalFloat("_albedo_boost_miplevel", settings.Lighting.AlbedoDetailBoostMipLevle);

            //Set properties
            graphCommands.SetGlobalFloat("_OneOverScreenResX", 1f / (float)descriptor.width);
            graphCommands.SetGlobalFloat("_OneOverScreenResY", 1f / (float)descriptor.height);
            graphCommands.SetGlobalFloat("_ExpandRangeMin", settings.SSGIRangeMax * settings.Advanced.ExpandObjectsRangeFactorMin);
            graphCommands.SetGlobalFloat("_ExpandRangeMax", settings.SSGIRangeMax * settings.Advanced.ExpandObjectsRangeFactorMax);

            //Draw SSGIObjects that need expanded vertices
            cmd.SetRenderTarget(nameID);
            LimitFarClipPlane(renderingData, cmd, settings.SSGIRangeMax);
            GeometryUtility.CalculateFrustumPlanes(renderingData.cameraData.camera, frustumPlanes);

            foreach (SSGIObject obj in RequestedObjects) {
                if (obj.RequiresVertexExpandRendering) {
                    for(int i = 0; i < obj.AffectedRenderers.Count; i++) {
                        Renderer renderer = obj.AffectedRenderers[i];
                        if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !GeometryUtility.TestPlanesAABB(frustumPlanes, renderer.bounds)) { continue; }
                        for (int j = 0; j < obj.SubMeshCounts[i]; j++) {
                            cmd.DrawRenderer(renderer, ssgiExpandVerticesMaterial, j, 0);
                        }
                    }
                }
            }

            //Execute context, as we need the Lightcapture before we write the expand vertices
            context.ExecuteCommandBuffer(cmd);
            
        }

        private void GatherSSGI(bool isSceneCam, SSGIGraphCommands context, GraphFrame renderingData, float limitedResScale, bool doShadows, SSGIVolumeComponent ssgiComp, bool doEncodeLightDir) {
            //Setup material
            if (!scanEnvironmentMaterial) {
                scanEnvironmentMaterial = new Material(Shader.Find("MF_SSGI/SSGI"));
            }

            //Throw warning
            if(settings.Quality.MultiFrameCellSize == 2) {
                Debug.LogWarning("MultiFrameCellSize is set to 2, this causes rounding errors. Please set to 0 to disable or a value of 3 or higher (at QualitySettings)");
            }

            //Setup shuffled RND
            Camera cam = renderingData.cameraData.camera;
            graphCommands.SetGlobalInt("_debug_reprojection", settings.DebugReprojection ? 1 : 0);
            graphCommands.SetGlobalFloat("_multiframe_cell_size", (isSceneCam || !Application.isPlaying) ? 0 : settings.Quality.MultiFrameCellSize);
            graphCommands.SetGlobalFloat("_multiframe_energy_falloff", settings.Quality.MultiFrameEnergyFalloff);

            graphCommands.SetGlobalFloat("_multiframe_dist", settings.Advanced.MultiFrameDistanceThreshold);
            graphCommands.SetGlobalFloat("_multiframe_shadow_compensate", settings.Advanced.MultiFrameShadowCompensate);
            graphCommands.SetGlobalInt("_multiframe_apply_multisample", settings.Advanced.MultiFrameApplyMultiSample ? 1 : 0);

            if (settings.Quality.MultiFrameCellSize > 0) {
                int cellSqr = settings.Quality.MultiFrameCellSize * settings.Quality.MultiFrameCellSize;
                if (rndNumbers == null || rndNumbers.Length != cellSqr) {
                    rndNumbers = new Vector2Int[cellSqr];
                    int i = 0;
                    for (int x = 0; x < settings.Quality.MultiFrameCellSize; x++) {
                        for (int y = 0; y < settings.Quality.MultiFrameCellSize; y++) {
                            rndNumbers[i++] = new Vector2Int(x,y);
                        }
                    }
                    Shuffle(rndNumbers);
                }
                //Set RND shuffled pixel numbers
                if (!frameCountTable.ContainsKey(cam)) {
                    frameCountTable.Add(cam, 0);
                }
                frameCountTable[cam]++;
                graphCommands.SetGlobalVector("_rnd_pixel", (Vector2)rndNumbers[frameCountTable[cam] % cellSqr]);
            }
            //Debug.Log(Shader.GetGlobalInt("_rnd_pixel_x") + ", "+ Shader.GetGlobalInt("_rnd_pixel_y"));

            //Pass pattern values
            RenderTextureDescriptor descriptor = GetDescriptor(renderingData, doEncodeLightDir ? fullHDR : halfHDR, settings.Quality.SSGIRenderScale * limitedResScale);

            //Set globals
            graphCommands.SetGlobalInt("_ssgi_samples_hq", settings.Quality.SSGISamplesHQ);
            graphCommands.SetGlobalInt("_ssgi_samples_backfill", settings.Quality.SSGISamplesBackfill);
            graphCommands.SetGlobalFloat("_ssgi_samples_reduction", settings.Advanced.SSGISamplesReduction);
            graphCommands.SetGlobalVector("_ssgi_res", new Vector4(descriptor.width, descriptor.height, 0f, 0f));
            graphCommands.SetGlobalFloat("_edge_vignette", settings.Advanced.SearchEdgeVignette);
            if (doEncodeLightDir) graphCommands.EnableKeyword("_ENCODE_LIGHTDIR"); else graphCommands.DisableKeyword("_ENCODE_LIGHTDIR");
            graphCommands.SetGlobalFloat("_multi_sample_normal_distance", settings.Advanced.MultiSampleNormalsDistance);

            graphCommands.SetGlobalFloat("_scan_base_range", settings.Advanced.Search2DRange);
            graphCommands.SetGlobalFloat("_scan_ratio_y", (float)descriptor.height / (float)descriptor.width);
            graphCommands.SetGlobalFloat("_scan_noise", settings.Advanced.Search2DNoise);

            //Screen space search
            graphCommands.SetGlobalFloat("_scan_depth_threshold_factor", settings.Advanced.ScanDepthThresholdFactor);
            graphCommands.SetGlobalFloat("_ssgi_range_max", settings.SSGIRangeMax);
            graphCommands.SetGlobalFloat("_ssgi_range_min", settings.SSGIRangeMin);

            //Energy
            graphCommands.SetGlobalFloat("_max_light_attenuation", settings.Lighting.MaxLightAttenuation);
            graphCommands.SetGlobalFloat("_max_input_energy", settings.Lighting.MaxInputEnergy);
            graphCommands.SetGlobalFloat("_frustum_pixel_size", 2f * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2f) * settings.Lighting.DistanceEnergyBoost);

            //SSGIComp
            graphCommands.SetGlobalFloat("_ssgi_intensity", ssgiComp.LightIntensity.value * 100f); //*100 to compensate for V1.0 to V1.1 update
            graphCommands.SetGlobalFloat("_light_falloff_distance", ssgiComp.LightFalloffDistance.value);
            graphCommands.SetGlobalFloat("_skybox_influence", ssgiComp.SkyboxInfluence.value);
            
            //Light cast/receive
            graphCommands.SetGlobalFloat("_light_cast_dot_min", settings.Lighting.LightCastDotMin);
            graphCommands.SetGlobalFloat("_light_cast_dot_max", settings.Lighting.LightCastDotMax);
            graphCommands.SetGlobalFloat("_light_receive_dot_min", settings.Lighting.LightReceiveDotMin);
            //compensate for missing Light-directions
            graphCommands.SetGlobalFloat("_light_receive_dot_max", settings.Quality.UseEncodedLightDirections ? settings.Lighting.LightReceiveDotMax : Mathf.Min(1f, settings.Lighting.LightReceiveDotMax * 2f));

            graphCommands.SetGlobalFloat("_depth_cutoff_near", settings.Advanced.CamCutoffNear);
            graphCommands.SetGlobalFloat("_depth_cutoff_far", settings.Advanced.CamCutoffFar);

            //Distance-based scan-2d-size
            float distance = settings.SSGIRangeMax * settings.ScanDepth2DRangeFactor * settings.Quality.ScanDepthMultiplier;
            graphCommands.SetGlobalFloat("_scansize_distance_multiplier", Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            graphCommands.SetGlobalFloat("_scansize_distance_threshold", distance);
            graphCommands.SetGlobalFloat("_scansize_distance_base_size", distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            graphCommands.SetGlobalFloat("_scansize_ortho", settings.Orthographic2DRangeFactor * settings.Quality.ScanDepthMultiplier);

            if (doShadows) {
                //Addative
                graphCommands.SetGlobalFloat("_shadow_intensity", ssgiComp.ShadowIntensity.value);

                //SSGIComponent
                graphCommands.SetGlobalFloat("_contact_shadow_range", ssgiComp.ContactShadowsRange.value);
                graphCommands.SetGlobalFloat("_casted_shadow_range", ssgiComp.CastedShadowsRange.value);
                graphCommands.SetGlobalFloat("_casted_shadow_intensity", ssgiComp.CastedShadowsIntensity.value);
                graphCommands.SetGlobalFloat("_casted_shadow_omni_dir", ssgiComp.CastedShadowsOmniDirectional.value);
                graphCommands.SetGlobalFloat("_result_shadows_contrast", ssgiComp.ShadowContrast.value);
                graphCommands.SetGlobalFloat("_contact_shadow_soft_knee", ssgiComp.ContactShadowsSoftKnee.value);
                graphCommands.SetGlobalFloat("_casted_shadow_soft_knee", ssgiComp.CastedShadowsSoftKnee.value);

                //Raymarching
                graphCommands.SetGlobalFloat("_raymarch_min_distance", settings.ShadowMinDistace);
                graphCommands.SetGlobalFloat("_raymarch_max_distance", settings.ShadowMaxDistace);

                graphCommands.SetGlobalFloat("_raymarch_samples_hq", settings.Quality.RaymarchSamplesHQ);
                graphCommands.SetGlobalFloat("_raymarch_samples_backfill", settings.Quality.RaymarchSamplesBackfill);
                graphCommands.SetGlobalFloat("_raymarch_cubic_distance_falloff", settings.Quality.RaymarchCubicDistanceFalloff);

                graphCommands.SetGlobalFloat("_raymarch_depth_bias", settings.Raymarch.RaymarchDepthBias);
                graphCommands.SetGlobalInt("_raymarch_normal_depth_miplevel", settings.Raymarch.RaymarchNormalDepthMipLevel);
                graphCommands.SetGlobalFloat("_raymarch_surface_depth_bias_min", settings.Raymarch.RaymarchSurfaceDepthBiasMin);
                graphCommands.SetGlobalFloat("_raymarch_surface_depth_bias_max", settings.Raymarch.RaymarchSurfaceDepthBiasMax);
                graphCommands.SetGlobalFloat("_raymarch_min_hit_count", settings.Raymarch.RaymarchMinimumHitCount);
                graphCommands.SetGlobalFloat("_raymarch_contact_min_dist", settings.Raymarch.RaymarchContactMinDistance);
                graphCommands.SetGlobalFloat("_raymarch_casted_min_dist", settings.Raymarch.RaymarchCastedMinDistance);
                graphCommands.SetGlobalFloat("_raymarch_shorten", settings.Quality.RaymarchMaxRangeFactor);
            } else {
                graphCommands.SetGlobalFloat("_shadow_intensity", 0f);
            }

            //Setup command buffer
            SSGIGraphCommands cmd = graphCommands;

            //Reset Far clipping distance
            cmd.SetProjectionMatrix(backupProjectionMatrix);

            //Reflection Probe Fallback shadows
            cmd.SetGlobalInt("_ssgi_refprobe_raymarch_samples", settings.Quality.ReflectionProbeFallbackDirectShadows ? settings.Quality.ReflectionProbeFallbackRaymarchSamples : 0);

            RenderTexture target = GenBufferedRT(SSGIPassType.SSGIColor, cam, descriptor, cmd, FilterMode.Point);
            RTWrapper worldPosWrapper = FetchBufferedRTwrapper(SSGIPassType.WorldPositions, cam);
            RTWrapper ssgiWrapper = FetchBufferedRTwrapper(SSGIPassType.SSGIColor, cam);
            graphCommands.SetGlobalTexture("_WorldPositions", worldPosWrapper.RT1);
            graphCommands.SetGlobalTexture("_PrevWorldPositions", worldPosWrapper.RT2);
            graphCommands.SetGlobalTexture("_PrevSSGI", ssgiWrapper.RT2);
            cmd.Blit(null, target, scanEnvironmentMaterial);

            context.ExecuteCommandBuffer(cmd);
            
        }

        private void Denoise(SSGIGraphCommands context, GraphFrame renderingData, float limitedResScale, bool doEncodeLightDir) {
            if (!denoiseImageMaterial) {
                denoiseImageMaterial = new Material(Shader.Find("MF_SSGI/Denoise"));
            }

            //Setup variables
            graphCommands.SetGlobalFloat("_denoise_min_dot_match", settings.Advanced.DenoiseNormalDotMin);
            graphCommands.SetGlobalFloat("_denoise_max_dot_match", settings.Advanced.DenoiseNormalDotMax);
            graphCommands.SetGlobalFloat("_denoise_max_depth_diff", settings.Advanced.DenoiseDepthDiffTheshold);
            graphCommands.SetGlobalFloat("_denoise_color_normal_contribution", settings.Advanced.DenoiseColorNormalContribution);
            graphCommands.SetGlobalFloat("_denoise_shadow_normal_contribution", settings.Advanced.DenoiseShadowNormalContribution);
            graphCommands.SetGlobalInt("_denoise_min_shadows_hit_count", settings.Raymarch.DenoiseShadowsMinHitCount);

            //Setup pre-denoise
            SSGIGraphCommands cmd = graphCommands;

            //--------- Pre-desnoise
            RenderTextureDescriptor descriptor = GetDescriptor(renderingData, doEncodeLightDir ? fullHDR : halfHDR, settings.Quality.SSGIRenderScale * limitedResScale);
            cmd.SetGlobalVector("_oneover_denoise_res", new Vector4(1f / (float)descriptor.width, 1f / (float)descriptor.height, 0f, 0f));
            graphCommands.SetGlobalFloat("_denoise_energy_compensation", settings.Quality.IntensityCompensation);
            graphCommands.SetGlobalFloat("_denoise_shadow_compensation", settings.Quality.ShadowCompensation);

            FilterMode filterMode = doEncodeLightDir ? FilterMode.Point : FilterMode.Trilinear;
            int nameID1 = Shader.PropertyToID("_MF_SSGI_Pre_Denoised_1");
            int nameID2 = Shader.PropertyToID("_MF_SSGI_Pre_Denoised_2");
            int nameIDFinal = Shader.PropertyToID("_MF_SSGI_Denoised_Final");
            cmd.GetTemporaryRT(nameID1, descriptor, filterMode);
            cmd.GetTemporaryRT(nameID2, descriptor, filterMode);
            cmd.GetTemporaryRT(nameIDFinal, descriptor, filterMode);

            for (int i = 0; i < settings.Quality.DenoisePasses; i++) {
                //Decrement added result foreach new pass
                float pixelSize = settings.Quality.DenoisePasses > 1 ?
                    Mathf.Lerp(settings.Quality.PreDenoisePixelSize, 1f, (float)i / (float)(settings.Quality.DenoisePasses - 1)) :
                    settings.Quality.PreDenoisePixelSize;

                cmd.SetGlobalFloat("_denoise_pixel_size", pixelSize);

                //Blit multiple passes
                if (i == 0) {
                    cmd.Blit(FetchBufferedRTwrapper(SSGIPassType.SSGIColor, renderingData.cameraData.camera).RT1, nameID1, denoiseImageMaterial); //Copy SSGI to D1
                } else if (i % 2 != 0) {
                    cmd.Blit(nameID1, i == settings.Quality.DenoisePasses - 1 ? nameIDFinal : nameID2, denoiseImageMaterial); //Copy D1 to D2/Final
                } else {
                    cmd.Blit(nameID2, i == settings.Quality.DenoisePasses - 1 ? nameIDFinal : nameID1, denoiseImageMaterial); //Copy D2 to D1/Final
                }
            }

            //--------- TAA on shadow channel
            //Blends current denoised shadow with reprojected previous-frame shadow under
            //a 5-tap neighborhood clamp. Greatly reduces temporal noise on shadows without
            //the spatial blur cost. Color/light-dir pass through unchanged.
            if (!taaShadowMaterial) {
                taaShadowMaterial = new Material(Shader.Find("MF_SSGI/TAA"));
            }
            cmd.SetGlobalFloat("_taa_shadow_blend", settings.Advanced.ShadowHistoryWeight);
            cmd.SetGlobalFloat("_taa_world_pos_threshold", settings.Advanced.ShadowHistoryPositionThreshold);
            RenderTexture taaCurrent = GenBufferedRT(SSGIPassType.TAAHistory, renderingData.cameraData.camera, descriptor, cmd, filterMode);
            RTWrapper taaWrapper = FetchBufferedRTwrapper(SSGIPassType.TAAHistory, renderingData.cameraData.camera);
            cmd.SetGlobalTexture("_TAAHistory", taaWrapper.RT2 != null ? (Texture)taaWrapper.RT2 : Texture2D.blackTexture);

            //TAA's disocclusion check samples _WorldPositions / _PrevWorldPositions. They're set as
            //material properties on scanEnvironmentMaterial / captureLightMaterial / captureNormalsMaterial,
            //but NOT on taaShadowMaterial. Without an explicit bind here, those samplers fall back to
            //whatever is globally bound by another camera or previous frame — producing intermittent
            //disocclusion misfires that show up as flickering cube silhouettes when DenoisePasses is low.
            RTWrapper worldPosWrapperTAA = FetchBufferedRTwrapper(SSGIPassType.WorldPositions, renderingData.cameraData.camera);
            cmd.SetGlobalTexture("_WorldPositions", worldPosWrapperTAA.RT1);
            cmd.SetGlobalTexture("_PrevWorldPositions",
                worldPosWrapperTAA.RT2 != null ? (Texture)worldPosWrapperTAA.RT2 : Texture2D.blackTexture);

            cmd.Blit(nameIDFinal, taaCurrent, taaShadowMaterial);
            //Re-bind the global so FinalBlit reads the TAA-stabilized output
            cmd.SetGlobalTexture("_MF_SSGI_Denoised_Final", taaCurrent);

            //Done
            rtsToRelease.Add(nameID1);
            rtsToRelease.Add(nameID2);
            rtsToRelease.Add(nameIDFinal);

            context.ExecuteCommandBuffer(cmd);
            
        }

        private void BlitToScreen(SSGIGraphCommands context, GraphFrame renderingData, float limitedResScale, SSGIVolumeComponent ssgiComp) {
            if (!blitFinalImageMaterial) {
                blitFinalImageMaterial = new Material(Shader.Find("MF_SSGI/FinalBlit"));
            }

            graphCommands.SetGlobalFloat("_debug_screen_coverage", settings.DebugScreenCoverage);
            graphCommands.SetGlobalInt("_debug_motion_vectors", settings.DebugMotionVectors ? 1 : 0);
            graphCommands.SetGlobalInt("_debug_albedo", settings.DebugAlbedo ? 1 : 0);


            graphCommands.SetGlobalFloat("_max_output_energy", settings.Lighting.MaxOutputEnergy);

            // Publish only when this camera actually executes the SSGI composition.
            graphCommands.SetGlobalFloat("_RavenSSGICompositionReady", 1f);
            graphCommands.SetGlobalVector("_RavenSSGIComposition", new Vector4(ssgiComp.PreMultiply.value, ssgiComp.FinalContrast.value, ssgiComp.FinalIntensity.value, settings.SSGIRangeMin));
            graphCommands.SetGlobalFloat("_RavenSSGICompositionRangeMax", settings.SSGIRangeMax);
            //SSGIComponent
            graphCommands.SetGlobalFloat("_composit_lightdir_influence", settings.Quality.UseEncodedLightDirections ? ssgiComp.LightDirInfluence.value : 0f);
            graphCommands.SetGlobalFloat("_composit_lightdir_normal_boost", ssgiComp.NormalmapBoost.value);
            graphCommands.SetGlobalFloat("_composit_final_contrast", ssgiComp.FinalContrast.value);
            graphCommands.SetGlobalFloat("_composit_final_intensity", ssgiComp.FinalIntensity.value);
            graphCommands.SetGlobalFloat("_composit_occlusion_intensity", ssgiComp.PreMultiply.value);
            graphCommands.SetGlobalColor("_composit_color", ssgiComp.GITint.value);
            graphCommands.SetGlobalFloat("_composit_gi_contrast", ssgiComp.GIContrast.value);
            graphCommands.SetGlobalFloat("_composit_gi_saturate", ssgiComp.GISaturation.value);
            graphCommands.SetGlobalFloat("_composit_gi_vibrance", ssgiComp.GIVibrance.value);
            graphCommands.SetGlobalVector("_shadow_boost_tint", ssgiComp.ShadowTint.value);
            graphCommands.SetGlobalFloat("_shadow_boost_exp", ssgiComp.ShadowExponential.value);

            graphCommands.SetGlobalFloat("_shadow_lambert_influence", settings.Lighting.ShadowsLambertInfluence);
            graphCommands.SetGlobalVector("_light_direction_info_a", new Vector4(settings.Lighting.LightDirectionDotMinSoft, settings.Lighting.LightDirectionDotMaxSoft, settings.Lighting.LightDirectionIntensitySoft));
            graphCommands.SetGlobalVector("_light_direction_info_b", new Vector4(settings.Lighting.LightDirectionDotMinHard, settings.Lighting.LightDirectionDotMaxHard, settings.Lighting.LightDirectionIntensityHard));

            graphCommands.SetGlobalColor("_deferred_specular_tint", settings.Lighting.SpecularTint);
            graphCommands.SetGlobalFloat("_albedo_min_whiteness", settings.Lighting.MinimumAlbedoWhiteness);

            graphCommands.SetGlobalFloat("_forward_albedo_contrast", settings.Lighting.AlbedoContrast);    
            graphCommands.SetGlobalFloat("_forward_albedo_subtract_fog", RenderSettings.fog ? settings.Lighting.AlbedoSubtractFogColor : 0f);
            graphCommands.SetGlobalFloat("_forward_albedo_subtract_sky", settings.Lighting.AlbedoSubtractSkyColor);

            graphCommands.SetGlobalInt("_aa_quality_level", (int)settings.Quality.AAQuality);
            graphCommands.SetGlobalInt("_aa_debug_edge_detect", settings.Advanced.DebugAAEdgeDetect ? 1 : 0);
            graphCommands.SetGlobalFloat("_aa_edge_detect_depth_theshold", settings.Advanced.AAEdgeDetectDepthThreshold);
            graphCommands.SetGlobalFloat("_aa_edge_detect_dot_theshold", settings.Advanced.AAEdgeDetectDotThreshold);
            graphCommands.SetGlobalFloat("_aa_normal_match_threshold", settings.Advanced.AANormalMapMatchThreshold);
            graphCommands.SetGlobalVector("_aa_sample_distance", new Vector4(
                (1f / (float)currentCamTexDescriptor.width) * settings.Quality.SSGIRenderScale * limitedResScale * settings.Advanced.FinalCompositAARange,
                (1f / (float)currentCamTexDescriptor.height) * settings.Quality.SSGIRenderScale * limitedResScale * settings.Advanced.FinalCompositAARange, 
            0f, 0f));

            SSGIGraphCommands cmd = graphCommands;
            cmd.SetRenderTarget(graphColor, graphDepth); //Unity 2021 compatible
            cmd.Blit(null, graphColor, blitFinalImageMaterial);

            context.ExecuteCommandBuffer(cmd);
            
        }


        private void SetTempRTActive(SSGIGraphCommands context, SSGIGraphCommands cmd, RenderTextureDescriptor descriptor, int nameID, Color color = default(Color), FilterMode filterMode = FilterMode.Point) {
            cmd.GetTemporaryRT(nameID, descriptor, filterMode);
            cmd.SetRenderTarget(nameID);
            cmd.ClearRenderTarget(true, true, color);
            context.ExecuteCommandBuffer(cmd);
            
        }

        private RenderTextureDescriptor GetDescriptor(GraphFrame renderingData, RenderTextureFormat format, float customScale, bool requiresDepth = false, int msaa = 1) {
            RenderTextureDescriptor descriptor = currentCamTexDescriptor;
            descriptor.width = (int)(currentCamTexDescriptor.width * customScale);
            descriptor.height = (int)(currentCamTexDescriptor.height * customScale);
            descriptor.colorFormat = format;
            descriptor.depthBufferBits = requiresDepth ? 16 : 0;
            
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.sRGB = false;
            descriptor.msaaSamples = msaa;
            return descriptor;
        }

        private void CollectReflectionProbes(Camera cam) {
            bool cameraAdded = false;
            if (!reflectionProbeWrappers.TryGetValue(cam, out List<ReflectionProbeWrapper> cameraWrappers)) {
                cameraWrappers = new List<ReflectionProbeWrapper>();
                reflectionProbeWrappers.Add(cam, cameraWrappers);
                cameraAdded = true;
            }

            //Collect in scene
            float time = GetTime();
            if (allProbes == null || time - probesLastCollectTimestamp > settings.Fallback.CollectReflectionProbesInterval) {
                allProbes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.InstanceID);

                //Update all wrappers for all camera's
                foreach (KeyValuePair<Camera, List<ReflectionProbeWrapper>> pair in reflectionProbeWrappers) {
                    pair.Value.RemoveAll(item => !item.Probe || Array.IndexOf(allProbes, item.Probe) < 0);
                    foreach (ReflectionProbe probe in allProbes) {
                        if (pair.Value.FindIndex(item => item.Probe == probe) == -1) {
                            pair.Value.Add(new ReflectionProbeWrapper(probe));
                        }
                    }
                }

                probesLastCollectTimestamp = time;
            } else if (cameraAdded && allProbes != null) {
                foreach (ReflectionProbe probe in allProbes) {
                    if (probe) cameraWrappers.Add(new ReflectionProbeWrapper(probe));
                }
            }
        }

        private void FilterActiveReflectionProbes(Camera camera, SSGIVolumeComponent ssgiComp) {
            //By default its disabled, only enable when active probes are found for this camera
            graphCommands.SetGlobalFloat("_ssgi_fallback_direct_intensity", 0f);
            graphCommands.SetGlobalFloat("_ssgi_fallback_indirect_intensity", 0f);
            
            //Early return when disabled
            if (allProbes == null) { return; }
            if (settings.Quality.MaxProbesPerPixel == 0) {
                return;
            }

            //Setup cheap non-alloc data
            GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
            filteredProbes.Clear();
            foreach (ReflectionProbe probe in allProbes) {
                if(!probe || !probe.texture || !probe.enabled || !probe.gameObject.activeInHierarchy || !GeometryUtility.TestPlanesAABB(frustumPlanes, probe.bounds)) { continue; }
                filteredProbes.Add(probe);
            }

            //Sort probes - Highest score wins
            Vector3 center = camera.transform.position;
            Vector3 forward = camera.transform.forward;
            filteredProbes.Sort((a, b) => GetProbeSortingValue(b, center, forward).CompareTo(GetProbeSortingValue(a, center, forward)));

            //Get correct delta-time
            float deltaTime = Time.deltaTime;// GetDeltaTime();
             
            //Populate a new list based on Fade-status
            if (reflectionProbeWrappers.ContainsKey(camera)) {
                activeProbes.Clear();
                List<ReflectionProbeWrapper> wrappers = reflectionProbeWrappers[camera];
                foreach (ReflectionProbeWrapper wrapper in wrappers) {
                    int idx = filteredProbes.IndexOf(wrapper.Probe);
                    float target = (idx != -1 && idx < settings.Quality.MaxProbesPerPixel) ? 1f : 0f;
                    wrapper.Fade = Mathf.MoveTowards(wrapper.Fade, target, deltaTime / settings.Fallback.EnterExitFadeDuration);
                    if (wrapper.Fade > 0f && wrapper.Probe) {
                        activeProbes.Add(wrapper.Probe);
                    }
                }

                if (activeProbes.Count > 0) {
                    activeProbes.Sort((a, b) => filteredProbes.IndexOf(a).CompareTo(filteredProbes.IndexOf(b)));

                    //Set shader values
                    graphCommands.SetGlobalInt("_ssgi_refprobe_count", activeProbes.Count);
                    graphCommands.SetGlobalFloat("_ssgi_refprobe_mip", settings.Fallback.ProbeSampleMipLevel);
                    graphCommands.SetGlobalFloat("_ssgi_refprobe_falloff", settings.Fallback.ProbeVolumeFalloffDistance);
                    graphCommands.SetGlobalFloat("_ssgi_refprobe_realtime_intensity", settings.Fallback.ProbeRealtimeIntensity);
                    graphCommands.SetGlobalFloat("_ssgi_refprobe_realtime_saturation", settings.Fallback.ProbeRealtimeSaturation);
                    graphCommands.SetGlobalFloat("_ssgi_refprobe_realtime_power", settings.Fallback.ProbeRealtimeExp);

                    for (int i = 0; i < activeProbes.Count; i++) {
                        ReflectionProbeWrapper wrapper = wrappers.Find(item => item.Probe == activeProbes[i]);
                        graphCommands.SetGlobalTexture("_ssgi_refprobe_texture_" + i, activeProbes[i].texture);
                        graphCommands.SetGlobalVector("_ssgi_refprobe_center_" + i, activeProbes[i].bounds.center);
                        graphCommands.SetGlobalVector("_ssgi_refprobe_source_" + i, activeProbes[i].transform.position);
                        graphCommands.SetGlobalVector("_ssgi_refprobe_extents_" + i, activeProbes[i].bounds.extents);
                        graphCommands.SetGlobalVector("_ssgi_refprobe_rayinfo_" + i, GetRefProbeRaymarchParams(camera, activeProbes[i].transform.position));

                        //Intensity
                        float intensity = activeProbes[i].intensity * wrapper.Fade;
                        if (wrapper.Override) {
                            intensity *= wrapper.Override.IntenityMultiplier;
                        }
                        graphCommands.SetGlobalVector("_ssgi_refprobe_params_" + i, new Vector4(intensity, activeProbes[i].textureHDRDecodeValues.y, activeProbes[i].textureHDRDecodeValues.w, 0f));
                    }

                    //Pass composition-settings
                    graphCommands.SetGlobalFloat("_ssgi_fallback_direct_intensity", ssgiComp.FallbackDirectIntensity.value);
                    graphCommands.SetGlobalFloat("_ssgi_fallback_direct_saturation", ssgiComp.FallbackDirectSaturation.value);
                    graphCommands.SetGlobalFloat("_ssgi_fallback_direct_power", ssgiComp.FallbackDirectPower.value);

                    if (settings.Quality.ApplyIndirectReflectionProbes) {
                        graphCommands.SetGlobalFloat("_ssgi_fallback_indirect_intensity", ssgiComp.FallbackDirectIntensity.value * settings.Fallback.FallbackIndirectIntensityMultiplier);
                        graphCommands.SetGlobalFloat("_ssgi_fallback_indirect_saturation", ssgiComp.FallbackDirectSaturation.value * settings.Fallback.FallbackIndirectSaturationMultiplier);
                        graphCommands.SetGlobalFloat("_ssgi_fallback_indirect_power", ssgiComp.FallbackDirectPower.value * settings.Fallback.FallbackIndirectPowerMultiplier);
                    }
                }
            }
        }

        private Vector4 GetRefProbeRaymarchParams(Camera camera, Vector3 center) {
            Vector3 screenPoint = camera.WorldToViewportPoint(center);

            Vector2 distToCenter = new Vector2(screenPoint.x - 0.5f, screenPoint.y - 0.5f);
            Vector2 absDistToCenter = new Vector2(Mathf.Abs(distToCenter.x), Mathf.Abs(distToCenter.y));
            if (absDistToCenter.x > 0.5f) {
                absDistToCenter.y /= screenPoint.x * 2f;
                absDistToCenter.x = 0.5f;
            }
            if (absDistToCenter.y > 0.5f) {
                absDistToCenter.x /= screenPoint.y * 2f;
                absDistToCenter.y = 0.5f;
            }
            //screenPoint = new Vector3((absDistToCenter.x + 0.5f) * Mathf.Sign(distToCenter.x), (absDistToCenter.y + 0.5f) * Mathf.Sign(distToCenter.y), screenPoint.z);
            screenPoint.x = Mathf.Clamp01(screenPoint.x);
            screenPoint.y = Mathf.Clamp01(screenPoint.y);

            if(screenPoint.z < 0f) {
                screenPoint.x = -screenPoint.x;
                screenPoint.y = 1f - screenPoint.y;
            } else {
                screenPoint.y = -screenPoint.y;
            }

            return screenPoint;
        }

        private float GetProbeSortingValue(ReflectionProbe probe, Vector3 center, Vector3 forward) {
            float distance = Vector3.Distance(probe.bounds.center, center);
            
            //Favour bounds enveloping the camera, salted by importance and distance
            if (probe.bounds.Contains(center)) {
                return 1000000f + (probe.importance * 1000) -distance;
            }

            //Favour bounds inside expanded proximity 
            float proximitySqr = settings.Fallback.ExpandCenterProximityFactor * settings.SSGIRangeMax;
            proximitySqr *= proximitySqr;
            if ((probe.bounds.ClosestPoint(center) - center).sqrMagnitude < proximitySqr) {
                return 10000f + (probe.importance * 10) - distance;
            }

            //Probes outside of bounds, but still biased by distance
            return -distance;
        }


        private float GetTime() {
#if UNITY_EDITOR
            if (Application.isPlaying) {
                return Time.realtimeSinceStartup;
            } else {
                return (float)UnityEditor.EditorApplication.timeSinceStartup;
            }
#else
            return Time.realtimeSinceStartup;
#endif
        }

        private float GetDeltaTime() {
#if UNITY_EDITOR
            float time = GetTime();
            float deltaTime = lastEditorUpdateTimestamp == -1f ? 0f : time - lastEditorUpdateTimestamp;
            lastEditorUpdateTimestamp = time;
            return deltaTime;
#else
            return Time.deltaTime;
#endif
        }

        private void HandleSceneUnloaded(Scene scene) {
            reflectionProbeWrappers.Clear();
            RequestedObjects.Clear();
            allProbes = null;
            lastEditorUpdateTimestamp = -1f;

            //Dispose all buffered RT's
            foreach(RTWrapper wrapper in rtWrappers) {
                wrapper.Dispose();
            }
            rtWrappers.Clear();
        }


        private RTWrapper FetchBufferedRTwrapper(SSGIPassType type, Camera camera) {
            RTWrapper wrapper = rtWrappers.Find(item => item.Cam == camera && item.Type == type);
            if (wrapper == null) {
                wrapper = new RTWrapper() {
                    Cam = camera,
                    Type = type
                };
                rtWrappers.Add(wrapper);
            }

            return wrapper;
        }

        private RenderTexture GenBufferedRT(SSGIPassType type, Camera camera, RenderTextureDescriptor descriptor, SSGIGraphCommands cmd, FilterMode filterMode) {
            //Fetch wrapper per camera
            RTWrapper wrapper = FetchBufferedRTwrapper(type, camera);

            //Tuple swap RT's
            (wrapper.RT1, wrapper.RT2) = (wrapper.RT2, wrapper.RT1);

            //Setup RT;
            if (!wrapper.RT1 || wrapper.RT1.width != descriptor.width || wrapper.RT1.height != descriptor.height) {
                if (wrapper.RT1) {
                    SSGIGraphCommands.Forget(wrapper.RT1); GameObject.DestroyImmediate(wrapper.RT1);
                }

                wrapper.RT1 = new RenderTexture(descriptor);
                string prefix = wrapper.RT2 ? "B" : "A";
                wrapper.RT1.name = $"{type.ToString()} [{prefix}]";
                wrapper.RT1.Create();
            }

            //Set nameID
            wrapper.RT1.filterMode = filterMode;
            return wrapper.RT1;
        }




        //-------------------- DEBUG --------------------
        private void CaptureDebugRTSSGI(SSGIGraphCommands context, GraphFrame renderingData, bool doEncodeLightDir) {
#if UNITY_EDITOR
            if (debugRenderTextures != null && debugRenderTextures.Count > 0) {
                int colorMode = doEncodeLightDir ? 2 : 0;
                SSGIGraphCommands cmd = graphCommands;
                foreach (KeyValuePair<RenderTexture, DebugRTData> pair in debugRenderTextures) {
                    if (pair.Value.WasHandled) { continue; }
                    if (pair.Value.Camera == renderingData.cameraData.camera) {

                        RenderTexture rt = FetchBufferedRTwrapper(SSGIPassType.SSGIColor, pair.Value.Camera).RT1;
                        if (rt) {
                            if (pair.Value.Type == SSGIPassType.SSGIColor) {
                                cmd.Blit(rt, pair.Key, SetupDebugMaterial(cmd, colorMode, 10f)); pair.Value.WasHandled = true;
                            } else if (pair.Value.Type == SSGIPassType.SSGIShadow) {
                                cmd.Blit(rt, pair.Key, SetupDebugMaterial(cmd, 1, 2f)); pair.Value.WasHandled = true;
                            } else if (pair.Value.Type == SSGIPassType.SSGILightDir) {
                                cmd.Blit(rt, pair.Key, SetupDebugMaterial(cmd, 3, 2f)); pair.Value.WasHandled = true;
                            }
                        }
                    }
                }

                context.ExecuteCommandBuffer(cmd);
                
            }
#endif
        }

        private void CaptureDebugRTOthers(SSGIGraphCommands context, GraphFrame renderingData, bool doEncodeLightDir) {
#if UNITY_EDITOR
            if (debugRenderTextures != null && debugRenderTextures.Count > 0) {
                SSGIGraphCommands cmd = graphCommands;
                int colorMode = doEncodeLightDir ? 2 : 0;

                foreach (KeyValuePair<RenderTexture, DebugRTData> pair in debugRenderTextures) {
                    if (pair.Value.Camera == renderingData.cameraData.camera) {

                        float fp = settings.SSGIRangeMax;//renderingData.cameraData.camera.farClipPlane;
                        if (pair.Value.WasHandled) { continue; }
                        RenderTexture worldPos = FetchBufferedRTwrapper(SSGIPassType.WorldPositions, pair.Value.Camera).RT1;
                        switch (pair.Value.Type) {
                            case SSGIPassType.ThicknessMaskPrePass:      cmd.Blit("_MF_SSGI_ThicknessMask_Prepass",     pair.Key, SetupDebugMaterial(cmd, 0, 10f));                 pair.Value.WasHandled = true; break;
                            case SSGIPassType.ThicknessMask:             cmd.Blit("_MF_SSGI_ThicknessMask",             pair.Key, SetupDebugMaterial(cmd, 0, 10f));                 pair.Value.WasHandled = true; break;
                            case SSGIPassType.SSGIObjects:               cmd.Blit("_MF_SSGI_SSGIObjects",               pair.Key, SetupDebugMaterial(cmd, 0, 10f, 1f, 1f, 0f));     pair.Value.WasHandled = true; break;
                            case SSGIPassType.ScreenCapture:             cmd.Blit("_MF_SSGI_ScreenCapture",             pair.Key, SetupDebugMaterial(cmd, 0, 10f));                 pair.Value.WasHandled = true; break;
                            case SSGIPassType.LightCapture:              cmd.Blit("_MF_SSGI_LightCapture",              pair.Key, SetupDebugMaterial(cmd, 0, 10f));                 pair.Value.WasHandled = true; break;
                            case SSGIPassType.WorldPositions:            cmd.Blit(worldPos,                             pair.Key, SetupDebugMaterial(cmd, 0, 1f));                  pair.Value.WasHandled = true; break;
                            case SSGIPassType.WorldPosDepth:             cmd.Blit(worldPos,                             pair.Key, SetupDebugMaterial(cmd, 1, fp));                  pair.Value.WasHandled = true; break;
                            case SSGIPassType.Normals:                   cmd.Blit("_MF_SSGI_Normals_HQ",                pair.Key, SetupDebugMaterial(cmd, 0, 1f));                  pair.Value.WasHandled = true; break;
                            case SSGIPassType.NormalsDepth:              cmd.Blit("_MF_SSGI_Normals_HQ",                pair.Key, SetupDebugMaterial(cmd, 1, fp));                  pair.Value.WasHandled = true; break;
                            case SSGIPassType.PreDenoised1Color:         cmd.Blit("_MF_SSGI_Pre_Denoised_1",            pair.Key, SetupDebugMaterial(cmd, colorMode, 1f));          pair.Value.WasHandled = true; break;
                            case SSGIPassType.PreDenoised1Shadow:        cmd.Blit("_MF_SSGI_Pre_Denoised_1",            pair.Key, SetupDebugMaterial(cmd, 1, 1f));                  pair.Value.WasHandled = true; break;
                            case SSGIPassType.PreDenoised1LightDir:      cmd.Blit("_MF_SSGI_Pre_Denoised_1",            pair.Key, SetupDebugMaterial(cmd, 3, 1f));                  pair.Value.WasHandled = true; break;
                            case SSGIPassType.PreDenoised2Color:         cmd.Blit("_MF_SSGI_Pre_Denoised_2",            pair.Key, SetupDebugMaterial(cmd, colorMode, 1f));          pair.Value.WasHandled = true; break;
                            case SSGIPassType.PreDenoised2Shadow:        cmd.Blit("_MF_SSGI_Pre_Denoised_2",            pair.Key, SetupDebugMaterial(cmd, 1, 1f));                  pair.Value.WasHandled = true; break;
                            case SSGIPassType.PreDenoised2LightDir:      cmd.Blit("_MF_SSGI_Pre_Denoised_2",            pair.Key, SetupDebugMaterial(cmd, 3, 1f));                  pair.Value.WasHandled = true; break;
                            case SSGIPassType.FinalDenoisedColor:        cmd.Blit("_MF_SSGI_Denoised_Final",            pair.Key, SetupDebugMaterial(cmd, colorMode, 1f));          pair.Value.WasHandled = true; break;
                            case SSGIPassType.FinalDenoisedShadow:       cmd.Blit("_MF_SSGI_Denoised_Final",            pair.Key, SetupDebugMaterial(cmd, 1, 1f));                  pair.Value.WasHandled = true; break;
                            case SSGIPassType.FinalDenoisedLightDir:     cmd.Blit("_MF_SSGI_Denoised_Final",            pair.Key, SetupDebugMaterial(cmd, 3, 1f));                  pair.Value.WasHandled = true; break;
                        }
                    }
                }

                context.ExecuteCommandBuffer(cmd);
                
            }
#endif
        }

        private Material SetupDebugMaterial(SSGIGraphCommands cmd, int mode, float range, float maskR = 1f, float maskG = 1f, float maskB = 1f) {
            if (!debugBlitMaterial) {
                debugBlitMaterial = new Material(Shader.Find("MF_SSGI/DebugBlit"));
            }

            cmd.SetGlobalVector("_debug_color_mask", new Vector4(maskR, maskG, maskB));
            cmd.SetGlobalInt("_debug_mode", mode);
            cmd.SetGlobalFloat("_debug_range", range);
            return debugBlitMaterial;
        }


        public void Shuffle<T>(T[] array) {
            int n = array.Length;
            while (n > 1) {
                n--;
                int k = UnityEngine.Random.Range(0, n + 1);
                T value = array[k];
                array[k] = array[n];
                array[n] = value;
            }
        }
    }
}
