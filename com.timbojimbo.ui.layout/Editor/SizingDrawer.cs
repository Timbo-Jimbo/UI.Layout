using System;
using TimboJimbo.UI.Layout;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Layout
{
    /// <summary>
    /// A size along one axis on one line: its mode, then its value where the mode has one (fixed and percent), then its
    /// min and max where they bound it (every mode but fixed). While sizes of different modes are edited together,
    /// every field shows.
    /// </summary>
    [CustomPropertyDrawer(typeof(Sizing))]
    public sealed class SizingDrawer : PropertyDrawer
    {
        private static readonly string[] s_modeNames = Enum.GetNames(typeof(SizingMode));

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var mode = property.FindPropertyRelative(nameof(Sizing.Mode));
            var value = property.FindPropertyRelative(nameof(Sizing.Value));
            var min = property.FindPropertyRelative(nameof(Sizing.Min));
            var max = property.FindPropertyRelative(nameof(Sizing.Max));

            // Which fields show is settled before the mode popup is drawn: a new mode lays out on the next event rather
            // than halfway through this one.
            bool mixed = mode.hasMultipleDifferentValues;
            bool hasValue = mixed || mode.intValue == (int)SizingMode.Fixed || mode.intValue == (int)SizingMode.Percent;
            bool bounded = mixed || mode.intValue != (int)SizingMode.Fixed;

            position.height = EditorGUIUtility.singleLineHeight;
            label = EditorGUI.BeginProperty(position, label, property);
            var field = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            // PrefixLabel has already placed the field's rect past the indented label; indenting the controls in it
            // again would push them right.
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            var modeRect = new Rect(field.x, field.y, Mathf.Min(ModeWidth(), field.width), field.height);
            EditorGUI.PropertyField(modeRect, mode, GUIContent.none);

            // The value has no label (the mode beside it says what it is); the min and max have short ones that drag
            // them. Every input gets the same share of what is left of the line.
            var minLabel = new GUIContent("Min", min.tooltip);
            var maxLabel = new GUIContent("Max", max.tooltip);
            float minLabelWidth = bounded ? CompactFields.LabelWidth(minLabel) : 0f;
            float maxLabelWidth = bounded ? CompactFields.LabelWidth(maxLabel) : 0f;
            int inputs = (hasValue ? 1 : 0) + (bounded ? 2 : 0);
            float x = modeRect.xMax + CompactFields.Gap;
            float input = Mathf.Max(0f, (field.xMax - x - (inputs - 1) * CompactFields.Gap - minLabelWidth - maxLabelWidth) / inputs);

            if (hasValue)
            {
                EditorGUI.PropertyField(new Rect(x, field.y, input, field.height), value, GUIContent.none);
                x += input + CompactFields.Gap;
            }
            if (bounded)
            {
                CompactFields.Field(new Rect(x, field.y, minLabelWidth + input, field.height), min, minLabel, minLabelWidth);
                x += minLabelWidth + input + CompactFields.Gap;
                CompactFields.Field(new Rect(x, field.y, maxLabelWidth + input, field.height), max, maxLabel, maxLabelWidth);
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }

        // Wide enough for the longest mode's name beside the popup's arrow.
        private static float ModeWidth()
        {
            float width = 0f;
            foreach (var name in s_modeNames)
                width = Mathf.Max(width, EditorStyles.popup.CalcSize(new GUIContent(name)).x);
            return Mathf.Ceil(width);
        }
    }
}
