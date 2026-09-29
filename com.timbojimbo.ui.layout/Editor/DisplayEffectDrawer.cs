using System;
using TimboJimbo.UI.Layout;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Layout
{
    /// <summary>
    /// A display effect under its label: whether it fades, how much it shrinks, then the edge it slides past as a row of
    /// buttons. The field is the serialized truth: its edge's button is lit, as Box's corner shapes are, and none is
    /// while effects that differ in it are edited together.
    /// </summary>
    [CustomPropertyDrawer(typeof(DisplayEffect))]
    public sealed class DisplayEffectDrawer : PropertyDrawer
    {
        private static readonly DisplayEdge[] s_edges = (DisplayEdge[])Enum.GetValues(typeof(DisplayEdge));
        private static readonly GUIContent[] s_edgeNames = EdgeNames();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            return EditorGUIUtility.singleLineHeight
                + spacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(nameof(DisplayEffect.Fade)))
                + spacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(nameof(DisplayEffect.Shrink)))
                + spacing + EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = EditorGUI.BeginProperty(position, label, property);
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(row, label);

            // Its three parts under its label, each still a property of its own (so prefab overrides, undo and editing
            // several nodes at once work on each).
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = indent + 1;
            Row(ref row, property.FindPropertyRelative(nameof(DisplayEffect.Fade)));
            Row(ref row, property.FindPropertyRelative(nameof(DisplayEffect.Shrink)));
            row.y = row.yMax + EditorGUIUtility.standardVerticalSpacing;
            row.height = EditorGUIUtility.singleLineHeight;
            Edges(row, property.FindPropertyRelative(nameof(DisplayEffect.Edge)));
            EditorGUI.indentLevel = indent;

            EditorGUI.EndProperty();
        }

        // Draws a field on the rows below the one given, which it moves on to.
        private static void Row(ref Rect row, SerializedProperty child)
        {
            row.y = row.yMax + EditorGUIUtility.standardVerticalSpacing;
            row.height = EditorGUI.GetPropertyHeight(child);
            EditorGUI.PropertyField(row, child, true);
        }

        /// <summary>
        /// The edge buttons beside the edge's label, the one it slides past lit. Clicking one sets it on every effect
        /// being edited.
        /// </summary>
        private static void Edges(Rect row, SerializedProperty edge)
        {
            var label = EditorGUI.BeginProperty(row, new GUIContent(edge.displayName, edge.tooltip), edge);
            var field = EditorGUI.PrefixLabel(row, GUIUtility.GetControlID(FocusType.Passive), label);

            EditorGUI.BeginChangeCheck();
            int clicked = GUI.Toolbar(field, EdgeIndexFor(edge), s_edgeNames);
            if (EditorGUI.EndChangeCheck() && clicked >= 0)
                edge.intValue = (int)s_edges[clicked];

            EditorGUI.EndProperty();
        }

        // The button of the edge it slides past, or -1 while effects that differ in it are edited together.
        private static int EdgeIndexFor(SerializedProperty edge)
        {
            return edge.hasMultipleDifferentValues ? -1 : Array.IndexOf(s_edges, (DisplayEdge)edge.intValue);
        }

        private static GUIContent[] EdgeNames()
        {
            var names = new GUIContent[s_edges.Length];
            for (int i = 0; i < s_edges.Length; i++)
                names[i] = EdgeName(s_edges[i]);
            return names;
        }

        // An arrow the way it slides out (and back in from), which reads at a glance in a row this short.
        private static GUIContent EdgeName(DisplayEdge edge)
        {
            return edge switch
            {
                DisplayEdge.Left => new GUIContent("←", "Slides past its parent's left edge."),
                DisplayEdge.Right => new GUIContent("→", "Slides past its parent's right edge."),
                DisplayEdge.Top => new GUIContent("↑", "Slides past its parent's top edge."),
                DisplayEdge.Bottom => new GUIContent("↓", "Slides past its parent's bottom edge."),
                _ => new GUIContent("None", "Does not slide."),
            };
        }
    }
}
