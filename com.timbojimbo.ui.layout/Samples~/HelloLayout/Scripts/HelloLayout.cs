using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TimboJimbo.UI.Layout.Samples.HelloLayout
{
    /// <summary>
    /// The README's first recipes in one scene. Every button makes an ordinary change inside
    /// <see cref="LayoutSystem.StartViewTransition(System.Action, LayoutTransition?, string[])"/>, and everything the
    /// change moved glides there: a row added or removed, the list turned on its side, the highlight moved to
    /// another tab. Take the wrapper away and the same changes still lay out; they just happen at once.
    /// </summary>
    public sealed class HelloLayout : MonoBehaviour
    {
        [SerializeField] private LayoutNode _list;
        [SerializeField] private LayoutNode _rowTemplate;
        [SerializeField] private LayoutNode _highlight;
        [SerializeField] private List<LayoutNode> _tabs = new();
        [SerializeField] private Color[] _colours =
        {
            new(0.36f, 0.55f, 1f), new(0.95f, 0.66f, 0.23f), new(0.37f, 0.82f, 0.61f), new(1f, 0.42f, 0.42f), new(0.71f, 0.48f, 1f),
        };

        private int _added;

        public void Setup(LayoutNode list, LayoutNode rowTemplate, LayoutNode highlight, List<LayoutNode> tabs)
        {
            _list = list;
            _rowTemplate = rowTemplate;
            _highlight = highlight;
            _tabs = tabs;
        }

        /// <summary>A new row fades in at the top and the others make room.</summary>
        public void AddRow()
        {
            LayoutSystem.StartViewTransition(() =>
            {
                var row = Instantiate(_rowTemplate, _list.transform);
                row.GetComponent<Image>().color = _colours[_added++ % _colours.Length];
                row.transform.SetAsFirstSibling();
                row.gameObject.SetActive(true);
            });
        }

        /// <summary>The last row fades out while the list closes up, then it is destroyed.</summary>
        public void RemoveRow()
        {
            for (int i = _list.transform.childCount - 1; i >= 0; i--)
            {
                if (!_list.transform.GetChild(i).TryGetComponent<LayoutNode>(out var row) || !row.Shown) continue;
                LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(row, () => Destroy(row.gameObject)));
                return;
            }
        }

        /// <summary>The list turns on its side and every row travels to its new place.</summary>
        public void FlipDirection()
        {
            LayoutSystem.StartViewTransition(() =>
                _list.Direction = _list.Direction == LayoutDirection.TopToBottom ? LayoutDirection.LeftToRight : LayoutDirection.TopToBottom);
        }

        /// <summary>The highlight moves under another tab: the same object under a new parent, so it flies across.</summary>
        public void SelectTab(int index)
        {
            LayoutSystem.StartViewTransition(() =>
            {
                _highlight.transform.SetParent(_tabs[index].transform, false);
                _highlight.transform.SetAsFirstSibling();   // under the label
            });
        }
    }
}
