using TimboJimbo.UI.Layout;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Layout
{
    /// <summary>Insets on one line: left, right, top and bottom, each with a one-letter label that drags it.</summary>
    [CustomPropertyDrawer(typeof(Insets))]
    public sealed class InsetsDrawer : PropertyDrawer
    {
        private static readonly string[] s_sides =
        {
            nameof(Insets.Left), nameof(Insets.Right), nameof(Insets.Top), nameof(Insets.Bottom),
        };
        private static readonly GUIContent[] s_sideLabels =
        {
            new("L", "Left"), new("R", "Right"), new("T", "Top"), new("B", "Bottom"),
        };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            position.height = EditorGUIUtility.singleLineHeight;
            label = EditorGUI.BeginProperty(position, label, property);
            var field = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            // Every label is as wide as the widest, so the four inputs line up the same width too.
            float labelWidth = 0f;
            foreach (var sideLabel in s_sideLabels)
                labelWidth = Mathf.Max(labelWidth, CompactFields.LabelWidth(sideLabel));
            float width = Mathf.Max(0f, (field.width - (s_sides.Length - 1) * CompactFields.Gap) / s_sides.Length);

            for (int i = 0; i < s_sides.Length; i++)
            {
                var rect = new Rect(field.x + i * (width + CompactFields.Gap), field.y, width, field.height);
                CompactFields.Field(rect, property.FindPropertyRelative(s_sides[i]), s_sideLabels[i], labelWidth);
            }

            EditorGUI.EndProperty();
        }
    }
}
