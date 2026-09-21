using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TimboJimbo.UI.Layout.Samples.Layout
{
    /// <summary>
    /// Drives the showcase scene's buttons: rows added to and removed from the scrolling list, the sizing
    /// row flipped between the two directions, its gap changed, and one block's Offset pulsed to show a
    /// layout-additive move that leaves its siblings alone. Everything else in the scene is static nodes.
    /// </summary>
    public sealed class LayoutShowcase : MonoBehaviour
    {
        [SerializeField] private LayoutNode _sizingRow;
        [SerializeField] private LayoutNode _list;
        [SerializeField] private LayoutNode _rowTemplate;
        [SerializeField] private LayoutNode _pulseTarget;
        [SerializeField] private TMP_Text _status;
        [SerializeField, Min(0)] private int _initialRows = 12;

        private readonly List<LayoutNode> _rows = new();
        private Coroutine _pulse;

        private void Start()
        {
            if (_rowTemplate != null)
                _rowTemplate.gameObject.SetActive(false);
            for (int i = 0; i < _initialRows; i++)
                AddRow();
            UpdateStatus();
        }

        /// <summary>Clones the template row at the end of the list; the list reflows before the next render.</summary>
        public void AddRow()
        {
            if (_list == null || _rowTemplate == null) return;
            var row = Instantiate(_rowTemplate, _list.transform);
            row.gameObject.SetActive(true);
            row.name = $"Row {_rows.Count + 1}";
            var label = row.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = $"Row {_rows.Count + 1}";
            _rows.Add(row);
            UpdateStatus();
        }

        /// <summary>Destroys the last row; the list notices the child change and reflows.</summary>
        public void RemoveRow()
        {
            if (_rows.Count == 0) return;
            var row = _rows[_rows.Count - 1];
            _rows.RemoveAt(_rows.Count - 1);
            Destroy(row.gameObject);
            UpdateStatus();
        }

        /// <summary>Lays the sizing row out along the other axis; the same sizing modes apply to the other dimension.</summary>
        public void ToggleDirection()
        {
            if (_sizingRow == null) return;
            _sizingRow.Direction = _sizingRow.Direction == LayoutDirection.LeftToRight
                ? LayoutDirection.TopToBottom
                : LayoutDirection.LeftToRight;
            UpdateStatus();
        }

        public void ChangeGap(float delta)
        {
            if (_sizingRow == null) return;
            _sizingRow.Gap = Mathf.Max(0f, _sizingRow.Gap + delta);
            UpdateStatus();
        }

        /// <summary>Bounces one block up and back through <see cref="LayoutNode.Offset"/>: no layout pass runs and its siblings do not move.</summary>
        public void PulseOffset()
        {
            if (_pulseTarget == null) return;
            if (_pulse != null)
                StopCoroutine(_pulse);
            _pulse = StartCoroutine(Pulse());
        }

        private IEnumerator Pulse()
        {
            const float duration = 0.8f;
            const float height = 24f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float s = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
                _pulseTarget.Offset = new Vector2(0f, height * s);
                yield return null;
            }
            _pulseTarget.Offset = Vector2.zero;
            _pulse = null;
        }

        private void UpdateStatus()
        {
            if (_status == null || _sizingRow == null) return;
            _status.text = $"{_rows.Count} rows in the list.  Sizing row: {_sizingRow.Direction}, gap {_sizingRow.Gap:0}.";
        }
    }
}
