using TimboJimbo.UI.Layout;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Layout
{
    /// <summary>
    /// A set of edges on its label's line as four buttons, one for each edge, each lit while the set has it, as Box's
    /// corner shapes are. None is lit while sets that differ are edited together; a click then gives every one of them
    /// the first's set with that edge added.
    /// </summary>
    [CustomPropertyDrawer(typeof(Edges))]
    public sealed class EdgesDrawer : PropertyDrawer
    {
        private static readonly Edges[] s_edges = { Edges.Left, Edges.Right, Edges.Top, Edges.Bottom };
        private static readonly GUIContent[] s_edgeNames =
        {
            new("←", "Left"), new("→", "Right"), new("↑", "Top"), new("↓", "Bottom"),
        };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            position.height = EditorGUIUtility.singleLineHeight;
            label = EditorGUI.BeginProperty(position, label, property);
            var field = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            // PrefixLabel has already placed the buttons' rect past the indented label.
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            bool mixed = property.hasMultipleDifferentValues;
            int set = property.intValue;
            float width = field.width / s_edges.Length;
            for (int i = 0; i < s_edges.Length; i++)
            {
                int edge = (int)s_edges[i];
                var style = i == 0 ? EditorStyles.miniButtonLeft : i == s_edges.Length - 1 ? EditorStyles.miniButtonRight : EditorStyles.miniButtonMid;
                var rect = new Rect(field.x + i * width, field.y, width, field.height);
                EditorGUI.BeginChangeCheck();
                bool on = GUI.Toggle(rect, !mixed && (set & edge) != 0, s_edgeNames[i], style);
                if (EditorGUI.EndChangeCheck())
                    property.intValue = on ? set | edge : set & ~edge;
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}
