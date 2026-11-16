using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;
using UiButton = UnityEngine.UI.Button;

namespace FewerClicks
{
    // Clones one of the game's own buttons (look, hover and press sounds, controller highlight) with our action
    // (spec §2 P1). Returns null when the source has an unexpected shape; the feature then works by hotkey only.
    internal static class UiClone
    {
        internal static GameObject CloneButton(GameObject src, Transform parent, string name, string label, UnityAction onClick, string warnKey)
        {
            if (src == null || parent == null) return null;
            var go = Object.Instantiate(src, parent);
            go.name = name;
            // The clone's Relay is a SavableScript with the source's save GUID: remove it at once, before any save can
            // collect it. The button's persistent listener went through it to the vanilla action; replaced below.
            var relay = go.GetComponent<Relay>();
            if (relay != null) Object.DestroyImmediate(relay);
            var btn = go.GetComponent<UiButton>();
            if (btn == null || go.GetComponent<DefaultUIButton>() == null)
            {
                Object.Destroy(go);
                Log.WarnOnce(warnKey, $"The game's {src.name} button has an unexpected shape; {name} is not added and the hotkey still works.");
                return null;
            }
            btn.onClick = new UiButton.ButtonClickedEvent();
            btn.onClick.AddListener(onClick);
            // The source shows the pad button that runs its own action (X for Fill, for example). Ours has no pad
            // button of its own, so drop the glyph rather than show a button that would do something else.
            go.GetComponent<DefaultUIButton>().ChangeControllerKey(KeyCode.None);
            var glyph = FindChild(go.transform, "Controller");
            if (glyph != null) glyph.gameObject.SetActive(false);
            var text = go.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.enableAutoSizing = true;
                text.fontSizeMin = 10f;
                text.fontSizeMax = 15f;
                text.text = label;
            }
            go.SetActive(true);
            return go;
        }

        private static Transform FindChild(Transform t, string name)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.name == name) return c;
                var deeper = FindChild(c, name);
                if (deeper != null) return deeper;
            }
            return null;
        }

        internal static void SetLabel(GameObject go, string label)
        {
            if (go == null) return;
            var text = go.GetComponentInChildren<TMP_Text>(true);
            if (text != null && text.text != label) text.text = label;
        }
    }
}
