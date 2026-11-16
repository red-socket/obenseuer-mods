using System;
using System.Collections.Generic;
using UnityEngine;

namespace TidyInventory
{
    // The game's own notification with our title (spec §2). Once() shows a tip only the first time per session and only
    // with General.ShowHints; Notify() drops a note identical to the last one within 3 real seconds (a held or repeated
    // key); Post() always shows (key collision notes).
    internal static class Hints
    {
        internal const string Title = "Tidy Inventory";
        private const float RepeatSeconds = 3f;

        private static readonly HashSet<string> Shown = new HashSet<string>();
        private static string _last;
        private static float _lastAt = -100f;

        internal static void Once(string id, string text)
        {
            if (Cfg.ShowHints == null || !Cfg.ShowHints.Value) return;
            if (!Shown.Add(id)) return;
            Notify(text);
        }

        internal static void Notify(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            float now = Time.unscaledTime;
            if (text == _last && now - _lastAt < RepeatSeconds) return;
            _last = text;
            _lastAt = now;
            Post(text);
        }

        internal static void Post(string text)
        {
            try
            {
                if (Notifications.instance != null) Notifications.instance.CreateNotification(Title, text);
            }
            catch (Exception e) { Log.Exception("notification", e); }
        }
    }
}
