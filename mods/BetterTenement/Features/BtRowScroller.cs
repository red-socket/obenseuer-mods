using UnityEngine;
using UnityEngine.EventSystems;

namespace BetterTenement
{
    // Row scroller for the storage slot grid (spec §3.3.3). Not a ScrollRect: it implements no drag interfaces (a drag
    // that starts on an empty slot behaves as in vanilla) and only ever moves the content by whole rows, so there are
    // no half-visible rows and the hatched placeholders behind stay aligned with the grid pitch.
    // UI-only component, added at runtime to a scene object; never saved.
    public sealed class BtRowScroller : MonoBehaviour, IScrollHandler
    {
        internal RectTransform Content;
        internal float Pitch = 69f;
        internal int VisibleRows = 11;
        internal int Rows;
        internal int FirstRow;

        private float _accum;

        internal int MaxFirstRow => Mathf.Max(0, Rows - VisibleRows);
        internal int Pages => Mathf.Max(1, (Rows + VisibleRows - 1) / VisibleRows);
        internal int Page => FirstRow >= MaxFirstRow && Rows > VisibleRows ? Pages : FirstRow / VisibleRows + 1;

        internal void SetRow(int row)
        {
            FirstRow = Mathf.Clamp(row, 0, MaxFirstRow);
            if (Content != null) Content.anchoredPosition = new Vector2(0f, FirstRow * Pitch);
            Chests.RefreshHeader();
        }

        internal void Step(int rows) => SetRow(FirstRow + rows);

        internal void PageStep(int pages)
        {
            if (Cfg.Mode.Value == PanelMode.Pages)
            {
                int page = Mathf.Clamp(Page - 1 + pages, 0, Pages - 1);
                SetRow(page * VisibleRows);
            }
            else SetRow(FirstRow + pages * VisibleRows);
        }

        // Wheel notches. Input modules report different units per notch (1, 6, 120...), so any event of at least one
        // unit counts as one notch; smaller deltas (touchpads) accumulate.
        public void OnScroll(PointerEventData e)
        {
            if (!enabled || e == null) return;
            float d = e.scrollDelta.y;
            if (Mathf.Abs(d) >= 1f) d = Mathf.Sign(d);
            _accum += d;
            while (_accum >= 1f) { _accum -= 1f; Notch(-1); }
            while (_accum <= -1f) { _accum += 1f; Notch(1); }
        }

        private void Notch(int direction)
        {
            if (Cfg.Mode.Value == PanelMode.Pages) PageStep(direction);
            else Step(direction * Mathf.Clamp(Cfg.RowsPerWheelNotch.Value, 1, 5));
        }
    }

    // Wheel catcher behind the masked viewport (fix for the C2b padding bands). The viewport's RectMask2D padding also
    // rejects raycasts in the top and bottom bands, so the viewport's own wheel Image misses there; this unmasked,
    // transparent sibling covers the same rect and hands wheel events to the scroller. No click or drag handlers.
    // UI-only component, added at runtime to a scene object; never saved.
    public sealed class BtWheelForward : MonoBehaviour, IScrollHandler
    {
        internal BtRowScroller Target;

        public void OnScroll(PointerEventData e)
        {
            if (Target != null) Target.OnScroll(e);
        }
    }
}
