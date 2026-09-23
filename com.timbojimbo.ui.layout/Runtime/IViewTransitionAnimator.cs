using System;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// How a node looks while it enters or leaves, the way keyframes on <c>::view-transition-new</c> and
    /// <c>::view-transition-old</c> do on the web. Put an implementation on the node's object and the layout
    /// engine hands it the moments; what happens in them (a slide, a scale, a fade, anything a sequence can do)
    /// is the implementation's. Without one, an entering or leaving subtree fades inside a view transition.
    /// <see cref="ViewTransitionSequences"/> implements it with authored sequences when the Sequencer package is
    /// installed. The transition passed in says how long the moves take (<see cref="ViewTransition.TransitionFor"/>)
    /// and what kind of change it is (<see cref="ViewTransition.HasType"/>); it is null outside a view transition.
    /// Advance the effect by <see cref="LayoutSystem.DeltaTime"/> to keep to the moves' clock.
    /// </summary>
    public interface IViewTransitionAnimator
    {
        /// <summary>
        /// The node has just appeared and sits at its final rect: it had its first layout inside
        /// <paramref name="transition"/>, or, with a null transition, outside one under a node that was already
        /// shown. Play the enter effect and call <paramref name="done"/> when it has finished; the transition's
        /// <see cref="ViewTransition.Finished"/> waits for it.
        /// </summary>
        void Enter(ViewTransition transition, Action done);

        /// <summary>
        /// <see cref="LayoutSystem.Exit"/> is taking the node out. The tree has reflowed without it and the node
        /// is still drawn where it was. Play the exit effect and call <paramref name="done"/> when it has gone;
        /// the node is then deactivated, or the exit's callback runs. <paramref name="transition"/> is null
        /// when the exit happens outside a view transition.
        /// </summary>
        void Exit(ViewTransition transition, Action done);

        /// <summary>
        /// End the effect at once: the transition was skipped, or <see cref="LayoutSystem.Show"/> called a leaving
        /// node back. <c>done</c> need not be called after this.
        /// </summary>
        void Skip();
    }
}
