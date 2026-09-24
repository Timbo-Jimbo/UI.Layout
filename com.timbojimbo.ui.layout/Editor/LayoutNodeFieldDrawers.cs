using System;
using System.Collections.Generic;
using TimboJimbo.UI.Layout;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Layout
{
    /// <summary>Draws a node's padding on one line with its sides labelled L, R, T and B instead of X, Y, Z and W.</summary>
    [CustomPropertyDrawer(typeof(InsetsFieldAttribute))]
    internal sealed class InsetsFieldDrawer : PropertyDrawer
    {
        private const float Spacing = 4f;
        private const float MiniLabelWidth = 12f;
        private static readonly GUIContent[] s_labels = { new("L", "Left"), new("R", "Right"), new("T", "Top"), new("B", "Bottom") };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, label);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = MiniLabelWidth;

            var value = property.vector4Value;
            float width = (position.width - Spacing * 3f) / 4f;
            EditorGUI.BeginChangeCheck();
            for (int i = 0; i < 4; i++)
            {
                var rect = new Rect(position.x + i * (width + Spacing), position.y, width, position.height);
                value[i] = EditorGUI.FloatField(rect, s_labels[i], value[i]);
            }
            if (EditorGUI.EndChangeCheck())
                property.vector4Value = value;

            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }

    /// <summary>
    /// Draws a node's transition in two parts that inherit separately, each Inherit (the view transition's own) or
    /// Custom. Timing: its duration and delay on the same line. Motion, on a line of its own, with a preset menu and,
    /// when Custom, its fields below: midpoint, gap and curvature, then the departure and the arrival. The choices
    /// are the hidden override flags stored next to the timing and the node's own motion.
    /// </summary>
    [CustomPropertyDrawer(typeof(NodeTransitionAttribute))]
    internal sealed class NodeTransitionDrawer : PropertyDrawer
    {
        private const float Spacing = 4f;
        private const string OverrideField = "_overrideTransition";
        private const string OverrideMotionField = "_overrideMotion";
        private const string MotionField = "_ownMotion";
        private static readonly string[] s_modes = { "Inherit", "Custom" };
        private static readonly GUIContent s_duration = new("Dur", "Duration in seconds; with no delay either, the node snaps");
        private static readonly GUIContent s_delay = new("Delay", "Delay in seconds");
        private static readonly GUIContent s_motion = new("Motion", "How the node travels, fades and scales, whatever its timing: Inherit takes the view transition's own");
        private static readonly GUIContent s_preset = new("Preset…", "Fill the motion from a named preset");
        private static readonly string[] s_motionFields = { "Midpoint", "Gap", "Curvature", "Depart", "Arrive" };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var overrideMotion = property.serializedObject.FindProperty(OverrideMotionField);
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float height = line * 2f + spacing;
            if (overrideMotion != null && overrideMotion.boolValue)
                height += (line + spacing) * s_motionFields.Length;
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var overrides = property.serializedObject.FindProperty(OverrideField);
            var overrideMotion = property.serializedObject.FindProperty(OverrideMotionField);
            var motion = property.serializedObject.FindProperty(MotionField);
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.BeginProperty(position, label, property);
            var first = EditorGUI.PrefixLabel(new Rect(position.x, position.y, position.width, line), label);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            float labelWidth = EditorGUIUtility.labelWidth;
            float modeWidth = Mathf.Min(72f, first.width * 0.3f);

            // Timing: Inherit, or Custom with its duration and delay.
            var modeRect = new Rect(first.x, first.y, modeWidth, line);
            if (ModePopup(modeRect, overrides) && !overrides.boolValue)
            {
                // Inherit keeps no timing of its own, which is how saved data tells the two apart.
                property.FindPropertyRelative("Duration").floatValue = 0f;
                property.FindPropertyRelative("Delay").floatValue = 0f;
            }
            if (overrides.boolValue)
            {
                EditorGUIUtility.labelWidth = 40f;
                var rest = new Rect(modeRect.xMax + Spacing, first.y, Mathf.Max(0f, first.width - modeWidth - Spacing), line);
                float half = (rest.width - Spacing) * 0.5f;
                EditorGUI.PropertyField(new Rect(rest.x, rest.y, half, line), property.FindPropertyRelative("Duration"), s_duration);
                EditorGUI.PropertyField(new Rect(rest.x + half + Spacing, rest.y, half, line), property.FindPropertyRelative("Delay"), s_delay);
                EditorGUIUtility.labelWidth = labelWidth;
            }

            // Motion: Inherit, or Custom with a preset menu and its fields below.
            float y = first.y + line + spacing;
            EditorGUI.LabelField(new Rect(position.x, y, first.x - position.x, line), s_motion);
            var motionMode = new Rect(first.x, y, modeWidth, line);
            ModePopup(motionMode, overrideMotion);
            if (overrideMotion.boolValue)
            {
                var presetRect = new Rect(motionMode.xMax + Spacing, y, Mathf.Min(110f, first.width - modeWidth - Spacing), line);
                if (EditorGUI.DropdownButton(presetRect, s_preset, FocusType.Passive))
                {
                    var menu = new GenericMenu();
                    foreach (var (name, preset) in LayoutMotion.Presets)
                    {
                        var chosen = preset;
                        menu.AddItem(new GUIContent(name), false, () =>
                        {
                            motion.serializedObject.Update();
                            Write(motion, chosen);
                            motion.serializedObject.ApplyModifiedProperties();
                        });
                    }
                    menu.DropDown(presetRect);
                }
                EditorGUIUtility.labelWidth = 70f;
                foreach (var field in s_motionFields)
                {
                    y += line + spacing;
                    EditorGUI.PropertyField(new Rect(first.x, y, first.width, line), motion.FindPropertyRelative(field));
                }
                EditorGUIUtility.labelWidth = labelWidth;
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }

        // An Inherit/Custom popup over a flag; true when the user changed it.
        private static bool ModePopup(Rect rect, SerializedProperty flag)
        {
            EditorGUI.showMixedValue = flag.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int mode = EditorGUI.Popup(rect, flag.boolValue ? 1 : 0, s_modes);
            bool changed = EditorGUI.EndChangeCheck();
            if (changed)
                flag.boolValue = mode == 1;
            EditorGUI.showMixedValue = false;
            return changed;
        }

        private static void Write(SerializedProperty motion, LayoutMotion value)
        {
            motion.FindPropertyRelative("Midpoint").floatValue = value.Midpoint;
            motion.FindPropertyRelative("Gap").floatValue = value.Gap;
            motion.FindPropertyRelative("Curvature").floatValue = value.Curvature;
            Write(motion.FindPropertyRelative("Depart"), value.Depart);
            Write(motion.FindPropertyRelative("Arrive"), value.Arrive);
        }

        private static void Write(SerializedProperty phase, MotionPhase value)
        {
            phase.FindPropertyRelative("Move").boolValue = value.Move;
            phase.FindPropertyRelative("Fade").boolValue = value.Fade;
            phase.FindPropertyRelative("Scale").boolValue = value.Scale;
            phase.FindPropertyRelative("Ease").intValue = (int)value.Ease;
        }
    }

    /// <summary>Draws a motion phase on one line: its three switches, then its ease.</summary>
    [CustomPropertyDrawer(typeof(MotionPhase))]
    internal sealed class MotionPhaseDrawer : PropertyDrawer
    {
        private static readonly string[] s_switches = { "Move", "Fade", "Scale" };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, label);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            float x = position.x;
            foreach (var name in s_switches)
            {
                var field = property.FindPropertyRelative(name);
                var content = new GUIContent(name, field.tooltip);
                float width = EditorStyles.toggle.CalcSize(content).x;
                EditorGUI.BeginChangeCheck();
                bool value = EditorGUI.ToggleLeft(new Rect(x, position.y, width, position.height), content, field.boolValue);
                if (EditorGUI.EndChangeCheck())
                    field.boolValue = value;
                x += width + 6f;
            }
            EditorGUI.PropertyField(new Rect(x, position.y, Mathf.Max(0f, position.xMax - x), position.height), property.FindPropertyRelative("Ease"), GUIContent.none);
            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}
