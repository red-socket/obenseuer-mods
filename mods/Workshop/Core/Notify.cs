using System;
using UnityEngine;

namespace Workshop
{
    // The game's own notification with our title (spec §2). Show() drops a note identical to the last one within 3 real
    // seconds (a held or repeated key or click); Post() always shows (key collision notes).
    internal static class Notify
    {
        internal const string Title = "Workshop";
        private const float RepeatSeconds = 3f;

        private static string _last;
        private static float _lastAt = -100f;

        internal static void Show(string text)
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
