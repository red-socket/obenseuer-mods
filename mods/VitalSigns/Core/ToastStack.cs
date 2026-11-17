using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace VitalSigns
{
    // Vital Signs' notes (spec §5.10): IMGUI at the top right, house skin, real time, stacked below Save Guard's and
    // Quest Helper's toasts. Never Notifications.CreateNotification: game notifications are saved and replayed. At most 3
    // of ours on screen (the rest wait their turn); the same text within 4 s is dropped; at most 4 a real minute, the
    // excess folded into one "+N more" line. A death note skips the rate limit. Never saved.
    internal static class ToastStack
    {
        private sealed class Entry { internal string Text; internal float Duration; internal float Until = -1f; internal bool Fold; }

        private const float DedupeSeconds = 4f, Width = 520f, Gap = 8f, Top = 24f;
        private const int OnScreen = 3, PerMinute = 4;
        private const string SaveGuardGuid = "redsocket.obenseuer.saveguard", QuestHelperGuid = "redsocket.obenseuer.questhelper";

        private static readonly List<Entry> Active = new List<Entry>();
        private static readonly Dictionary<string, float> LastShown = new Dictionary<string, float>();
        private static readonly List<float> Shown = new List<float>();
        private static Entry _fold;
        private static int _folded;

        internal static bool Any => Active.Count > 0;

        internal static void Show(string text, float seconds, bool urgent = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            float now = Time.realtimeSinceStartup;
            if (LastShown.TryGetValue(text, out var at) && now - at < DedupeSeconds) return;
            if (LastShown.Count > 64)
            {
                var old = new List<string>();
                foreach (var kv in LastShown) if (now - kv.Value >= DedupeSeconds) old.Add(kv.Key);
                foreach (var k in old) LastShown.Remove(k);
            }
            LastShown[text] = now;
            Shown.RemoveAll(t => now - t > 60f);
            if (!urgent && Shown.Count >= PerMinute)
            {
                _folded++;
                string fold = Strings.ToastFold(_folded);
                if (_fold != null && Active.Contains(_fold) && _fold.Until < 0f) _fold.Text = fold;
                else
                {
                    _fold = new Entry { Text = fold, Duration = seconds, Fold = true };
                    Active.Add(_fold);
                }
                return;
            }
            Shown.Add(now);
            if (Active.Count >= 12) Active.RemoveAt(0);
            Active.Add(new Entry { Text = text, Duration = seconds });
        }

        internal static void Draw(Vector2 screen)
        {
            if (Active.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            float y = StartY();
            int drawn = 0;
            for (int i = 0; i < Active.Count && drawn < OnScreen; i++)
            {
                var t = Active[i];
                if (t.Until < 0f) t.Until = now + t.Duration;   // the timer starts when the toast is first drawn
                if (now > t.Until)
                {
                    if (t == _fold) { _fold = null; _folded = 0; }
                    Active.RemoveAt(i--);
                    continue;
                }
                var content = new GUIContent("<b>" + Strings.ModName + "</b>\n" + Skin.Esc(t.Text));
                float h = Skin.ToastBox.CalcHeight(content, Width);
                GUI.Box(new Rect(screen.x - Width - 24f, y, Width, h), content, Skin.ToastBox);
                y += h + Gap;
                drawn++;
            }
        }

        // ---- Below Save Guard and Quest Helper (spec §5.10) ----

        private sealed class Bridge
        {
            internal readonly string Guid, TypeName, InnerName, Prefix;
            internal readonly int Max;
            internal bool Resolved, Broken;
            internal FieldInfo Active, Text, Until;
            internal float UiScale = 1f;

            internal Bridge(string guid, string typeName, string innerName, string prefix, int max)
            {
                Guid = guid; TypeName = typeName; InnerName = innerName; Prefix = prefix; Max = max;
            }
        }

        private static readonly Bridge SaveGuard = new Bridge(SaveGuardGuid, "SaveGuard.Toasts", "Toast", null, int.MaxValue);
        private static readonly Bridge QuestHelper = new Bridge(QuestHelperGuid, "QuestHelper.Toast", "Entry", "<b>Quest Helper</b>\n", 5);

        // Each column's bottom in pixels, converted to our units. Save Guard starts at its own 24; Quest Helper starts
        // below Save Guard's toasts measured in its own units (it doesn't scale them), so its bottom is its own sum.
        private static float StartY()
        {
            float ours = Skin.PixelScale(Skin.Scale);
            float sgUnits = Column(SaveGuard, 0f);
            float sgPx = sgUnits > 0f ? (Top + sgUnits) * Skin.PixelScale(SaveGuard.UiScale) : 0f;
            float qhUnits = Column(QuestHelper, 0f);
            float qhPx = 0f;
            // Quest Helper measures Save Guard's toasts with the same skin and width, in its own units.
            if (qhUnits > 0f) qhPx = (Top + sgUnits + qhUnits) * Skin.PixelScale(QuestHelper.UiScale);
            float bottom = Math.Max(sgPx, qhPx) / ours;
            return Math.Max(Top, bottom);
        }

        // Sum of (height + gap) of a mod's visible toasts in its own units (same skin and width as ours).
        private static float Column(Bridge b, float acc)
        {
            if (!Resolve(b)) return acc;
            IList list;
            try { list = b.Active.GetValue(null) as IList; }
            catch (Exception) { return acc; }
            if (list == null) return acc;
            float now = Time.realtimeSinceStartup;
            int n = 0;
            for (int i = 0; i < list.Count && n < b.Max; i++)
            {
                try
                {
                    var t = list[i];
                    float until = (float)b.Until.GetValue(t);
                    if (until >= 0f && now > until) continue;
                    var text = b.Text.GetValue(t) as string;
                    if (text == null) continue;
                    string shown = b.Prefix != null ? b.Prefix + Skin.Esc(text) : text;
                    acc += Skin.ToastBox.CalcHeight(new GUIContent(shown), Width) + Gap;
                    n++;
                }
                catch (Exception) { }
            }
            return acc;
        }

        private static bool Resolve(Bridge b)
        {
            if (b.Broken) return false;
            if (b.Resolved) return true;
            b.Resolved = true;
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(b.Guid, out var info) || info == null) { b.Broken = true; return false; }
                var type = AccessTools.TypeByName(b.TypeName);
                var inner = type != null ? AccessTools.Inner(type, b.InnerName) : null;
                b.Active = type != null ? AccessTools.Field(type, "Active") : null;
                b.Text = inner != null ? AccessTools.Field(inner, "Text") : null;
                b.Until = inner != null ? AccessTools.Field(inner, "Until") : null;
                if (b.Active == null || b.Text == null || b.Until == null)
                {
                    b.Broken = true;
                    Log.WarnOnce("toast bridge " + b.Guid, $"Ui: {b.TypeName} has changed; our notes may overlap its notes.");
                    return false;
                }
                b.UiScale = ReadUiScale(info);
                return true;
            }
            catch (Exception e)
            {
                b.Broken = true;
                Log.WarnOnce("toast bridge " + b.Guid, $"Ui: could not read {b.TypeName} ({e.Message}); our notes may overlap its notes.");
                return false;
            }
        }

        // Quest Helper names it QuestDoctor.UiScale; Save Guard names it UI.Scale.
        private static float ReadUiScale(BepInEx.PluginInfo info)
        {
            try
            {
                var cfg = info.Instance != null ? info.Instance.Config : null;
                if (cfg == null) return 1f;
                foreach (var kv in cfg)
                {
                    string key = kv.Key.Key;
                    if ((key == "UiScale" || (key == "Scale" && kv.Key.Section == "UI")) && kv.Value.BoxedValue is float f && f > 0f) return f;
                }
            }
            catch (Exception) { }
            return 1f;
        }

        // Their UiScale can change while the game runs (Plugin.Update, every 2 s).
        internal static void RefreshScales()
        {
            if (Active.Count == 0) return;
            foreach (var b in new[] { SaveGuard, QuestHelper })
                if (b.Resolved && !b.Broken && Chainloader.PluginInfos.TryGetValue(b.Guid, out var info)) b.UiScale = ReadUiScale(info);
        }
    }
}
