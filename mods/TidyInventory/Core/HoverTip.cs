using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TidyInventory
{
    // A hover tooltip for our cloned buttons through the game's own ToolTip (SRC/ToolTip.cs:135, :152). Not the game's
    // ToolTipInfo: its serialized SoundInfo would be missing on a component added at runtime. Hides on exit and disable.
    internal sealed class HoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal string Title;
        internal string Text;
        private bool _shown;

        public void OnPointerEnter(PointerEventData eventData)
        {
            try
            {
                if (ToolTip.instance == null || string.IsNullOrEmpty(Text)) return;
                ToolTip.instance.Activate(Title ?? "", Text);
                _shown = true;
            }
            catch (Exception e) { Log.Exception("button tooltip", e); }
        }

        public void OnPointerExit(PointerEventData eventData) => Hide();

        private void OnDisable() => Hide();

        private void Hide()
        {
            if (!_shown) return;
            _shown = false;
            try { if (ToolTip.instance != null) ToolTip.instance.Deactivate(); }
            catch (Exception e) { Log.Exception("button tooltip", e); }
        }
    }
}
