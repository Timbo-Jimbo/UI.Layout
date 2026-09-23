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
    /// Draws a node's transition in two parts that inherit separately. Timing: Inherit (the view transition's own)
    /// on one line, or Custom and then its duration and ease, and its delay on a second line. Motion, on a line of
    /// its own: Inherit (the view transition's own) or one of every <see cref="ITransitionMotion"/> type in the
    /// project, with the chosen motion's fields below. The timing's choice is the hidden override flag stored next
    /// to it, and the motion is the node's serialized reference beside it, null when it inherits.
    /// </summary>
    [CustomPropertyDrawer(typeof(NodeTransitionAttribute))]
    internal sealed class NodeTransitionDrawer : PropertyDrawer
    {
        private const float Spacing = 4f;
        private const string OverrideField = "_overrideTransition";
        private const string MotionField = "_motion";
        private static readonly string[] s_modes = { "Inherit", "Custom" };
        private static readonly GUIContent s_duration = new("Dur", "Duration in seconds; with no delay either, the node snaps");
        private static readonly GUIContent s_delay = new("Delay", "Delay in seconds");
        private static readonly GUIContent s_motion = new("Motion", "How the node travels, whatever its timing: Inherit takes the view transition's own motion; anything else replaces it");

        private static Type[] s_motionTypes;
        private static GUIContent[] s_motionNames;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var overrides = property.serializedObject.FindProperty(OverrideField);
            var motion = property.serializedObject.FindProperty(MotionField);
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float height = line;
            if (overrides != null && overrides.boolValue)
                height += spacing + line;
            if (motion != null)
            {
                height += spacing + line;
                foreach (var child in Children(motion))
                    height += EditorGUI.GetPropertyHeight(child, true) + spacing;
            }
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var overrides = property.serializedObject.FindProperty(OverrideField);
            var motion = property.serializedObject.FindProperty(MotionField);
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.BeginProperty(position, label, property);
            var first = EditorGUI.PrefixLabel(new Rect(position.x, position.y, position.width, line), label);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            float labelWidth = EditorGUIUtility.labelWidth;

            // Timing.
            float modeWidth = Mathf.Min(72f, first.width * 0.3f);
            var modeRect = new Rect(first.x, first.y, modeWidth, line);
            EditorGUI.showMixedValue = overrides.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int mode = EditorGUI.Popup(modeRect, overrides.boolValue ? 1 : 0, s_modes);
            if (EditorGUI.EndChangeCheck())
            {
                overrides.boolValue = mode == 1;
                // Inherit keeps no timing of its own, which is how saved data tells the two apart.
                if (mode == 0)
                {
                    property.FindPropertyRelative("Duration").floatValue = 0f;
                    property.FindPropertyRelative("Ease").enumValueIndex = 0;
                    property.FindPropertyRelative("Delay").floatValue = 0f;
                }
            }
            EditorGUI.showMixedValue = false;

            float y = first.y;
            if (overrides.boolValue)
            {
                EditorGUIUtility.labelWidth = 44f;
                var rest = new Rect(modeRect.xMax + Spacing, first.y, Mathf.Max(0f, first.width - modeWidth - Spacing), line);
                float half = (rest.width - Spacing) * 0.5f;
                EditorGUI.PropertyField(new Rect(rest.x, rest.y, half, line), property.FindPropertyRelative("Duration"), s_duration);
                EditorGUI.PropertyField(new Rect(rest.x + half + Spacing, rest.y, half, line), property.FindPropertyRelative("Ease"), GUIContent.none);
                // The delay lines up under the first line's fields, after the mode.
                y += line + spacing;
                EditorGUI.PropertyField(new Rect(first.x, y, modeWidth + Spacing + half, line), property.FindPropertyRelative("Delay"), s_delay);
                EditorGUIUtility.labelWidth = labelWidth;
            }

            // Motion, then its own fields indented under it.
            if (motion != null)
            {
                y += line + spacing;
                EditorGUIUtility.labelWidth = 52f;
                MotionPopup(new Rect(first.x, y, first.width, line), motion);
                EditorGUIUtility.labelWidth = labelWidth;
                y += line + spacing;
                foreach (var child in Children(motion))
                {
                    float height = EditorGUI.GetPropertyHeight(child, true);
                    EditorGUI.PropertyField(new Rect(first.x, y, first.width, height), child, true);
                    y += height + spacing;
                }
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }

        private static void MotionPopup(Rect rect, SerializedProperty motion)
        {
            EnsureMotionTypes();
            var current = motion.managedReferenceValue?.GetType();
            int index = current == null ? 0 : Array.IndexOf(s_motionTypes, current) + 1;
            EditorGUI.showMixedValue = motion.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int chosen = EditorGUI.Popup(rect, s_motion, Mathf.Max(0, index), s_motionNames);
            if (EditorGUI.EndChangeCheck() && chosen != index)
                motion.managedReferenceValue = chosen == 0 ? null : Activator.CreateInstance(s_motionTypes[chosen - 1]);
            EditorGUI.showMixedValue = false;
        }

        // Every concrete, serializable motion type with a parameterless constructor, named without "Motion".
        private static void EnsureMotionTypes()
        {
            if (s_motionTypes != null) return;
            var types = new List<Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<ITransitionMotion>())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition || !type.IsSerializable) continue;
                if (typeof(UnityEngine.Object).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null) continue;
                types.Add(type);
            }
            types.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            s_motionTypes = types.ToArray();
            s_motionNames = new GUIContent[s_motionTypes.Length + 1];
            s_motionNames[0] = new GUIContent("Inherit", "The view transition's own motion");
            for (int i = 0; i < s_motionTypes.Length; i++)
            {
                var name = s_motionTypes[i].Name;
                if (name.EndsWith("Motion") && name.Length > "Motion".Length)
                    name = name.Substring(0, name.Length - "Motion".Length);
                s_motionNames[i + 1] = new GUIContent(ObjectNames.NicifyVariableName(name), s_motionTypes[i].FullName);
            }
        }

        private static IEnumerable<SerializedProperty> Children(SerializedProperty property)
        {
            if (property == null || property.managedReferenceValue == null) yield break;
            var child = property.Copy();
            var end = property.GetEndProperty();
            if (!child.NextVisible(true)) yield break;
            while (!SerializedProperty.EqualContents(child, end))
            {
                yield return child.Copy();
                if (!child.NextVisible(false)) yield break;
            }
        }
    }
}
