using System;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// How a node looks while it enters or leaves, the way keyframes on <c>::view-transition-new</c> and
    /// <c>::view-transition-old</c> do on the web. Put an implementation on the node's object and the layout
    /// engine hands it the moments; what happens in them (a slide, a scale, a fade, anything a sequence can do)
    /// is the implementation's. Without one, an entering or leaving subtree fades. <see cref="ViewTransitionSequences"/>
    /// implements it with two authored sequences when the Sequencer package is installed.
    /// </summary>
    public interface IViewTransitionAnimator
    {
        /// <summary>
        /// The node has just had its first layout inside <paramref name="transition"/> and sits at its final rect.
        /// Play the enter effect and call <paramref name="done"/> when it has finished; the transition's
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

        /// <summary>The transition was skipped: end the effect at once. <c>done</c> need not be called after this.</summary>
        void Skip();
    }
}
