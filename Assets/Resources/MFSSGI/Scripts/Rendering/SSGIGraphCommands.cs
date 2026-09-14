using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace MF.SSGI {
    // Records the existing multipass algorithm into one graph-owned unsafe pass.
    // Texture handles are resolved only during graph execution, never cached across frames.
    internal sealed class SSGIGraphCommands {
        readonly RenderGraph graph;
        readonly TextureDesc baseDescriptor;
        readonly Dictionary<int,TextureHandle> named = new();
        readonly Dictionary<int,TextureHandle> depthTargets = new();
        readonly List<Action<CommandBuffer>> operations = new();
        internal readonly Dictionary<TextureHandle,AccessFlags> textures = new();
        internal readonly List<RendererListHandle> lists = new();
        internal readonly HashSet<TextureHandle> created = new();
        static readonly Dictionary<RenderTexture,RTHandle> imports = new();
        readonly Dictionary<RenderTexture,TextureHandle> frameImports = new();
        public SSGIGraphCommands(RenderGraph g,TextureHandle color) { graph=g; baseDescriptor=color.GetDescriptor(g); }
        public static void Forget(RenderTexture rt) { if(ReferenceEquals(rt,null)) return; if(imports.Remove(rt,out var h)) h.Release(); }
        public static void ReleaseImports() { foreach(var h in imports.Values) h.Release(); imports.Clear(); }
        void Use(TextureHandle h,AccessFlags access) { if(!h.IsValid()) throw new InvalidOperationException("MF.SSGI received an invalid graph texture"); textures[h]=textures.TryGetValue(h,out var a)?a|access:access; }
        Func<RenderTargetIdentifier> Target(object value,AccessFlags access) {
            if(value==null) value=Texture2D.blackTexture;
            if(value is string s) value=Shader.PropertyToID(s);
            if(value is int id) { if(!named.TryGetValue(id,out var n)) throw new InvalidOperationException("MF.SSGI undeclared texture id: "+id); value=n; }
            if(value is RenderTexture rt) {
                if(!frameImports.TryGetValue(rt,out var h)) {
                    if(!imports.TryGetValue(rt,out var external)) {external=RTHandles.Alloc(rt);imports.Add(rt,external);}
                    h=graph.ImportTexture(external); frameImports.Add(rt,h);
                }
                value=h;
            }
            if(value is TextureHandle handle) { Use(handle,access); return ()=>((RTHandle)handle).nameID; }
            if(value is Texture texture) return ()=>new RenderTargetIdentifier(texture);
            throw new InvalidOperationException("MF.SSGI unsupported target "+value.GetType());
        }
        public void Bind(string name,TextureHandle handle) {named[Shader.PropertyToID(name)]=handle;SetGlobalTexture(name,handle);}
        public void GetTemporaryRT(int id,RenderTextureDescriptor d,FilterMode filter) {
            var desc=new TextureDesc(baseDescriptor) {name="MF.SSGI "+id,width=d.width,height=d.height,depthBufferBits=DepthBits.None,colorFormat=d.graphicsFormat,msaaSamples=(MSAASamples)d.msaaSamples,filterMode=filter,useMipMap=d.useMipMap,autoGenerateMips=d.autoGenerateMips,clearBuffer=false};
            var color=graph.CreateTexture(desc); named[id]=color;created.Add(color);
            if(d.depthBufferBits>0) {
                var depthDesc=new TextureDesc(baseDescriptor) {name="MF.SSGI depth "+id,width=d.width,height=d.height,colorFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.None,depthBufferBits=DepthBits.Depth16,msaaSamples=(MSAASamples)d.msaaSamples,clearBuffer=false};
                depthTargets[id]=graph.CreateTexture(depthDesc);created.Add(depthTargets[id]);
            }
            SetGlobalTexture(id,color);
        }
        public void ReleaseTemporaryRT(int id) { /* Render Graph owns transient lifetime. */ }
        public void SetGlobalTexture(string name,object texture)=>SetGlobalTexture(Shader.PropertyToID(name),texture);
        public void SetGlobalTexture(int id,object texture) { var t=Target(texture,AccessFlags.Read);operations.Add(c=>c.SetGlobalTexture(id,t())); }
        public void SetGlobalFloat(string n,float v)=>operations.Add(c=>c.SetGlobalFloat(n,v));
        public void SetGlobalInt(string n,int v)=>operations.Add(c=>c.SetGlobalInt(n,v));
        public void SetGlobalColor(string n,Color v)=>operations.Add(c=>c.SetGlobalColor(n,v));
        public void SetGlobalVector(string n,Vector4 v)=>operations.Add(c=>c.SetGlobalVector(n,v));
        public void EnableKeyword(string n)=>operations.Add(c=>c.EnableShaderKeyword(n));
        public void DisableKeyword(string n)=>operations.Add(c=>c.DisableShaderKeyword(n));
        public void SetProjectionMatrix(Matrix4x4 m)=>operations.Add(c=>c.SetProjectionMatrix(m));
        public void SetRenderTarget(object color) {
            if(color is int id && depthTargets.TryGetValue(id,out var depth)) {SetRenderTarget(color,depth);return;}
            var a=Target(color,AccessFlags.Write);operations.Add(c=>c.SetRenderTarget(a()));
        }
        public void SetRenderTarget(object color,object depth) {var a=Target(color,AccessFlags.Write);var b=Target(depth,AccessFlags.Write);operations.Add(c=>c.SetRenderTarget(a(),b()));}
        public void ClearRenderTarget(bool depth,bool color,Color value)=>operations.Add(c=>c.ClearRenderTarget(depth,color,value));
        public void DrawRenderer(Renderer r,Material m,int sub,int pass)=>operations.Add(c=>c.DrawRenderer(r,m,sub,pass));
        public void Blit(object source,object destination,Material material=null,int pass=-1) {
            var a=Target(source,AccessFlags.Read);var b=Target(destination,AccessFlags.Write);
            operations.Add(c=> {if(material==null)c.Blit(a(),b());else c.Blit(a(),b(),material,pass);});
        }
        public void DrawRenderers(CullingResults cull,ref DrawingSettings drawing,ref FilteringSettings filtering) {
            var handle=graph.CreateRendererList(new RendererListParams(cull,drawing,filtering));lists.Add(handle);operations.Add(c=>c.DrawRendererList(handle));
        }
        public void Execute(CommandBuffer cmd) {foreach(var op in operations)op(cmd);}
        public void ExecuteCommandBuffer(SSGIGraphCommands unused) { /* Commands already recorded in order. */ }
    }
}
