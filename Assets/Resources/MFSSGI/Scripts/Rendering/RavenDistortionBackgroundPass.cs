using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace MF.SSGI {
    internal sealed class RavenDistortionBackgroundPass : ScriptableRenderPass {
        static readonly int TextureId=Shader.PropertyToID("_RavenAfterSSGITexture");
        class Data {public TextureHandle source,destination;public Vector4 texels;}
        public void Setup(ScriptableRenderer unused,RenderPassEvent injection) {renderPassEvent=injection;ConfigureInput(ScriptableRenderPassInput.Color);}
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData) {
            var resources=frameData.Get<UniversalResourceData>();
            if(resources.isActiveTargetBackBuffer)return;
            var desc=resources.activeColorTexture.GetDescriptor(graph);
            desc.name="Raven Distortion Background After SSGI";desc.depthBufferBits=DepthBits.None;desc.msaaSamples=MSAASamples.None;desc.clearBuffer=false;
            var destination=graph.CreateTexture(desc);
            using(var builder=graph.AddUnsafePass<Data>("Raven Distortion Background",out var data)) {
                data.source=resources.activeColorTexture;data.destination=destination;data.texels=new Vector4(1f/desc.width,1f/desc.height,desc.width,desc.height);
                builder.UseTexture(data.source,AccessFlags.Read);builder.UseTexture(destination,AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(destination,TextureId);
                builder.AllowGlobalStateModification(true);builder.AllowPassCulling(false);
                builder.SetRenderFunc(static(Data d,UnsafeGraphContext c)=>{
                    var cmd=CommandBufferHelpers.GetNativeCommandBuffer(c.cmd);
                    cmd.Blit(((RTHandle)d.source).nameID,((RTHandle)d.destination).nameID);
                    cmd.SetGlobalVector("_RavenAfterSSGITexture_TexelSize",d.texels);
                    cmd.SetGlobalFloat("_RavenAfterSSGIReady",1);
                });
            }
        }
    }
    internal sealed class RavenDistortionResetPass : ScriptableRenderPass {
        class Data {}
        public RavenDistortionResetPass(){renderPassEvent=RenderPassEvent.AfterRenderingTransparents;}
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData) {
            using(var builder=graph.AddUnsafePass<Data>("Raven Distortion Reset",out var data)) {
                builder.AllowGlobalStateModification(true);builder.AllowPassCulling(false);
                builder.SetRenderFunc(static(Data d,UnsafeGraphContext c)=>{c.cmd.SetGlobalFloat("_RavenAfterSSGIReady",0); c.cmd.SetGlobalFloat("_RavenSSGICompositionReady",0);});
            }
        }
    }
}
