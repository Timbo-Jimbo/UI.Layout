using TimboJimbo.UI.Layout;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace TimboJimboEditor.UI.Layout
{
    /// <summary>
    /// Inspector for <see cref="LayoutNode"/>, its settings grouped the way they read: whether it shows, its size, how
    /// it lays out what is inside it (and the safe area it keeps clear of, or reaches past), which way it scrolls that,
    /// which end it keeps to, where it comes to rest and whether it shows indicators, where and how it is drawn apart from
    /// that (floating, moved by its offset, scaled and faded), then how it moves: its animation, how it appears and
    /// disappears, and the name it is matched by, with how it fills the rect it moves through then and whether it is cut
    /// to it. What does not apply is hidden: its height while its aspect ratio sets it from its width, the safe area a
    /// root keeps on any other node and the one a node reaches past on a root, its scroll anchor, snap and indicators
    /// while it does not scroll, the indicators' colour while it shows none, its match fit and clip while it has no name,
    /// and,
    /// through their drawers, a sizing's value but for fixed and percent, a wrap's count but for a grid and its min size
    /// but for an adaptive one, a floating's placement but while it floats, and its element but while it floats against
    /// one. In play mode a scroll container also shows, read only, how far it is scrolled, how far it can be, and whether
    /// it is scrolling, and every node the id it is matched by.
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
        private static readonly GUIContent s_matchIdLabel = new("Match Id",
            "Which of the nodes with its match name this is, set in code (MatchId): its own id, or else the nearest one set on a node above it, followed by that node's name; none when no id is set.");

        private SerializedProperty _display;
        private SerializedProperty _width;
        private SerializedProperty _aspectRatio;
        private SerializedProperty _height;
        private SerializedProperty _padding;
        private SerializedProperty _safeArea;
        private SerializedProperty _ignoresSafeArea;
        private SerializedProperty _childGap;
        private SerializedProperty _direction;
        private SerializedProperty _wrap;
        private SerializedProperty _childAlignX;
        private SerializedProperty _childAlignY;
        private SerializedProperty _scroll;
        private SerializedProperty _scrollAnchor;
        private SerializedProperty _scrollSnap;
        private SerializedProperty _showsScrollIndicators;
        private SerializedProperty _scrollIndicatorColor;
        private SerializedProperty _floating;
        private SerializedProperty _offset;
        private SerializedProperty _scale;
        private SerializedProperty _opacity;
        private SerializedProperty _animation;
        private SerializedProperty _displayEffect;
        private SerializedProperty _matchName;
        private SerializedProperty _matchFit;
        private SerializedProperty _matchClip;

        // The scroll settings under their group's header, where "Scroll" again would only repeat it, and the match
        // settings under the name; their tooltips are the fields' own.
        private GUIContent _scrollLabel;
        private GUIContent _scrollAnchorLabel;
        private GUIContent _scrollSnapLabel;
        private GUIContent _indicatorsLabel;
        private GUIContent _indicatorColorLabel;
        private GUIContent _matchFitLabel;
        private GUIContent _matchClipLabel;

        // The readouts as last painted, so the inspector is drawn again only while one is out of date.
        private Vector2 _shownOffset;
        private Vector2 _shownRange;
        private bool _shownScrolling;
        private object _shownId;
        private LayoutNode _shownIdFrom;

        private void OnEnable()
        {
            _display = serializedObject.FindProperty("_display");
            _width = serializedObject.FindProperty("_width");
            _aspectRatio = serializedObject.FindProperty("_aspectRatio");
            _height = serializedObject.FindProperty("_height");
            _padding = serializedObject.FindProperty("_padding");
            _safeArea = serializedObject.FindProperty("_safeArea");
            _ignoresSafeArea = serializedObject.FindProperty("_ignoresSafeArea");
            _childGap = serializedObject.FindProperty("_childGap");
            _direction = serializedObject.FindProperty("_direction");
            _wrap = serializedObject.FindProperty("_wrap");
            _childAlignX = serializedObject.FindProperty("_childAlignX");
            _childAlignY = serializedObject.FindProperty("_childAlignY");
            _scroll = serializedObject.FindProperty("_scroll");
            _scrollAnchor = serializedObject.FindProperty("_scrollAnchor");
            _scrollSnap = serializedObject.FindProperty("_scrollSnap");
            _showsScrollIndicators = serializedObject.FindProperty("_showsScrollIndicators");
            _scrollIndicatorColor = serializedObject.FindProperty("_scrollIndicatorColor");
            _floating = serializedObject.FindProperty("_floating");
            _offset = serializedObject.FindProperty("_offset");
            _scale = serializedObject.FindProperty("_scale");
            _opacity = serializedObject.FindProperty("_opacity");
            _animation = serializedObject.FindProperty("_animation");
            _displayEffect = serializedObject.FindProperty("_displayEffect");
            _matchName = serializedObject.FindProperty("_matchName");
            _matchFit = serializedObject.FindProperty("_matchFit");
            _matchClip = serializedObject.FindProperty("_matchClip");
            _scrollLabel = new GUIContent("Axis", _scroll.tooltip);
            _scrollAnchorLabel = new GUIContent("Anchor", _scrollAnchor.tooltip);
            _scrollSnapLabel = new GUIContent("Snap", _scrollSnap.tooltip);
            _indicatorsLabel = new GUIContent("Indicators", _showsScrollIndicators.tooltip);
            _indicatorColorLabel = new GUIContent("Indicator Color", _scrollIndicatorColor.tooltip);
            _matchFitLabel = new GUIContent("Fit", _matchFit.tooltip);
            _matchClipLabel = new GUIContent("Clip", _matchClip.tooltip);
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
            // A root keeps its content clear of the safe area, and any other node can reach out past it: only the one
            // that applies shows, both while roots and other nodes are edited together.
            Kinds(out bool roots, out bool others);
            if (roots)
                EditorGUILayout.PropertyField(_safeArea);
            if (others)
                EditorGUILayout.PropertyField(_ignoresSafeArea);
            EditorGUILayout.PropertyField(_childGap);
            EditorGUILayout.PropertyField(_direction);
            EditorGUILayout.PropertyField(_wrap);
            EditorGUILayout.PropertyField(_childAlignX);
            EditorGUILayout.PropertyField(_childAlignY);

            Header("Scroll");
            EditorGUILayout.PropertyField(_scroll, _scrollLabel);
            // The rest only means something for a node that scrolls, and the indicators' colour for one that shows them;
            // while nodes that differ in either are edited together, it shows.
            if (_scroll.hasMultipleDifferentValues || _scroll.intValue != (int)ScrollAxis.None)
            {
                EditorGUILayout.PropertyField(_scrollAnchor, _scrollAnchorLabel);
                EditorGUILayout.PropertyField(_scrollSnap, _scrollSnapLabel);
                EditorGUILayout.PropertyField(_showsScrollIndicators, _indicatorsLabel);
                if (_showsScrollIndicators.hasMultipleDifferentValues || _showsScrollIndicators.boolValue)
                    EditorGUILayout.PropertyField(_scrollIndicatorColor, _indicatorColorLabel);
            }
            ScrollReadout();

            Header("Position");
            EditorGUILayout.PropertyField(_floating);
            EditorGUILayout.PropertyField(_offset);
            EditorGUILayout.PropertyField(_scale);
            EditorGUILayout.PropertyField(_opacity);

            Header("Motion");
            EditorGUILayout.PropertyField(_animation);
            EditorGUILayout.PropertyField(_displayEffect);
            EditorGUILayout.PropertyField(_matchName);
            // How it fills the rect it moves through, and whether it is cut to it, only mean something for a node with a
            // name; while nodes that differ in it are edited together, they show.
            if (_matchName.hasMultipleDifferentValues || _matchName.stringValue.Length > 0)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_matchFit, _matchFitLabel);
                EditorGUILayout.PropertyField(_matchClip, _matchClipLabel);
                EditorGUI.indentLevel--;
            }
            MatchIdReadout();

            serializedObject.ApplyModifiedProperties();
        }

        // Draws the inspector again while a readout is out of date: for the scroll, every frame it scrolls, once more as
        // it comes to rest, and once when it is scrolled at once or its range changes; for the match id, once when code
        // sets it here or above. The inspector asks this every update, so a change made while nothing else draws it is
        // still caught. Not while this component is folded away, where the readouts are never painted and so would
        // never be up to date.
        public override bool RequiresConstantRepaint()
        {
            if (!ShowsReadouts(out var node) || !InternalEditorUtility.GetIsInspectorExpanded(node)) return false;

            bool scrollBehind = node.Scroll != ScrollAxis.None
                && (node.ScrollOffset != _shownOffset || node.ScrollRange != _shownRange || node.IsScrolling != _shownScrolling);
            object id = MatchIdOf(node, out var from);
            return scrollBehind || !object.Equals(id, _shownId) || from != _shownIdFrom;
        }

        // In play mode, how far a scroll container is scrolled, how far it can be and whether it is scrolling, read only
        // (the system owns them), under its axis. Whether it is a scroll container is settled from the node as it is,
        // so a new axis picked in the popup shows or hides this from the next event on, once it has been applied, and
        // the layout and repaint of one event always agree. What a repaint shows is noted for RequiresConstantRepaint;
        // another event reads the same values but shows nothing, so noting them then would hide that the screen is
        // behind.
        private void ScrollReadout()
        {
            if (!ShowsReadouts(out var node) || node.Scroll == ScrollAxis.None) return;

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

        // In play mode, the id it is matched by, read only (it is set in code and never saved), under its match name: its
        // own, the one it inherits with the node that comes from, or none. What a repaint shows is noted as the scroll
        // readout's is.
        private void MatchIdReadout()
        {
            if (!ShowsReadouts(out var node)) return;

            object id = MatchIdOf(node, out var from);
            if (Event.current.type == EventType.Repaint)
            {
                _shownId = id;
                _shownIdFrom = from;
            }

            string text = id == null ? "none" : from == null ? id.ToString() : $"{id} (from {from.name})";
            EditorGUI.indentLevel++;
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField(s_matchIdLabel, text);
            EditorGUI.indentLevel--;
        }

        // The id the layout system matches the node by: its own, or when that is null the nearest one set on an enabled
        // node above it, through any plain objects in between (so a root takes it from the node it is mounted under);
        // null when none is. Worked out here from the public ids, since the one the system keeps is internal. from is
        // the node it comes from, null when it is its own.
        private static object MatchIdOf(LayoutNode node, out LayoutNode from)
        {
            from = null;
            if (node.MatchId != null) return node.MatchId;

            for (var above = node.transform.parent; above != null; above = above.parent)
            {
                if (above.TryGetComponent(out LayoutNode aboveNode) && aboveNode.isActiveAndEnabled && aboveNode.MatchId != null)
                {
                    from = aboveNode;
                    return aboveNode.MatchId;
                }
            }
            return null;
        }

        // Whether any node being edited is a root, and whether any is not.
        private void Kinds(out bool roots, out bool others)
        {
            roots = others = false;
            foreach (var edited in targets)
            {
                if (edited is LayoutNode node && node.IsRoot)
                    roots = true;
                else
                    others = true;
            }
        }

        // Whether the readouts show: in play mode, for one enabled node at a time (several scrolled differently, or
        // matched by different ids, have no one value to show).
        private bool ShowsReadouts(out LayoutNode node)
        {
            node = target as LayoutNode;
            return Application.isPlaying && targets.Length == 1 && node != null && node.isActiveAndEnabled;
        }

        private static void Header(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
