using TimboJimbo.UI.Layout;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Layout
{
    /// <summary>
    /// What a node floats against, on its label's line; then, only while it floats (or while nodes that differ in what
    /// they float against are edited together), where it is placed: its point, the point it meets, and its offset.
    /// </summary>
    [CustomPropertyDrawer(typeof(Floating))]
    public sealed class FloatingDrawer : PropertyDrawer
    {
        private static readonly string[] s_placement =
        {
            nameof(Floating.Point), nameof(Floating.TargetPoint), nameof(Floating.Offset),
        };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!ShowsPlacement(property))
                return height;
            foreach (var name in s_placement)
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(name));
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Settled up front, as GetPropertyHeight settled it: floating or not from this event's popup lays out on the
            // next.
            bool placement = ShowsPlacement(property);

            label = EditorGUI.BeginProperty(position, label, property);
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            var field = EditorGUI.PrefixLabel(row, GUIUtility.GetControlID(FocusType.Passive), label);

            // PrefixLabel has already placed the popup's rect past the indented label.
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUI.PropertyField(field, property.FindPropertyRelative(nameof(Floating.AttachTo)), GUIContent.none);

            if (placement)
            {
                EditorGUI.indentLevel = indent + 1;
                foreach (var name in s_placement)
                {
                    var child = property.FindPropertyRelative(name);
                    row.y = row.yMax + EditorGUIUtility.standardVerticalSpacing;
                    row.height = EditorGUI.GetPropertyHeight(child);
                    EditorGUI.PropertyField(row, child, true);
                }
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }

        private static bool ShowsPlacement(SerializedProperty property)
        {
            var attach = property.FindPropertyRelative(nameof(Floating.AttachTo));
            return attach.hasMultipleDifferentValues || attach.intValue != (int)FloatingAttach.None;
        }
    }
}
