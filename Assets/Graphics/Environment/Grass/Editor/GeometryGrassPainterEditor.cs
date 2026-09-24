using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(GeometryGrassPainter))]
public sealed class GeometryGrassPainterEditor : Editor
{
    private enum GrassTool { Paint, Remove, Edit }

    public override VisualElement CreateInspectorGUI()
    {
        serializedObject.Update();
        var root = new VisualElement();
        root.Add(new HelpBox("Select a tool, then hold Shift and drag the left mouse button in the Scene view. The colored outline marks the brush radius.", HelpBoxMessageType.Info));

        var toolProperty = serializedObject.FindProperty("toolbarInt");
        var toolField = new EnumField("Tool", (GrassTool)Mathf.Clamp(toolProperty.intValue, 0, 2));
        toolField.RegisterValueChangedCallback(evt =>
        {
            serializedObject.Update();
            toolProperty.intValue = (int)(GrassTool)evt.newValue;
            serializedObject.ApplyModifiedProperties();
            SceneView.RepaintAll();
        });
        root.Add(toolField);
        root.Add(Field("showBrushPreview", "Show Brush Preview"));
        root.Add(Field("brushSize", "Brush Radius"));
        root.Add(Field("density", "Samples Per Brush Stamp"));

        var paint = new Foldout { text = "Paint Settings", value = true };
        paint.Add(Field("AdjustedColor", "Grass Color"));
        paint.Add(Field("rangeR", "Red Variation"));
        paint.Add(Field("rangeG", "Green Variation"));
        paint.Add(Field("rangeB", "Blue Variation"));
        paint.Add(Field("sizeWidth", "Blade Width Scale"));
        paint.Add(Field("sizeLength", "Blade Height Scale"));
        root.Add(paint);

        var surface = new Foldout { text = "Surface Filters", value = false };
        surface.Add(Field("hitMask", "Raycast Layers"));
        surface.Add(Field("paintMask", "Paint Layers"));
        surface.Add(Field("normalLimit", "Surface Slope Limit"));
        root.Add(surface);

        var advanced = new Foldout { text = "Advanced", value = false };
        advanced.Add(Field("grassLimit", "Maximum Grass Points"));
        advanced.Add(Field("lightingCellSize", "Lighting Cell Size"));
        root.Add(advanced);

        var count = new Label();
        root.Add(count);
        count.schedule.Execute(() =>
        {
            if (target == null) return;
            var data = new SerializedObject(target);
            count.text = "Painted Points: " + data.FindProperty("positions").arraySize;
        }).Every(500);
        return root;
    }

    private PropertyField Field(string propertyName, string label)
    {
        var field = new PropertyField(serializedObject.FindProperty(propertyName), label);
        field.Bind(serializedObject);
        return field;
    }
}
