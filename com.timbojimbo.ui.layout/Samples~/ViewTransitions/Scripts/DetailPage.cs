using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TimboJimbo.UI.Layout.Samples.ViewTransitions
{
    /// <summary>
    /// The page a card opens into, spawned from a prefab for the transition and destroyed on the way back. Its
    /// background persists, its body is named like the card's body and its avatar and title like the card's parts,
    /// and <see cref="Bind"/> gives it the item's scope so all of those pair with the card that opened it.
    /// </summary>
    public sealed class DetailPage : MonoBehaviour
    {
        [SerializeField] private LayoutNode _node;
        [SerializeField] private Image _avatar;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private Button _back;
        [SerializeField] private Button _surface;

        public LayoutNode Node => _node;
        public Button BackButton => _back;
        /// <summary>The whole page as a button, so clicking it goes back too.</summary>
        public Button SurfaceButton => _surface;

        public void Setup(LayoutNode node, Image avatar, TMP_Text title, Button back, Button surface)
        {
            _node = node;
            _avatar = avatar;
            _title = title;
            _back = back;
            _surface = surface;
        }

        public void Bind(string id, string title, Color colour)
        {
            _node.ViewTransitionScope = id;
            _avatar.color = colour;
            _title.text = title;
        }
    }
}
