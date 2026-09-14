#if URP
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace StylizedWater2 {
[InitializeOnLoad]
public static class RavenWaterGraphSetup {
 static RavenWaterGraphSetup(){EditorApplication.delayCall+=ApplyRequested;}
 static void ApplyRequested(){if(!File.Exists("Temp/WaterGraph/request")||EditorApplication.isPlayingOrWillChangePlaymode)return;Apply();File.Delete("Temp/WaterGraph/request");}
 [MenuItem("Tools/Water/Enable Render Graph Caustics and SSR")]
 public static void Apply(){
  Directory.CreateDirectory("Temp/WaterGraph");
  foreach(var path in new[]{"Assets/Settings/ForwardRenderer.asset","Assets/Settings/SSGI/SSGIRenderer.asset","Assets/Settings/SSGITest/SSGIRenderer.asset"}){
   var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);if(!renderer)continue;
   var feature=renderer.rendererFeatures.OfType<StylizedWaterRenderFeature>().FirstOrDefault();
   if(!feature){feature=ScriptableObject.CreateInstance<StylizedWaterRenderFeature>();feature.name="Stylized Water 2";AssetDatabase.AddObjectToAsset(feature,renderer);renderer.rendererFeatures.Add(feature);}
   feature.directionalCaustics=true;feature.screenSpaceReflectionSettings.enable=true;feature.Create();feature.SetActive(true);
   EditorUtility.SetDirty(feature);EditorUtility.SetDirty(renderer);
  }AssetDatabase.SaveAssets();File.WriteAllText("Temp/WaterGraph/setup.txt","Directional Caustics and SSR enabled on existing default and SSGI renderers. Compatibility unchanged.");
 }
}
}
#endif
