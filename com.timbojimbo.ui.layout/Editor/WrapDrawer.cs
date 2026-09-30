using System;
using TimboJimbo.UI.Layout;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Layout
{
    /// <summary>
    /// A wrap on its label's line as a row of buttons, its mode's lit, as Box's corner shapes are (none is while wraps
    /// that differ in it are edited together); then, only for a grid, how many cells to a line, and only for an adaptive
    /// grid, the shortest a cell may be. While wraps of different modes are edited together, both show.
    /// </summary>
    [CustomPropertyDrawer(typeof(Wrap))]
    public sealed class WrapDrawer : PropertyDrawer
    {
        private static readonly LayoutWrapMode[] s_modes = (LayoutWrapMode[])Enum.GetValues(typeof(LayoutWrapMode));
        private static readonly GUIContent[] s_modeNames = ModeNames();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (Shows(property, LayoutWrapMode.Grid))
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(nameof(Wrap.Count)));
            if (Shows(property, LayoutWrapMode.Adaptive))
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(nameof(Wrap.MinSize)));
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Settled up front, as GetPropertyHeight settled them: what a button picks in this event lays out on the next.
            bool count = Shows(property, LayoutWrapMode.Grid);
            bool minSize = Shows(property, LayoutWrapMode.Adaptive);

            label = EditorGUI.BeginProperty(position, label, property);
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            var field = EditorGUI.PrefixLabel(row, GUIUtility.GetControlID(FocusType.Passive), label);

            // PrefixLabel has already placed the buttons' rect past the indented label.
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            var mode = property.FindPropertyRelative(nameof(Wrap.Mode));
            int lit = mode.hasMultipleDifferentValues ? -1 : Array.IndexOf(s_modes, (LayoutWrapMode)mode.intValue);
            EditorGUI.BeginChangeCheck();
            int clicked = GUI.Toolbar(field, lit, s_modeNames);
            if (EditorGUI.EndChangeCheck() && clicked >= 0)
            {
                mode.intValue = (int)s_modes[clicked];
                // A grid of no cells to a line would be laid out as one; it starts at one it says.
                var cells = property.FindPropertyRelative(nameof(Wrap.Count));
                if (s_modes[clicked] == LayoutWrapMode.Grid && cells.intValue < 1)
                    cells.intValue = 1;
            }

            EditorGUI.indentLevel = indent + 1;
            if (count)
                Row(ref row, property.FindPropertyRelative(nameof(Wrap.Count)));
            if (minSize)
                Row(ref row, property.FindPropertyRelative(nameof(Wrap.MinSize)));

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

        // Whether the field that only `mode` uses shows: for that mode, or while wraps of different modes are edited.
        private static bool Shows(SerializedProperty property, LayoutWrapMode mode)
        {
            var current = property.FindPropertyRelative(nameof(Wrap.Mode));
            return current.hasMultipleDifferentValues || current.intValue == (int)mode;
        }

        private static GUIContent[] ModeNames()
        {
            var names = new GUIContent[s_modes.Length];
            for (int i = 0; i < s_modes.Length; i++)
            {
                names[i] = s_modes[i] switch
                {
                    LayoutWrapMode.Lines => new GUIContent("Lines", "A child that would run past the end of a line starts the next, as text wraps."),
                    LayoutWrapMode.Grid => new GUIContent("Grid", "Lines of Count equal cells."),
                    LayoutWrapMode.Adaptive => new GUIContent("Adaptive", "Lines of as many equal cells at least Min Size long as fit."),
                    _ => new GUIContent("None", "One line."),
                };
            }
            return names;
        }
    }
}
