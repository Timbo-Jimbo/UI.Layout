using TimboJimbo.UI.Layout;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace TimboJimboEditor.UI.Layout
{
    /// <summary>
    /// Inspector for <see cref="LayoutNode"/>, its settings grouped the way they read: whether it shows, its size, how
    /// it lays out what is inside it, which way it scrolls that, where it is placed apart from that (floating, and moved
    /// by its offset), then how it moves. What does not apply is hidden: its height while its aspect ratio sets it from
    /// its width, and, through their drawers, a sizing's value but for fixed and percent and a floating's placement but
    /// while it floats. In play mode a scroll container also shows, read only, how far it is scrolled, how far it can
    /// be, and whether it is scrolling.
    /// </summary>
    [CustomEditor(typeof(LayoutNode))]
    [CanEditMultipleObjects]
    public sealed class LayoutNodeEditor : UnityEditor.Editor
    {
        private static readonly GUIContent s_scrollOffsetLabel = new("Scroll Offset",
            "How far its children are scrolled, x right and y down, from 0 (the start) to its scroll range.");
        private static readonly GUIContent s_scrollRangeLabel = new("Scroll Range",
            "How far it can be scrolled each way: how far its content runs past its edge (0 when it fits).");
        private static readonly GUIContent s_isScrollingLabel = new("Is Scrolling",
            "Whether it is being dragged, gliding, or springing to where it scrolls.");

        private SerializedProperty _display;
        private SerializedProperty _width;
        private SerializedProperty _aspectRatio;
        private SerializedProperty _height;
        private SerializedProperty _padding;
        private SerializedProperty _childGap;
        private SerializedProperty _direction;
        private SerializedProperty _childAlignX;
        private SerializedProperty _childAlignY;
        private SerializedProperty _scroll;
        private SerializedProperty _floating;
        private SerializedProperty _offset;
        private SerializedProperty _animation;

        // The scroll axis under its group's header, where "Scroll" again would only repeat it; its tooltip is the
        // field's own.
        private GUIContent _scrollLabel;

        // The scroll readout as last painted, so the inspector is drawn again only while that is out of date.
        private Vector2 _shownOffset;
        private Vector2 _shownRange;
        private bool _shownScrolling;

        private void OnEnable()
        {
            _display = serializedObject.FindProperty("_display");
            _width = serializedObject.FindProperty("_width");
            _aspectRatio = serializedObject.FindProperty("_aspectRatio");
            _height = serializedObject.FindProperty("_height");
            _padding = serializedObject.FindProperty("_padding");
            _childGap = serializedObject.FindProperty("_childGap");
            _direction = serializedObject.FindProperty("_direction");
            _childAlignX = serializedObject.FindProperty("_childAlignX");
            _childAlignY = serializedObject.FindProperty("_childAlignY");
            _scroll = serializedObject.FindProperty("_scroll");
            _floating = serializedObject.FindProperty("_floating");
            _offset = serializedObject.FindProperty("_offset");
            _animation = serializedObject.FindProperty("_animation");
            _scrollLabel = new GUIContent("Axis", _scroll.tooltip);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_display);

            Header("Size");
            EditorGUILayout.PropertyField(_width);
            // The aspect ratio comes before the height it hides, so typing a ratio in does not move its own field (and
            // lose it the keyboard) as the height goes; while nodes that differ in it are edited together, the height
            // shows.
            EditorGUILayout.PropertyField(_aspectRatio);
            if (_aspectRatio.hasMultipleDifferentValues || _aspectRatio.floatValue <= 0f)
                EditorGUILayout.PropertyField(_height);

            Header("Content");
            EditorGUILayout.PropertyField(_padding);
            EditorGUILayout.PropertyField(_childGap);
            EditorGUILayout.PropertyField(_direction);
            EditorGUILayout.PropertyField(_childAlignX);
            EditorGUILayout.PropertyField(_childAlignY);

            Header("Scroll");
            EditorGUILayout.PropertyField(_scroll, _scrollLabel);
            ScrollReadout();

            Header("Position");
            EditorGUILayout.PropertyField(_floating);
            EditorGUILayout.PropertyField(_offset);

            Header("Motion");
            EditorGUILayout.PropertyField(_animation);

            serializedObject.ApplyModifiedProperties();
        }

        // Draws the inspector again while the scroll readout is out of date: every frame it scrolls, once more as it
        // comes to rest, and once when it is scrolled at once or its range changes. The inspector asks this every
        // update, so a scroll that starts while nothing else draws it is still caught. Not while this component is
        // folded away, where the readout is never painted and so would never be up to date.
        public override bool RequiresConstantRepaint()
        {
            return ShowsReadout(out var node) && InternalEditorUtility.GetIsInspectorExpanded(node)
                && (node.ScrollOffset != _shownOffset || node.ScrollRange != _shownRange || node.IsScrolling != _shownScrolling);
        }

        // In play mode, how far a scroll container is scrolled, how far it can be and whether it is scrolling, read only
        // (the system owns them), under its axis. What a repaint shows is noted for RequiresConstantRepaint; another
        // event reads the same values but shows nothing, so noting them then would hide that the screen is behind.
        private void ScrollReadout()
        {
            if (!ShowsReadout(out var node)) return;

            var offset = node.ScrollOffset;
            var range = node.ScrollRange;
            bool scrolling = node.IsScrolling;
            if (Event.current.type == EventType.Repaint)
            {
                _shownOffset = offset;
                _shownRange = range;
                _shownScrolling = scrolling;
            }

            EditorGUI.indentLevel++;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector2Field(s_scrollOffsetLabel, offset);
                EditorGUILayout.Vector2Field(s_scrollRangeLabel, range);
                EditorGUILayout.Toggle(s_isScrollingLabel, scrolling);
            }
            EditorGUI.indentLevel--;
        }

        // Whether the scroll readout shows: in play mode, for one enabled scroll container at a time (several scrolled
        // differently have no one value to show). Settled from the node as it is, so a new axis picked in the popup
        // shows or hides it from the next event on, once it has been applied, and the layout and repaint of one event
        // always agree.
        private bool ShowsReadout(out LayoutNode node)
        {
            node = target as LayoutNode;
            return Application.isPlaying && targets.Length == 1 && node != null && node.isActiveAndEnabled
                && node.Scroll != ScrollAxis.None;
        }

        private static void Header(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
