using System;
using TimboJimbo.UI.Layout;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Layout
{
    /// <summary>
    /// A match fit on its label's line as a row of buttons, its mode's lit, as Box's corner shapes are (none is while
    /// fits that differ are edited together).
    /// </summary>
    [CustomPropertyDrawer(typeof(MatchFit))]
    public sealed class MatchFitDrawer : PropertyDrawer
    {
        private static readonly MatchFit[] s_fits = (MatchFit[])Enum.GetValues(typeof(MatchFit));
        private static readonly GUIContent[] s_fitNames = FitNames();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            position.height = EditorGUIUtility.singleLineHeight;
            label = EditorGUI.BeginProperty(position, label, property);
            var field = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            // PrefixLabel has already placed the buttons' rect past the indented label.
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            int lit = property.hasMultipleDifferentValues ? -1 : Array.IndexOf(s_fits, (MatchFit)property.intValue);
            EditorGUI.BeginChangeCheck();
            int clicked = GUI.Toolbar(field, lit, s_fitNames);
            if (EditorGUI.EndChangeCheck() && clicked >= 0)
                property.intValue = (int)s_fits[clicked];

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }

        private static GUIContent[] FitNames()
        {
            var names = new GUIContent[s_fits.Length];
            for (int i = 0; i < s_fits.Length; i++)
            {
                names[i] = s_fits[i] switch
                {
                    MatchFit.Resize => new GUIContent("Resize", "The rect changes size, nothing scaled: what is inside is laid out at its own size, pinned at its top left."),
                    MatchFit.Fill => new GUIContent("Fill", "Scaled each way to fill the rect exactly, stretched or squashed on the way."),
                    MatchFit.Contain => new GUIContent("Contain", "Scaled evenly so that all of it fits inside the rect."),
                    MatchFit.Cover => new GUIContent("Cover", "Scaled evenly so that it covers the whole rect, running past it (Clip crops it)."),
                    _ => new GUIContent("Width", "Match Width: scaled evenly to the rect's width, as the web scales its snapshots."),
                };
            }
            return names;
        }
    }
}
