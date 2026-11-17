using System.Collections.Generic;
using UnityEngine;

namespace QuestHelper
{
    // Quest Helper's own notes (D15), drawn with IMGUI at the top right, Save Guard's pattern, below any Save Guard toast. Never
    // Notifications.CreateNotification: game notifications are saved and replayed (GUID "Notifications"). The same text
    // within 4 real seconds is dropped. Never saved.
    internal static class Toast
    {
        private sealed class Entry { internal string Text; internal float Duration; internal float Until = -1f; }

        private const float DedupeSeconds = 4f;
        private static readonly List<Entry> Active = new List<Entry>();
        private static readonly Dictionary<string, float> LastShown = new Dictionary<string, float>();

        internal static bool Any => Active.Count > 0;

        internal static void Show(string text, float seconds = 7f)
        {
            if (string.IsNullOrEmpty(text)) return;
            float now = Time.realtimeSinceStartup;
            if (LastShown.TryGetValue(text, out var at) && now - at < DedupeSeconds) return;
            LastShown[text] = now;
            if (Active.Count >= 5) Active.RemoveAt(0);
            Active.Add(new Entry { Text = text, Duration = seconds });
        }

        internal static void Draw(Vector2 screen)
        {
            if (Active.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            float y = 24f, w = 520f;
            // Below Save Guard's toasts, which use the same column (same skin and width).
            foreach (var text in SaveGuardBridge.VisibleToasts())
                y += Skin.ToastBox.CalcHeight(new GUIContent(text), w) + 8f;
            for (int i = 0; i < Active.Count; i++)
            {
                var t = Active[i];
                if (t.Until < 0f) t.Until = now + t.Duration;   // the timer starts when the toast is first drawn
                if (now > t.Until) { Active.RemoveAt(i--); continue; }
                var content = new GUIContent("<b>Quest Helper</b>\n" + Skin.Esc(t.Text));
                float h = Skin.ToastBox.CalcHeight(content, w);
                GUI.Box(new Rect(screen.x - w - 24f, y, w, h), content, Skin.ToastBox);
                y += h + 8f;
            }
        }
    }
}
