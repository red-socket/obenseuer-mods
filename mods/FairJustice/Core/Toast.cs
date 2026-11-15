using System;
using System.Collections.Generic;
using UnityEngine;

namespace FairJustice
{
    // The game's own notification (the same call Better Tenement uses), null-safe, at most once per key per 5 s.
    // Gated by Confiscation.Notify. Never used for the sentence panel.
    internal static class Toast
    {
        private static readonly Dictionary<string, float> Last = new Dictionary<string, float>();

        internal static void Show(string key, string title, string text)
        {
            try
            {
                if (!Cfg.Notify.Value || Notifications.instance == null) return;
                float now = Time.unscaledTime;
                if (Last.TryGetValue(key, out float t) && now - t < 5f) return;
                Last[key] = now;
                Notifications.instance.CreateNotification(title, text);
            }
            catch (Exception e) { Log.Exception("notification", e); }
        }
    }
}
