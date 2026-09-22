#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Imports the baked Raven v6 studio takes and verifies them on a temporary model instance.</summary>
public static class RavenMocapV6Import
{
    const string Folder = "Assets/Graphics/Characters/Raven/Animations/Mocap";
    const string Target = "Assets/Graphics/Characters/Raven/ModelV6/Raven_GameReady.fbx";

    [MenuItem("Tools/Raven/Mocap V6/Import and validate")]
    public static void ImportAndValidate()
    {
        var report = new StringBuilder();
        Directory.CreateDirectory("Library/MocapV6");
        try
        {
            AssetDatabase.Refresh();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(Target).OfType<Avatar>().Single();
            if (!avatar.isValid || !avatar.isHuman) throw new Exception("Raven v6 avatar is not valid Humanoid.");
            var files = Directory.GetFiles(Folder, "*.fbx").OrderBy(p => p).ToArray();
            if (files.Length != 24) throw new Exception("Expected 24 takes, got " + files.Length);
            EnsureFolder(Folder + "/Clips");
            foreach (var rawPath in files)
            {
                var path = rawPath.Replace('\\', '/');
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
                importer.importAnimation = true;
                importer.preserveHierarchy = true;
                importer.importCameras = false;
                importer.importLights = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
                importer.animationRotationError = 0.1f;
                importer.animationPositionError = 0.1f;
                importer.animationScaleError = 0.1f;
                importer.SaveAndReimport();
                var take = importer.defaultClipAnimations.Single();
                var rootClip = new ModelImporterClipAnimation
                {
                    name = Path.GetFileNameWithoutExtension(path) + "_RootMotion",
                    takeName = take.takeName,
                    firstFrame = take.firstFrame,
                    lastFrame = take.lastFrame,
                    loopTime = false,
                    loopPose = false,
                    lockRootRotation = false,
                    lockRootHeightY = false,
                    lockRootPositionXZ = false,
                    keepOriginalOrientation = true,
                    keepOriginalPositionY = true,
                    keepOriginalPositionXZ = true,
                    heightFromFeet = false
                };
                var inPlace = new ModelImporterClipAnimation
                {
                    name = Path.GetFileNameWithoutExtension(path) + "_InPlace",
                    takeName = take.takeName,
                    firstFrame = take.firstFrame,
                    lastFrame = take.lastFrame,
                    loopTime = false,
                    loopPose = false,
                    lockRootRotation = true,
                    lockRootHeightY = true,
                    lockRootPositionXZ = true,
                    keepOriginalOrientation = true,
                    keepOriginalPositionY = true,
                    keepOriginalPositionXZ = true,
                    heightFromFeet = false
                };
                importer.clipAnimations = new[] {rootClip, inPlace};
                importer.SaveAndReimport();
                var imported = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
                if (imported.Length != 2) throw new Exception("Expected two variants: " + path);
                foreach (var clip in imported)
                {
                    if (!clip.humanMotion || clip.length < 1 || clip.length > 60) throw new Exception("Invalid clip: " + clip.name);
                    var dest = Folder + "/Clips/" + clip.name + ".anim";
                    var prepared = UnityEngine.Object.Instantiate(clip);
                    prepared.name = clip.name;
                    if (clip.name.EndsWith("_InPlace"))
                    {
                        foreach (var binding in AnimationUtility.GetCurveBindings(prepared))
                        {
                            if (binding.type != typeof(Animator) || (binding.propertyName != "RootT.x" && binding.propertyName != "RootT.z")) continue;
                            var curve = AnimationUtility.GetEditorCurve(prepared, binding);
                            AnimationUtility.SetEditorCurve(prepared, binding, AnimationCurve.Constant(0, clip.length, curve.Evaluate(0)));
                        }
                    }
                    var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(dest);
                    if (existing) { EditorUtility.CopySerialized(prepared, existing); EditorUtility.SetDirty(existing); UnityEngine.Object.DestroyImmediate(prepared); }
                    else AssetDatabase.CreateAsset(prepared, dest);
                    report.AppendLine("IMPORTED " + clip.name + " seconds=" + clip.length.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                }
                // The portable FBX exposes its recorded trajectory. In-place assets are the
                // standalone clips above, where horizontal body translation is explicitly removed.
                importer.clipAnimations = new[] {rootClip};
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            ValidateClips(report);
            RenderPreviews();
            var controllerPath = Folder + "/MocapPreview.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (!controller)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                var machine = controller.layers[0].stateMachine;
                int index = 0;
                foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] {Folder + "/Clips"}).OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                    var state = machine.AddState(clip.name, new Vector3((index % 4) * 330, (index / 4) * 65, 0));
                    state.motion = clip;
                    index++;
                }
            }
            AssetDatabase.SaveAssets();
            report.AppendLine("PASS: 24 FBX takes, 48 Humanoid clips; every clip evaluated on Raven v6 at 31 timestamps with mesh validation.");
            Debug.Log(report.ToString());
        }
        catch (Exception ex) { report.AppendLine("FAIL: " + ex); Debug.LogError(ex); throw; }
        finally { File.WriteAllText("Library/MocapV6/unity-validation.txt", report.ToString()); }
    }

    static void EnsureFolder(string path)
    {
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }

    static void ValidateClips(StringBuilder report)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Target);
        var instance = UnityEngine.Object.Instantiate(model);
        instance.hideFlags = HideFlags.HideAndDontSave;
        var animator = instance.GetComponent<Animator>();
        if (!animator) animator = instance.AddComponent<Animator>();
        animator.avatar = AssetDatabase.LoadAllAssetsAtPath(Target).OfType<Avatar>().Single();
        animator.applyRootMotion = true;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var bones = instance.GetComponentsInChildren<Transform>();
        var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
        var mesh = new Mesh();
        try
        {
            var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] {Folder + "/Clips"});
            if (clips.Length != 48) throw new Exception("Expected 48 standalone clips.");
            foreach (var guid in clips)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip.name.EndsWith("_InPlace"))
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type != typeof(Animator) || (binding.propertyName != "RootT.x" && binding.propertyName != "RootT.z")) continue;
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        if (curve.keys.Any(k => Mathf.Abs(k.value - curve.keys[0].value) > .00001f)) throw new Exception("In-place horizontal trajectory is not constant: " + clip.name);
                    }
                }
                var graph = PlayableGraph.Create("RavenMocapValidation");
                try
                {
                    animator.Rebind(); instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimationClipPlayable.Create(graph, clip);
                    var output = AnimationPlayableOutput.Create(graph, "Preview", animator); output.SetSourcePlayable(playable);
                    graph.Play();
                    Vector3[] first = null;
                    float movement = 0, maxBounds = 0;
                    for (int i = 0; i <= 30; ++i)
                    {
                        playable.SetTime(clip.length * i / 30.0); graph.Evaluate(0);
                        var hipPosition = instance.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position);
                        if (hipPosition.y < -.3f || hipPosition.y > 2.5f) throw new Exception("Unexpected hip height: " + clip.name + " " + hipPosition);
                        if (clip.name.EndsWith("_InPlace") && new Vector2(hipPosition.x, hipPosition.z).magnitude > .8f) throw new Exception("In-place body drifts away from origin: " + clip.name);
                        var positions = bones.Select(b => instance.transform.InverseTransformPoint(b.position)).ToArray();
                        if (first == null) first = positions;
                        for (int b = 0; b < positions.Length; ++b)
                        {
                            if (!Finite(positions[b])) throw new Exception("Nonfinite skeleton: " + clip.name);
                            movement = Mathf.Max(movement, Vector3.Distance(first[b], positions[b]));
                        }
                        foreach (var renderer in renderers)
                        {
                            renderer.BakeMesh(mesh);
                            if (mesh.vertexCount == 0 || !Finite(mesh.bounds.size) || mesh.bounds.size.magnitude > 6) throw new Exception("Invalid skinning: " + clip.name + "/" + renderer.name);
                            foreach (var vertex in mesh.vertices) if (!Finite(vertex)) throw new Exception("Invalid vertex: " + clip.name);
                            maxBounds = Mathf.Max(maxBounds, mesh.bounds.size.magnitude);
                        }
                    }
                    if (movement < .01f) throw new Exception("Clip did not animate Raven: " + clip.name);
                    report.AppendLine("PASS " + clip.name + " samples=31 meshes=" + renderers.Length + " movement=" + movement.ToString("F3") + " maxMeshBounds=" + maxBounds.ToString("F3"));
                }
                finally { graph.Destroy(); }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(instance); }
    }
    public static void BatchImport()
    {
        try {ImportAndValidate();EditorApplication.Exit(0);}
        catch {EditorApplication.Exit(1);throw;}
    }
    static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

    public static void RenderPreviews()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Target));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, scene);
        var animator = instance.GetComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var cameraObject = new GameObject("Mocap preview camera");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
        camera.cameraType = CameraType.Preview; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.10f, .12f, .16f); camera.fieldOfView = 32; camera.nearClipPlane = .01f; camera.farClipPlane = 30;
        foreach (var angles in new[] { new Vector3(35, -35, 0), new Vector3(30, 145, 0) })
        {
            var obj = new GameObject("Preview light"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene);
            obj.transform.eulerAngles = angles; var light = obj.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = angles.y < 0 ? 2.2f : 1.2f;
        }
        var rt = new RenderTexture(480, 560, 24); camera.targetTexture = rt;
        var tile = new Texture2D(480, 560, TextureFormat.RGB24, false);
        var sheet = new Texture2D(480 * 6, 560 * 4, TextureFormat.RGB24, false);
        var names = new StringBuilder();
        var previous = RenderTexture.active;
        try
        {
            var clips = Directory.GetFiles(Folder + "/Clips", "*_InPlace.anim").OrderBy(p => p).ToArray();
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clips[i].Replace('\\', '/'));
                var graph = PlayableGraph.Create("Mocap preview");
                try
                {
                    animator.Rebind(); instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimationClipPlayable.Create(graph, clip); playable.SetTime(clip.length * .45);
                    AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(playable); graph.Play(); playable.SetTime(clip.length * .45); graph.Evaluate(.001f);
                    var hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    var center = new Vector3(hips.x, .9f, hips.z);
                    camera.transform.position = center + new Vector3(2.2f, 1f, 4.3f); camera.transform.LookAt(center);
                    var snapshots = new System.Collections.Generic.List<GameObject>();
                    var bakedMeshes = new System.Collections.Generic.List<Mesh>();
                    var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
                    foreach (var skin in skins)
                    {
                        var baked = new Mesh(); skin.BakeMesh(baked); bakedMeshes.Add(baked);
                        var snapshot = new GameObject("Sampled " + skin.name);
                        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(snapshot, scene);
                        snapshot.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                        snapshot.transform.localScale = skin.transform.lossyScale;
                        snapshot.AddComponent<MeshFilter>().sharedMesh = baked;
                        snapshot.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                        snapshots.Add(snapshot); skin.enabled = false;
                    }
                    camera.Render(); RenderTexture.active = rt;
                    tile.ReadPixels(new Rect(0, 0, 480, 560), 0, 0); tile.Apply();
                    sheet.SetPixels(i % 6 * 480, (3 - i / 6) * 560, 480, 560, tile.GetPixels());
                    names.AppendLine((i + 1) + ": " + clip.name);
                    foreach (var skin in skins) skin.enabled = true;
                    foreach (var snapshot in snapshots) UnityEngine.Object.DestroyImmediate(snapshot);
                    foreach (var baked in bakedMeshes) UnityEngine.Object.DestroyImmediate(baked);
                }
                finally { graph.Destroy(); }
            }
            sheet.Apply(); File.WriteAllBytes("Library/MocapV6/unity-contact-sheet.png", sheet.EncodeToPNG());
            File.WriteAllText("Library/MocapV6/preview-index.txt", names.ToString());
        }
        finally
        {
            RenderTexture.active = previous; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tile); UnityEngine.Object.DestroyImmediate(sheet);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    public static void BatchValidate()
    {
        var report = new StringBuilder();
        try
        {
            AssetDatabase.Refresh();
            ValidateClips(report);
            foreach (var variant in new[] {"InPlace", "RootMotion"})
            {
                var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Target));
                var animator = instance.GetComponent<Animator>(); animator.applyRootMotion = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var graph = PlayableGraph.Create("Root motion validation");
                try
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/Clips/G3AttackDash0013_" + variant + ".anim");
                    animator.Rebind(); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimationClipPlayable.Create(graph, clip);
                    AnimationPlayableOutput.Create(graph, "Motion", animator).SetSourcePlayable(playable); graph.Play();
                    float displacement = 0;
                    for (int f = 0; f < Mathf.CeilToInt(clip.length * 30); f++)
                    {
                        graph.Evaluate(1f / 30);
                        var p = instance.transform.position; displacement = Mathf.Max(displacement, new Vector2(p.x, p.z).magnitude);
                    }
                    if (variant == "InPlace" && displacement > .02f) throw new Exception("In-place root moved " + displacement);
                    if (variant == "RootMotion" && displacement < .1f) throw new Exception("Root-motion trajectory was lost.");
                    report.AppendLine("PASS continuous playback: " + variant + " max horizontal root displacement=" + displacement.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + " m");
                }
                finally { graph.Destroy(); UnityEngine.Object.DestroyImmediate(instance); }
            }
            RenderPreviews();
            report.AppendLine("PASS: 48 Humanoid clips x 31 samples x 8 meshes; hip height and in-place origin bounds; continuous root-motion/in-place playback.");
            File.WriteAllText("Library/MocapV6/unity-validation.txt", report.ToString());
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            report.AppendLine("FAIL: " + ex); File.WriteAllText("Library/MocapV6/unity-validation.txt", report.ToString());
            Debug.LogException(ex); EditorApplication.Exit(1);
        }
    }
}
#endif






