using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Layout
{
    /// <summary>
    /// Several properties sharing one line, as a Vector4 field's X, Y, Z and W do: each is still a property of its own
    /// (so prefab overrides, undo and editing several objects at once work on each), drawn with a label only as wide as
    /// its text that still drags the value.
    /// </summary>
    internal static class CompactFields
    {
        /// <summary>The space between one field and the next on a line.</summary>
        public const float Gap = 4f;

        /// <summary>How wide <paramref name="label"/> is drawn as a field's label.</summary>
        public static float LabelWidth(GUIContent label) => Mathf.Ceil(EditorStyles.label.CalcSize(label).x);

        /// <summary>
        /// <paramref name="property"/> in <paramref name="rect"/>: its <paramref name="label"/>,
        /// <paramref name="labelWidth"/> wide, at the rect's left edge (not indented), and its field in the rest.
        /// </summary>
        public static void Field(Rect rect, SerializedProperty property, GUIContent label, float labelWidth)
        {
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            int indent = EditorGUI.indentLevel;
            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUI.indentLevel = 0;
            EditorGUI.PropertyField(rect, property, label);
            EditorGUI.indentLevel = indent;
            EditorGUIUtility.labelWidth = oldLabelWidth;
        }
    }
}
