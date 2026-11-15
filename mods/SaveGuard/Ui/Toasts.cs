using System.Collections.Generic;
using UnityEngine;

namespace SaveGuard
{
    // Save Guard's own messages, drawn with IMGUI at the top right. Never Notifications.CreateNotification:
    // Notifications is saved into Globals.tnmt (Notifications.cs:36-39), so a toast there would end up in the save.
    internal static class Toasts
    {
        private sealed class Toast { internal string Text; internal float Duration; internal float Until = -1f; }

        private static readonly List<Toast> Active = new List<Toast>();
        private static readonly List<string> ForMenu = new List<string>();

        internal static void Show(string text, float seconds = 6f)
        {
            if (Active.Count >= 6) Active.RemoveAt(0);
            Active.Add(new Toast { Text = text, Duration = seconds });
        }

        // Startup messages wait for the main menu (the intro screens come first).
        internal static void ShowOnMenu(string text) => ForMenu.Add(text);

        internal static void OnMainMenu()
        {
            foreach (var t in ForMenu) Show(t, 10f);
            ForMenu.Clear();
        }

        internal static void OnGUI(Vector2 screen)
        {
            if (Active.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            float y = 24f, w = 520f;
            for (int i = 0; i < Active.Count; i++)
            {
                var t = Active[i];
                if (t.Until < 0f) t.Until = now + t.Duration;   // the timer starts when the toast is first drawn
                if (now > t.Until) { Active.RemoveAt(i--); continue; }
                var content = new GUIContent(t.Text);
                float h = Skin.ToastBox.CalcHeight(content, w);
                GUI.Box(new Rect(screen.x - w - 24f, y, w, h), content, Skin.ToastBox);
                y += h + 8f;
            }
        }
    }
}
