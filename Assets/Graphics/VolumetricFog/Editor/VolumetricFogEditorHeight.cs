using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace VolumetricFogAndMist2 {
    public partial class VolumetricFogEditor {
        Texture2D strokeTexture;
        Texture2D strokeCoverage;
        int heightStrokeUndoGroup = -1;

        bool IsHeightBrush => fog.maskBrushMode == MASK_TEXTURE_BRUSH_MODE.HeightFog ||
                              fog.maskBrushMode == MASK_TEXTURE_BRUSH_MODE.ResetHeight;
        bool PaintsHeight => IsHeightBrush || (fog.maskBrushMode == MASK_TEXTURE_BRUSH_MODE.AddFog && fog.maskBrushPaintHeight);

        void DrawHeightControls() {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableHeightMap"), new GUIContent("Local Fog Height"));
            if (!serializedObject.FindProperty("enableHeightMap").boolValue) return;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fogHeightMap"), new GUIContent("Height Map"));
            if (fog.fogHeightMap == null) {
                if (GUILayout.Button("Create Height Map")) {
                    serializedObject.ApplyModifiedProperties();
                    CreateHeightMap();
                    serializedObject.Update();
                }
            } else if (!fog.fogHeightMap.isReadable || fog.fogHeightMap.format != TextureFormat.RGBAFloat) {
                EditorGUILayout.HelpBox("Use a readable RGBAFloat height map created with Create Height Map. Color or coverage textures cannot store heights in metres.", MessageType.Error);
            }
            EditorGUILayout.HelpBox("Height Fog paints metres above the terrain (or vertical reach from the volume centre without Terrain Fit). Reset Height restores the profile. Height remains bounded by the fog volume. Coverage and color are unchanged.", MessageType.Info);
        }

        void CreateHeightMap() {
            int size = Mathf.Clamp(fog.fogOfWarTextureSize, 256, 1024);
            var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true) {
                name = fog.name + "HeightMap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            texture.SetPixels(new Color[size * size]);
            texture.Apply(false, false);
            string coveragePath = AssetDatabase.GetAssetPath(fog.fogOfWarTexture);
            string directory = string.IsNullOrEmpty(coveragePath) ? "Assets" : System.IO.Path.GetDirectoryName(coveragePath).Replace('\\', '/');
            string path = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + texture.name + ".asset");
            AssetDatabase.CreateAsset(texture, path);
            Undo.RecordObject(fog, "Assign fog height map");
            fog.fogHeightMap = texture;
            fog.enableHeightMap = true;
            fog.maskBrushHeight = fog.DefaultPaintHeight;
            EditorUtility.SetDirty(fog);
            PrefabUtility.RecordPrefabInstancePropertyModifications(fog);
            EditorSceneManager.MarkSceneDirty(fog.gameObject.scene);
            AssetDatabase.SaveAssetIfDirty(texture);
            fog.UpdateMaterialProperties();
        }

        void BeginHeightStroke() {
            if (!PaintsHeight || fog.fogHeightMap == null || !fog.enableHeightMap) return;
            Undo.IncrementCurrentGroup();
            heightStrokeUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paint fog height");
            strokeTexture = fog.fogHeightMap;
            Undo.RegisterCompleteObjectUndo(strokeTexture, "Paint fog height");
            if (!IsHeightBrush && fog.fogOfWarTexture != null) {
                strokeCoverage = fog.fogOfWarTexture;
                Undo.RegisterCompleteObjectUndo(strokeCoverage, "Paint fog height and coverage");
            }
        }

        void FinishHeightStroke() {
            if (strokeTexture != null) {
                EditorUtility.SetDirty(strokeTexture);
                AssetDatabase.SaveAssetIfDirty(strokeTexture);
            }
            if (heightStrokeUndoGroup >= 0) Undo.CollapseUndoOperations(heightStrokeUndoGroup);
            if (strokeCoverage != null) {
                EditorUtility.SetDirty(strokeCoverage);
                AssetDatabase.SaveAssetIfDirty(strokeCoverage);
            }
            strokeTexture = null;
            strokeCoverage = null;
            heightStrokeUndoGroup = -1;
        }

        void PaintHeightOnMaskPosition(Vector3 position) {
            if (!fog.enableHeightMap || fog.fogHeightMap == null || fog.fogHeightMap.format != TextureFormat.RGBAFloat) return;
            if (fog.PaintFogHeight(position, fog.maskBrushHeight, fog.maskBrushWidth,
                fog.maskBrushOpacity, fog.maskBrushFuzziness, fog.maskBrushMode == MASK_TEXTURE_BRUSH_MODE.ResetHeight)) {
                EditorUtility.SetDirty(fog.fogHeightMap);
                SceneView.RepaintAll();
            }
        }

        void OnHeightUndoRedo() {
            if (fog == null) return;
            if (fog.fogHeightMap != null) {
                fog.fogHeightMap.Apply(false, false);
                EditorUtility.SetDirty(fog.fogHeightMap);
                AssetDatabase.SaveAssetIfDirty(fog.fogHeightMap);
            }
            fog.UpdateMaterialProperties();
            if (fog.enableFogOfWar && fog.fogOfWarTexture != null) fog.ReloadFogOfWarTexture();
            SceneView.RepaintAll();
        }
    }
}
