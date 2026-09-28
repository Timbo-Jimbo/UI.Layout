using System;
using TimboJimbo.UI.Layout;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Layout
{
    /// <summary>
    /// A layout animation under a foldout, with a row of preset buttons on its header line, then (unfolded) its duration,
    /// bounce, curvature and delay. The fields are the serialized truth: the preset whose bounce and curvature they
    /// match is lit, as Box's corner shapes are, and none is once they are changed from all of them (or while
    /// animations that differ in them are edited together), rather than a Custom button that would do nothing.
    /// </summary>
    [CustomPropertyDrawer(typeof(LayoutAnimation))]
    public sealed class LayoutAnimationDrawer : PropertyDrawer
    {
        // The space EditorGUI.PrefixLabel leaves between a label and its field, which the presets keep to line up with
        // the fields under them.
        private const float PrefixPadding = 2f;

        private static readonly LayoutAnimationPreset[] s_presets = (LayoutAnimationPreset[])Enum.GetValues(typeof(LayoutAnimationPreset));
        private static readonly GUIContent[] s_presetNames = PresetNames();
        private static readonly string[] s_fields =
        {
            nameof(LayoutAnimation.Duration), nameof(LayoutAnimation.Bounce), nameof(LayoutAnimation.Curvature), nameof(LayoutAnimation.Delay),
        };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
                return height;
            foreach (var name in s_fields)
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(name));
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Settled up front, as GetPropertyHeight settled it: unfolding lays out on the next event.
            bool expanded = property.isExpanded;

            label = EditorGUI.BeginProperty(position, label, property);
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            // The foldout takes only the label's part of the line, so a click on the presets beside it is theirs.
            float labelWidth = EditorGUIUtility.labelWidth;
            property.isExpanded = EditorGUI.Foldout(new Rect(row.x, row.y, labelWidth, row.height), expanded, label, true);
            var presets = new Rect(row.x + labelWidth + PrefixPadding, row.y, Mathf.Max(0f, row.width - labelWidth - PrefixPadding), row.height);
            Presets(presets, property);

            if (expanded)
            {
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = indent + 1;
                foreach (var name in s_fields)
                {
                    var child = property.FindPropertyRelative(name);
                    row.y = row.yMax + EditorGUIUtility.standardVerticalSpacing;
                    row.height = EditorGUI.GetPropertyHeight(child);
                    EditorGUI.PropertyField(row, child, true);
                }
                EditorGUI.indentLevel = indent;
            }

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// The preset buttons, the one the animation matches lit. Clicking one sets its bounce and curvature on every
        /// animation being edited; a preset leaves the duration and delay alone (<see cref="LayoutAnimation.Use"/>
        /// keeps them), so each keeps its own timing.
        /// </summary>
        private static void Presets(Rect rect, SerializedProperty property)
        {
            var bounce = property.FindPropertyRelative(nameof(LayoutAnimation.Bounce));
            var curvature = property.FindPropertyRelative(nameof(LayoutAnimation.Curvature));

            EditorGUI.BeginChangeCheck();
            int clicked = GUI.Toolbar(rect, PresetIndexFor(bounce, curvature), s_presetNames);
            if (EditorGUI.EndChangeCheck() && clicked >= 0)
            {
                var used = default(LayoutAnimation).Use(s_presets[clicked]);
                bounce.floatValue = used.Bounce;
                curvature.floatValue = used.Curvature;
            }
        }

        // The preset the bounce and curvature match, or -1 when they have been changed from all of them or differ
        // between the animations being edited.
        private static int PresetIndexFor(SerializedProperty bounce, SerializedProperty curvature)
        {
            if (bounce.hasMultipleDifferentValues || curvature.hasMultipleDifferentValues)
                return -1;

            var animation = new LayoutAnimation(0f, bounce.floatValue, curvature.floatValue);
            for (int i = 0; i < s_presets.Length; i++)
            {
                if (animation.Matches(s_presets[i]))
                    return i;
            }
            return -1;
        }

        private static GUIContent[] PresetNames()
        {
            var names = new GUIContent[s_presets.Length];
            for (int i = 0; i < s_presets.Length; i++)
                names[i] = new GUIContent(ObjectNames.NicifyVariableName(s_presets[i].ToString()), PresetTooltip(s_presets[i]));
            return names;
        }

        private static string PresetTooltip(LayoutAnimationPreset preset)
        {
            string what = preset switch
            {
                LayoutAnimationPreset.Smooth => "Settles without overshooting.",
                LayoutAnimationPreset.Snappy => "A small overshoot, quick to settle.",
                LayoutAnimationPreset.Bouncy => "A lively overshoot and swing back.",
                LayoutAnimationPreset.Arc => "Settles without overshooting, bowing out sideways on the way.",
                _ => string.Empty,
            };
            return what + " Sets the bounce and curvature; the duration and delay stay as they are.";
        }
    }
}
