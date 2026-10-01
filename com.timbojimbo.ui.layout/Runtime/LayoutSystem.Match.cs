using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    public static partial class LayoutSystem
    {
        // ── Names ────────────────────────────────────────────────────────────────
        //
        // A node holds its MatchName, under its effective id (its own MatchId, or else the nearest one above it), while
        // it is shown. Inside Animate, a node the change starts showing pairs with the one other node that held its name
        // and id before the change, and one the change stops showing with the one other node that holds them after it.
        //
        // When the other changes too, one shown and one hidden, it is a hand-off: the source, and the destination that
        // takes over from it. The destination is given the source's presentation, where it is drawn and how fast it is
        // drawn moving there, and then moves to its own place on its own springs for the change, as any node does,
        // fading in. The source follows it, drawn at its rect beneath it and fading out from halfway, until the pair
        // lands (or it is shown again with no new pair, when it stops following and comes back as itself). Both fly
        // (LayoutSystem.Flight.cs), ignoring their parent groups, so neither a clip nor a page's fade cuts them on the
        // way. What is inside the destination rides it, laid out at its own size and pinned at its top left; what is
        // inside the source rides that.
        //
        // When the other stays shown, nothing is handed over and the other does not move: the one that changes plays its
        // own DisplayEffect with the other's rect as its away pose (its Anchor), in place of the effect's edge and
        // shrink, growing out of it as it is shown and shrinking back into it as it is hidden (a dropdown's list out of
        // its button), fading if its effect fades, and flying until the effect has played. What is inside it is laid out
        // at its own size, pinned at its top left. For a hand-off instead (a cell zooming into the page it opens), the
        // other is hidden in the same change.

        // Whether the pass under way is Animate's pass for its change, which notes who holds each name before and after.
        private static bool s_matching;

        // Who holds each name and id, before the change and after it, noted by that pass for every node shown with a name;
        // and the nodes that started or stopped being shown with one, in the order they were met (outer trees first,
        // parents before children), which are the only ones looked up. Kept for the next change, the holders' lists too.
        private static readonly Dictionary<MatchKey, Holders> s_holders = new();
        private static readonly Stack<Holders> s_spareHolders = new();
        private static readonly List<NodeState> s_changing = new();

        // Which ways each name was held (before or after the change, with an id or without one), for telling a node given
        // no id where the others have one, or the other way round.
        private static readonly Dictionary<string, byte> s_nameSides = new();
        private const byte BeforeWithId = 1, BeforeWithoutId = 2, AfterWithId = 4, AfterWithoutId = 8;

        // A change's pairs, in the order they were found (outer before those nested in them), and every node in one.
        private static readonly List<Pair> s_pairs = new();
        private static readonly HashSet<NodeState> s_paired = new();

        // Followers written once every tree has been (Frame), and scratch lists: followers, destinations, ancestors.
        private static readonly List<NodeState> s_following = new();
        private static readonly List<NodeState> s_followers = new();
        private static readonly List<NodeState> s_ending = new();
        private static readonly List<NodeState> s_above = new();

        // Names already said to be a mistake, each once: given to a root, held by several candidates, or with an id on
        // one side only.
        private static readonly HashSet<string> s_warnedRoot = new();
        private static readonly HashSet<string> s_warnedTwins = new();
        private static readonly HashSet<string> s_warnedOneSide = new();

        // A name and an id: equal when the names are ordinally and the ids by Equals(object), as a Dictionary compares
        // keys, so a string, a number, a Guid, an enum or a record is one id by its value, and any other class by
        // reference unless it overrides Equals and GetHashCode.
        private readonly struct MatchKey : IEquatable<MatchKey>
        {
            public readonly string Name;
            public readonly object Id;

            public MatchKey(string name, object id)
            {
                Name = name;
                Id = id;
            }

            public bool Equals(MatchKey other) => string.Equals(Name, other.Name, StringComparison.Ordinal) && object.Equals(Id, other.Id);

            public override bool Equals(object obj) => obj is MatchKey other && Equals(other);

            public override int GetHashCode() => (Name.GetHashCode() * 397) ^ (Id != null ? Id.GetHashCode() : 0);
        }

        // The nodes that held a name and id before the change, and those that hold them after it.
        private sealed class Holders
        {
            public readonly List<NodeState> Before = new();
            public readonly List<NodeState> After = new();
        }

        // A pair found, with where its source was drawn as the change's pass ended, read before anything is written:
        // the world corners of its rect (bottom left, top right), and how fast its centre and its size were drawn
        // moving, in world units a second.
        private struct Pair
        {
            public NodeState Source;
            public NodeState Destination;
            public Vector3 BottomLeft;
            public Vector3 TopRight;
            public Vector3 Velocity;
            public Vector2 SizeVelocity;
        }

        private static void ResetMatch()
        {
            s_matching = false;
            ClearKeys();
            s_spareHolders.Clear();
            s_pairs.Clear();
            s_paired.Clear();
            s_following.Clear();
            s_followers.Clear();
            s_ending.Clear();
            s_above.Clear();
            s_warnedRoot.Clear();
            s_warnedTwins.Clear();
            s_warnedOneSide.Clear();
        }

        // Forgets who held what, keeping the lists for the next change.
        private static void ClearKeys()
        {
            foreach (var holders in s_holders.Values)
            {
                holders.Before.Clear();
                holders.After.Clear();
                s_spareHolders.Push(holders);
            }
            s_holders.Clear();
            s_nameSides.Clear();
            s_changing.Clear();
        }

        // ── Holding ──────────────────────────────────────────────────────────────

        // As the pass visits a node (its PassShown just worked out): its name and id as the pass before found them, then
        // as this one does, its id its own or else that of the node above it (its layout parent, or for a root the node
        // above its transform), visited before it. In Animate's pass for its change, where the pass before was the one
        // just before the update, it is noted as holding its name before the change if it was shown then, and after it
        // if it is shown now; and, having started or stopped being shown with a name, as changing.
        private static void RecordKey(NodeState state, NodeState above)
        {
            state.WasName = state.PassName;
            state.WasId = state.PassId;
            var node = state.Node;
            state.PassName = node.MatchName;
            state.PassId = node.MatchId ?? above?.PassId;
            if (!s_matching) return;

            bool before = state.WasShown && !string.IsNullOrEmpty(state.WasName);
            bool after = state.PassShown && state.PassName.Length > 0;
            if (before)
                NoteHolder(state, state.WasName, state.WasId, false);
            if (after)
                NoteHolder(state, state.PassName, state.PassId, true);
            if (state.WasShown != state.PassShown && (state.PassShown ? after : before))
                s_changing.Add(state);
        }

        private static void NoteHolder(NodeState state, string name, object id, bool after)
        {
            var key = new MatchKey(name, id);
            if (!s_holders.TryGetValue(key, out var holders))
            {
                holders = s_spareHolders.Count > 0 ? s_spareHolders.Pop() : new Holders();
                s_holders.Add(key, holders);
            }
            (after ? holders.After : holders.Before).Add(state);
            s_nameSides.TryGetValue(name, out var sides);
            byte side = after ? id != null ? AfterWithId : AfterWithoutId : id != null ? BeforeWithId : BeforeWithoutId;
            s_nameSides[name] = (byte)(sides | side);
        }

        // ── Pairing ──────────────────────────────────────────────────────────────

        // Once the change's pass is over, before anything boards: each node that started or stopped being shown with a
        // name finds the node it pairs with, in the order they were met, so an outer pair is found before those nested
        // in it. Where every source is drawn is read before any destination's ancestors are written; then each
        // destination takes over from its source, in the same order, so a nested destination takes over inside an outer
        // one already where its own source is drawn.
        private static void MatchPairs(LayoutTransition transition)
        {
            try
            {
                for (int i = 0; i < s_changing.Count; i++)
                {
                    var state = s_changing[i];
                    if (!s_paired.Contains(state))
                        FindPartner(state, transition);
                }
                for (int i = 0; i < s_pairs.Count; i++)
                    TakeOver(s_pairs[i], transition);
            }
            finally
            {
                ClearKeys();
                s_pairs.Clear();
                s_paired.Clear();
            }
        }

        // A node the change started showing pairs with the one other node that held its name and id before the change,
        // and one the change stopped showing with the one other node that holds them after it. Neither may be a root
        // (the system does not drive a root's rect), both are on one root canvas (flights are ranked within its
        // sorting), neither is inside the other (drawn at the other's rect, it would move itself), and both are in view:
        // the one drawn before the change as it is drawn, the one drawn after it where it goes, so a ScrollTo in the same
        // change brings it into view. With several candidates, or none because the id is set on one side only, it pairs
        // with none and each plays its own effect; the mistake is said once for the name. The other changing too, it is a
        // hand-off (TakeOver). The other staying shown, it is this one's anchor (AnchorTo), if this one plays its own
        // effect in the change (its shown value set off by it) rather than riding what above it comes or goes, which
        // plays nothing of its own to grow or shrink by.
        private static void FindPartner(NodeState state, LayoutTransition transition)
        {
            bool shown = state.PassShown;
            string name = shown ? state.PassName : state.WasName;
            object id = shown ? state.PassId : state.WasId;
            if (state.Parent == null)
            {
                if (s_warnedRoot.Add(name))
                    Debug.LogWarning($"{state.Node.name} is the root of its layout, so its MatchName \"{name}\" pairs it with nothing: the system does not drive a root's rect, so roots never take over from one another, and it plays its own DisplayEffect. (Said once for the name.)", state.Node);
                return;
            }
            var canvas = RootCanvasOf(state);
            if (canvas == null || !InView(state, !shown)) return;

            NodeState partner = null;
            int count = 0;
            if (s_holders.TryGetValue(new MatchKey(name, id), out var holders))
            {
                var others = shown ? holders.Before : holders.After;
                for (int i = 0; i < others.Count; i++)
                {
                    var other = others[i];
                    if (other == state || other.Parent == null || s_paired.Contains(other) || RootCanvasOf(other) != canvas
                        || Nested(state, other) || !InView(other, shown))
                        continue;
                    partner = other;
                    count++;
                }
            }

            if (count > 1)
            {
                if (s_warnedTwins.Add(name))
                    Debug.LogWarning($"{count} nodes could pair with {state.Node.name} under MatchName \"{name}\" and MatchId {id ?? "none"}, so it pairs with none of them and each plays its own DisplayEffect. Give each a MatchId of its own. (Said once for the name.)", state.Node);
                return;
            }
            if (count == 0)
            {
                // The others held the name with an id where this has none, or the other way round: most likely an id
                // not given to one of them (a page opened without its item's id).
                byte opposite = shown ? id != null ? BeforeWithoutId : BeforeWithId : id != null ? AfterWithoutId : AfterWithId;
                if (s_nameSides.TryGetValue(name, out var sides) && (sides & opposite) != 0 && s_warnedOneSide.Add(name))
                {
                    string given = id != null ? "with MatchId " + id : "with no MatchId";
                    string theirs = id != null ? "have none" : "have one";
                    Debug.LogWarning($"MatchId set on one side only: {state.Node.name} is {(shown ? "shown" : "hidden")} under MatchName \"{name}\" {given}, while the nodes it could pair with {theirs}, so it pairs with none of them. (Said once for the name.)", state.Node);
                }
                return;
            }

            s_paired.Add(state);
            s_paired.Add(partner);
            if (partner.WasShown && partner.PassShown)
            {
                if (state.Shown.Moving && state.Shown.Transition == transition)
                    AnchorTo(state, partner, transition);
                return;
            }

            var source = shown ? partner : state;
            var destination = shown ? state : partner;

            // Where it is drawn as the passes before the change's update left it, as the in-view test read it: what they
            // put somewhere at once (an Offset a drag moved in the same frame, taken up by the fling letting go of it, or
            // a scroll put back just before) is not drawn there until the frame, so it and what it is inside are written
            // first. A follower is drawn at the node it follows, so that is the rect read for one, and how fast it grows.
            var drawn = source.Follows ?? source;
            WriteDown(drawn);
            drawn.RectTransform.GetWorldCorners(s_corners);
            var space = SpaceOf(drawn);
            s_pairs.Add(new Pair
            {
                Source = source,
                Destination = destination,
                BottomLeft = s_corners[0],
                TopRight = s_corners[2],
                Velocity = DrawnVelocity(source),
                SizeVelocity = space != null ? Vector2.Scale(drawn.Size.Velocity, UnitOf(space)) : Vector2.zero,
            });
        }

        // Writes a node where it is drawn, and before it each node it is inside, outermost first, on through the node
        // above each root, whose place that root's follows: a pass gives them their values, and only the frame draws
        // them there.
        private static void WriteDown(NodeState state)
        {
            for (var above = state; above != null; above = above.Parent ?? above.Above)
                s_above.Add(above);
            for (int i = s_above.Count - 1; i >= 0; i--)
                Write(s_above[i]);
            s_above.Clear();
        }

        // The root canvas a node draws in, through its parent (as a flight's is found, whatever canvas it has itself).
        private static Canvas RootCanvasOf(NodeState state)
        {
            var parent = state.RectTransform.parent;
            var canvas = parent != null ? parent.GetComponentInParent<Canvas>() : null;
            return canvas != null ? canvas.rootCanvas : null;
        }

        // Whether either node is inside the other.
        private static bool Nested(NodeState a, NodeState b) =>
            a.RectTransform.IsChildOf(b.RectTransform) || b.RectTransform.IsChildOf(a.RectTransform);

        // Whether any of a node is in view of the scroll containers it is inside (a clip of the user's is not asked): as
        // it is drawn now (its presentation, slide and all, and each scroll as drawn), or not `drawn`, where it goes (its
        // targets, and each scroll where it is springing to). Up its layout parents, as TryScrollRect goes, its rect is
        // taken into each one's content space; each scroll container must overlap it with what it shows, its whole rect
        // at its scroll, which then comes off on the way up. Flying, or inside a flight, it is drawn above every clip.
        private static bool InView(NodeState state, bool drawn)
        {
            if (state.Flight != null) return true;
            PoseOf(state, drawn, out var centre, out var size);
            Vector2 min = centre - size * 0.5f, max = centre + size * 0.5f;
            for (var parent = state.Parent; parent != null; parent = parent.Parent)
            {
                if (parent.Flight != null) return true;
                Vector2 shows;
                if (parent.Parent != null)
                    PoseOf(parent, drawn, out centre, out shows);
                else
                    shows = parent.RectTransform.rect.size;
                var scroll = parent.Scroll;
                if (scroll != null && scroll.Axis != ScrollAxis.None)
                {
                    var offset = !drawn && scroll.Phase == ScrollPhase.Springing ? scroll.Offset.Target : scroll.Offset.Value;
                    min -= offset;
                    max -= offset;
                    if (max.x <= 0f || max.y <= 0f || min.x >= shows.x || min.y >= shows.y)
                        return false;
                }
                if (parent.Parent == null) break;
                var corner = centre - shows * 0.5f;
                min += corner;
                max += corner;
            }
            return true;
        }

        // A node's centre and size in its parent's layout space, as drawn (its slide included) or where they go.
        private static void PoseOf(NodeState state, bool drawn, out Vector2 centre, out Vector2 size)
        {
            centre = drawn ? state.Position.Value + SlideOf(state) : state.Position.Target;
            size = Vector2.Max(drawn ? state.Size.Value : state.Size.Target, Vector2.zero);
        }

        // ── Taking over ──────────────────────────────────────────────────────────

        // The destination takes over from where the source is drawn, channel by channel, then both board the flight
        // layer, the destination directly above the source (turning back, each keeps the place it has there).
        private static void TakeOver(in Pair pair, LayoutTransition transition)
        {
            var source = pair.Source;
            var destination = pair.Destination;
            var parent = destination.Parent;

            // Its parent where it is drawn this frame: its ancestors written outermost first, on through the node above
            // each root, whose place that root's follows. A page shown in this change was put at its place in the pass
            // but is not drawn there yet, and is written at its away pose, brought up to date as it was placed; an outer
            // pair's destination is written where its source is drawn.
            WriteDown(parent);

            // Drawn at a scale of about nothing (a page two levels up appearing with Shrink 1, say), the parent's space
            // has no image of the source's rect: there is no pair, and each plays its own effect, as the pass set it to.
            var space = parent.RectTransform;
            if (Degenerate(space)) return;

            // Turning back: it follows the source, which took over from it, so it is drawn at the source's rect
            // already, part faded. Asked before Link, which stops it following. Either half growing out of or shrinking
            // into an anchor is taken over from where it is drawn now, and stops.
            bool reversal = destination.Follows == source;
            source.Anchor = null;
            destination.Anchor = null;
            Link(source, destination);

            // Position: where the source is drawn, in the parent's layout space with the parent's scroll put back on
            // (the destination is drawn moved back by it), moving as fast as the source is drawn moving less the parent,
            // plus the parent's scroll. So it is drawn exactly there, at that speed, and moves on to its own place on its
            // own spring for the change, after its delay, bowing out as any move does.
            CornersIn(pair.BottomLeft, pair.TopRight, space, out var centre, out var size);
            Vector2 local = space.InverseTransformVector(pair.Velocity - DrawnVelocity(parent));
            SetOffFrom(destination, destination.Position, centre + ScrolledBy(parent),
                new Vector2(local.x, -local.y) + ScrollVelocityOf(parent), transition);

            // Size likewise, from the source's drawn size over its own scale, which goes where it goes at once. It is
            // shown at once too: the pair plays in place of its DisplayEffect.
            float scale = destination.Scale.Target.x;
            if (scale < 1e-4f) scale = 1f;
            var unit = UnitOf(space);
            var growing = new Vector2(unit.x > 0f ? pair.SizeVelocity.x / unit.x : 0f, unit.y > 0f ? pair.SizeVelocity.y / unit.y : 0f);
            SetOffFrom(destination, destination.Size, size / scale, growing / scale, transition);
            Snap(destination.Scale, destination.Scale.Target, transition);
            Snap(destination.Shown, destination.Shown.Target, transition);

            // The cross-fade. The destination fades in from nothing, or, turning back, from where it is. Flying for
            // another pair, it is not drawn where it now sets off from (a header opened again from another row while it
            // still follows the last), so it fades in from nothing too. The source fades out beneath it; and the
            // source's shown value stops where it was going, since it is drawn at the destination's rect rather than at
            // its own away pose.
            if (!reversal)
                Snap(destination.Opacity, Vector2.zero, transition);
            Retarget(destination, destination.Opacity, new Vector2(OpacityTargetOf(destination), 0f), transition);
            FadeUnder(source, destination.Node.Animation, transition);
            Snap(source.Shown, source.Shown.Target, transition);

            // Flying already and not turning back (a detail card opened again from another tile while it is still
            // closing to the last), it would keep its place in the layer, below a source that boards now above
            // everything flying, which would cover it until the source had faded out: it lands, so that it boards again
            // directly above its source. Turning back, the two keep the places they have.
            if (!reversal && destination.Flight != null)
                Land(destination);
            BoardPair(source, destination);
        }

        // A node shown or hidden with one that stays shown under its name plays its effect with that one as its away
        // pose (Anchor, read again each time it is drawn): it grows out of it, or shrinks back into it, flying until its
        // effect has played. Turned back part way (shown again as it shrank into it, or hidden as it grew out of it),
        // it goes back from where it is drawn, as its effect turns back. Still following what took over from it in a
        // hand-off, it stops, and fades back in on the change from where its fade has got to.
        private static void AnchorTo(NodeState state, NodeState anchor, LayoutTransition transition)
        {
            if (state.Follows != null)
            {
                state.Follows = null;
                Retarget(state, state.Opacity, new Vector2(OpacityTargetOf(state), 0f), transition);
            }
            state.Anchor = anchor;
            ReadAnchor(state);
            BoardAnchored(state);
        }

        // Makes the source follow the destination. What followed the source follows the destination instead, so that a
        // follower never follows another (a chain: a card rebuilt again before it has landed), bar the destination
        // itself, which simply stops (a reversal: it takes over from what took over from it). The destination draws
        // itself from now on, whatever it followed.
        private static void Link(NodeState source, NodeState destination)
        {
            for (int i = 0; i < s_flights.Count; i++)
            {
                var flight = s_flights[i];
                if (flight.Follows == source)
                    flight.Follows = flight == destination ? null : destination;
            }
            destination.Follows = null;
            source.Follows = destination;
        }

        // Sets a spring off from `value` at `velocity` to where it goes, as Retarget sets any off for the change. There
        // already, it is at rest there, and carries no velocity on.
        private static void SetOffFrom(NodeState state, Spring spring, Vector2 value, Vector2 velocity, LayoutTransition transition)
        {
            spring.Value = value;
            spring.Velocity = velocity;
            Retarget(state, spring, spring.Target, transition);
            if (!spring.Moving)
                spring.Velocity = Vector2.zero;
        }

        // Puts a spring at `value` at once, at rest. What `transition` itself set it moving for has only been replaced by
        // its own takeover, so that change is not cut short by it; what another change set it moving for is let go of on
        // its way.
        private static void Snap(Spring spring, Vector2 value, LayoutTransition transition)
        {
            spring.Target = value;
            Stop(spring, spring.Transition == transition);
        }

        // The source fades out, from where it is, on the destination's spring without bouncing, once the destination is
        // well on its way in: after the destination's delay and half its duration, when it is about 82% faded in, so the
        // two together cover the rect throughout (never less than about 97.6%). On an animation with no duration, it goes
        // as the destination comes: at once, or once the delay is up.
        private static void FadeUnder(NodeState source, LayoutAnimation animation, LayoutTransition transition)
        {
            var fade = source.Opacity;
            fade.Target = Vector2.zero;
            if (!fade.Moving && fade.Value == Vector2.zero) return;
            if (animation.AtOnce)
            {
                Snap(fade, Vector2.zero, transition);
                return;
            }
            Spring.Parameters(animation, out fade.Omega, out _);
            fade.Zeta = 1f;
            fade.Delay = Mathf.Max(0f, animation.Delay) + Mathf.Max(0f, animation.Duration) * 0.5f;
            Hold(fade, transition);
        }

        // How fast a node's centre is drawn moving on screen, in world units a second: its velocity within its parent
        // (its slide's included), less its parent's scroll's, turned into world units by the parent, and so on up its
        // parents and on through the node above each root, whose place the root's follows. A root adds nothing of its
        // own, and a follower, drawn at the node it follows, moves as that one does. Ancestors that scale or rotate are
        // left out.
        private static Vector3 DrawnVelocity(NodeState state)
        {
            var velocity = Vector3.zero;
            for (var node = state; node != null;)
            {
                if (node.Follows != null)
                    return velocity + DrawnVelocity(node.Follows);
                var parent = node.Parent;
                if (parent == null)
                {
                    node = node.Above;
                    continue;
                }
                var v = node.Position.Velocity + SlideVelocityOf(node) - ScrollVelocityOf(parent);
                velocity += parent.RectTransform.TransformVector(new Vector3(v.x, -v.y, 0f));
                node = parent;
            }
            return velocity;
        }

        // How fast a node's scroll offset is moving, while it glides or springs (held by a press, it follows the press).
        private static Vector2 ScrollVelocityOf(NodeState state)
        {
            var scroll = state.Scroll;
            return scroll != null && scroll.Axis != ScrollAxis.None
                && (scroll.Phase == ScrollPhase.Gliding || scroll.Phase == ScrollPhase.Springing)
                ? scroll.Offset.Velocity
                : Vector2.zero;
        }

        // Whether a space is drawn at a scale of about nothing, so that nothing converts into it.
        private static bool Degenerate(Transform space)
        {
            var scale = space.lossyScale;
            return Mathf.Abs(scale.x) < 1e-4f || Mathf.Abs(scale.y) < 1e-4f;
        }

        // ── Following ────────────────────────────────────────────────────────────

        // A follower is drawn at the rect of the node it follows as that is drawn this frame (written already: what is
        // followed is never a follower, and followers are written once every tree has been), at a scale of 1, so its
        // content is laid out at its own size, pinned at its top left. Under a parent drawn at a scale of about nothing
        // (riding a page that shrinks away), it is left where it is, unseen with that page.
        private static void DrawOver(NodeState state)
        {
            var space = SpaceOf(state);
            if (space == null || Degenerate(space)) return;
            state.Follows.RectTransform.GetWorldCorners(s_corners);
            CornersIn(s_corners[0], s_corners[2], space, out var centre, out var size);
            WriteRect(state.RectTransform, centre, size, 1f);
        }

        // A destination flying ignores its parent groups, so it is drawn at its own opacity times where the opacity of
        // each node above it (on through the node above a root) is going: a steady partial opacity above it shows as it
        // flies, while a page fading in does not cut it, and landing, once those have come to rest there, changes
        // nothing as the groups reach it again.
        private static float OpacityAbove(NodeState state)
        {
            float opacity = 1f;
            for (var above = state.PassParent ?? state.Above; above != null; above = above.PassParent ?? above.Above)
                opacity *= Mathf.Clamp01(above.Opacity.Target.x);
            return opacity;
        }

        // ── Landing and ending ───────────────────────────────────────────────────

        // Whether a pair half's pair has landed: its destination's position, size and opacity, every follower's
        // opacity, and the opacity of each node above the destination (on through the node above a root) are at rest,
        // and nothing above a follower is still on its way out. Waiting for the destination's ancestors matters because
        // landing gives the destination its parent groups back, and a page still fading would cut its alpha at once.
        // Waiting for a follower's matters because, once it stops following, it is drawn in its parent again at its own
        // opacity: a chat's header, hidden with the page sliding away, would show in that page until it had gone (as
        // would an avatar inside a detail page still following its card). A pair half left without a partner lands once
        // it is at rest itself.
        private static bool PairAtRest(NodeState state)
        {
            var destination = state.Follows ?? state;
            if (destination.Position.Moving || destination.Size.Moving || destination.Opacity.Moving) return false;
            for (int i = 0; i < s_flights.Count; i++)
            {
                var flight = s_flights[i];
                if (flight.Follows != destination) continue;
                if (flight.Opacity.Moving || (flight.PassParent != null && flight.PassParent.PassLeaving)) return false;
            }
            for (var above = destination.PassParent ?? destination.Above; above != null; above = above.PassParent ?? above.Above)
            {
                if (above.Opacity.Moving) return false;
            }
            return true;
        }

        // A pair lands, whichever half found it at rest: what follows its destination stops, and all of it leaves the
        // flight layer.
        private static void LandPair(NodeState state)
        {
            var destination = state.Follows ?? state;
            Unfollow(destination, false);
            Land(destination);
        }

        // Ends the pairs a destination takes over in: each node following it stops (StopFollowing) and lands. Ended
        // `early` (the destination caught, dropped out or gone), a source on its way out stays where it is drawn to
        // finish its fade. Returns whether anything followed it.
        private static bool Unfollow(NodeState destination, bool early)
        {
            CollectFollowers(destination);
            bool any = s_followers.Count > 0;
            for (int i = 0; i < s_followers.Count; i++)
            {
                StopFollowing(s_followers[i], early);
                Land(s_followers[i]);
            }
            s_followers.Clear();
            return any;
        }

        // Catching a node first ends every pair whose destination is the node or is inside it, each cross-fade finished
        // at once: the destination at its opacity, its sources gone. Those flights land, and the node is then caught as
        // any node is. Held part-faded, a destination would stay so over its source, which is outside what is caught and
        // would fade out from under it (StopInside holds a nested one too). A node growing out of its anchor is caught
        // as any node is, its shown value held, so it stays where it is drawn between the two.
        private static void EndPairsInside(NodeState state)
        {
            var caught = state.RectTransform;
            for (int i = 0; i < s_flights.Count; i++)
            {
                var flight = s_flights[i];
                if (flight.Flight.PairHalf && flight.Follows == null && flight.RectTransform.IsChildOf(caught))
                    s_ending.Add(flight);
            }
            for (int i = 0; i < s_ending.Count; i++)
            {
                var destination = s_ending[i];
                PutAt(destination.Opacity, destination.Opacity.Target);
                for (int j = 0; j < s_flights.Count; j++)
                {
                    if (s_flights[j].Follows == destination)
                        PutAt(s_flights[j].Opacity, Vector2.zero);
                }
                Unfollow(destination, true);
                Land(destination);
            }
            s_ending.Clear();
        }

        // A node disabled or destroyed ends the pair it is in, as if it had landed. Following, it stops, and the node it
        // followed leaves the flight layer unless others still follow it; followed, what follows it stops. This runs in
        // OnDisable, so what leaves the layer only stands down (LayoutSystem.Flight.cs).
        private static void Unpair(NodeState state)
        {
            var destination = state.Follows;
            if (destination != null)
            {
                state.Follows = null;
                if (!HasFollowers(destination))
                    StandDown(destination);
                return;
            }
            CollectFollowers(state);
            for (int i = 0; i < s_followers.Count; i++)
            {
                StopFollowing(s_followers[i], true);
                StandDown(s_followers[i]);
            }
            s_followers.Clear();
        }

        // A node stops following the one that took over from it. Still shown, it is back in its place at once, its
        // opacity at its own (its other springs never moved). Ended early while it is on its way out, it stays where
        // it is drawn and finishes its fade there: its position and size are put there (its size over its scale, which
        // it is drawn at again), Place leaves both and tells its content nothing (Thrown), so that stays laid out at
        // its own size as it was drawn while following, and it is drawn without its effect's slide and scale
        // (EffectOff; its shown value was stopped as it began to follow). Both clear as it finishes leaving, or is shown
        // again. Landing, it has faded to nothing, so where it is drawn no longer matters; hidden with something above it
        // rather than by its own Display, it goes with that.
        private static void StopFollowing(NodeState state, bool early)
        {
            state.Follows = null;
            if (state.PassShown)
            {
                PutAt(state.Opacity, new Vector2(OpacityTargetOf(state), 0f));
                return;
            }
            if (!early || !state.Leaving) return;
            var space = SpaceOf(state);
            if (space != null && !Degenerate(space))
            {
                DrawnIn(state.RectTransform, space, out var centre, out var size);
                PutAt(state.Position, centre + ScrolledBy(state.Parent));
                float scale = state.Scale.Value.x;
                PutAt(state.Size, scale > 1e-4f ? size / scale : size);
            }
            state.Thrown = true;
            state.EffectOff = true;
        }

        // A follower shown again that takes over from nothing (none in view, several, an id on one side only, or shown
        // outside Animate, where no pair forms) stops following there, and leaves the flight layer: it comes back as any
        // node shown in that change does, drawn at its own place in what it is inside, where its other springs carried
        // on unseen (a chat's header, riding its page as that turns back or comes in again, rather than left fading at
        // the row it flew home to). Its opacity, which Place sends to nothing while it follows, goes back to its own on
        // the change from wherever its fade had got to; at once outside one, or when it was not drawn. The node it
        // followed carries on by itself, and lands once it has come to rest.
        private static void ShowAgain(NodeState state, LayoutTransition transition)
        {
            state.Follows = null;
            var opacity = new Vector2(OpacityTargetOf(state), 0f);
            if (transition != null && !state.PassUnseen)
                Retarget(state, state.Opacity, opacity, transition);
            else
                PutAt(state.Opacity, opacity);
            Land(state);
        }

        // The nodes following a destination, into s_followers, found by looking through the flight layer (which is
        // short): a follower flies until its pair lands or ends.
        private static void CollectFollowers(NodeState destination)
        {
            for (int i = 0; i < s_flights.Count; i++)
            {
                if (s_flights[i].Follows == destination)
                    s_followers.Add(s_flights[i]);
            }
        }

        private static bool HasFollowers(NodeState destination)
        {
            for (int i = 0; i < s_flights.Count; i++)
            {
                if (s_flights[i].Follows == destination)
                    return true;
            }
            return false;
        }
    }
}
