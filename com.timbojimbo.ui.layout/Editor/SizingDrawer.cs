using TimboJimbo.UI.Layout;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Layout
{
    /// <summary>Draws a <see cref="Sizing"/> on one line: the mode, then only the fields that mode uses.</summary>
    [CustomPropertyDrawer(typeof(Sizing))]
    public sealed class SizingDrawer : PropertyDrawer
    {
        private const float Spacing = 4f;
        private const float MiniLabelWidth = 28f;

        private static readonly GUIContent s_min = new("Min");
        private static readonly GUIContent s_max = new("Max", "0 is unbounded");

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var mode = property.FindPropertyRelative("Mode");
            var value = property.FindPropertyRelative("Value");
            var min = property.FindPropertyRelative("Min");
            var max = property.FindPropertyRelative("Max");

            EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, label);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            float modeWidth = Mathf.Min(72f, position.width * 0.4f);
            var modeRect = new Rect(position.x, position.y, modeWidth, position.height);
            var rest = new Rect(position.x + modeWidth + Spacing, position.y, Mathf.Max(0f, position.width - modeWidth - Spacing), position.height);
            EditorGUI.PropertyField(modeRect, mode, GUIContent.none);

            switch ((SizingMode)mode.intValue)
            {
                case SizingMode.Fixed:
                    EditorGUI.PropertyField(rest, value, GUIContent.none);
                    break;
                case SizingMode.Percent:
                    EditorGUI.Slider(rest, value, 0f, 1f, GUIContent.none);
                    break;
                default:
                    float half = (rest.width - Spacing) * 0.5f;
                    float labelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = MiniLabelWidth;
                    EditorGUI.PropertyField(new Rect(rest.x, rest.y, half, rest.height), min, s_min);
                    EditorGUI.PropertyField(new Rect(rest.x + half + Spacing, rest.y, half, rest.height), max, s_max);
                    EditorGUIUtility.labelWidth = labelWidth;
                    break;
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}
