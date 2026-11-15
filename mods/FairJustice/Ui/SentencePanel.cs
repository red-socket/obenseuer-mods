using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using UnityEngine;

namespace FairJustice
{
    // S-037 sentence panel (spec §3.3A). Read-only IMGUI box while jailed: time left until release and the charges by
    // crime type. Strings are rebuilt at 1 Hz in Update from live game data; OnGUI only draws the cached strings.
    // The release estimate follows Prison.DeltaSeconds (SRC/Prison.cs:173-219): records burn at crimePerPrisonHour,
    // one at a time, with a 300 s minimum stay and a (100 + 12 x timesInJail) h cap.
    internal static class SentencePanel
    {
        internal static bool EnabledAtLaunch;

        private static bool _shown = true;     // session toggle (ToggleKey), reset at the main menu
        private static bool _visible;
        private static float _nextRebuild;
        private static bool _dirty = true;

        private static string _place = "", _line = "";
        private static readonly List<KeyValuePair<string, string>> Rows = new List<KeyValuePair<string, string>>();

        private static GUIStyle _right, _line16, _placeStyle;
        private static float _lineHeight;

        private const float Width = 340f;
        private const float Pad = 16f;

        // Off when Prison.prisonInSeconds is missing: every estimate depends on it.
        internal static bool Live => EnabledAtLaunch && Cfg.SentencePanel.Value && Vanilla.HasSeconds;
        internal static bool Visible => _visible;

        internal static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene)
        {
            if (scene.name == Jail.MainMenuScene) _shown = true;
            _visible = false;
            _nextRebuild = 0f;
        }

        internal static void Update()
        {
            if (!Live)
            {
                _visible = false;
                return;
            }
            bool inPrison = Jail.InPrison;
            if (inPrison && !Game.ConsoleOpen && Cfg.ToggleKey.Value.IsDown())
            {
                _shown = !_shown;
                Log.Verbose(_shown ? "Sentence panel shown." : "Sentence panel hidden.");
            }
            bool want = inPrison && _shown && WantVisible();
            if (want && (!_visible || Time.unscaledTime >= _nextRebuild))
            {
                _nextRebuild = Time.unscaledTime + 1f;
                Rebuild();
            }
            _visible = want;
        }

        private static bool WantVisible()
        {
            if (SaveController.Loading) return false;
            var ui = GameUIController.instance;
            if (ui == null || !ui.GameUIVisible()) return false;
            if (ui.IsMenuVisible)
            {
                // IsMenuVisible is also true while the waiting clock shows (GameUIController.cs:115-124).
                bool waiting = WaitingUI.instance != null && WaitingUI.instance.waitingUIisVisible;
                if (!waiting || !Cfg.ShowWhileWaiting.Value) return false;
            }
            return !Game.Paused && !Game.ConsoleOpen && !Game.DialogueActive;
        }

        private static void Rebuild()
        {
            _dirty = true;
            var crime = Crime.instance;
            var prison = Jail.Current;
            Rows.Clear();
            if (crime == null || prison == null)
            {
                _line = "";
                return;
            }
            _place = PlaceName(prison);

            // Charges: one row per crime type with a positive sum, in record order.
            float perHour = crime.crimePerPrisonHour > 0f ? crime.crimePerPrisonHour : 250f;
            var order = new List<Crime.CrimeType>();
            var sums = new Dictionary<Crime.CrimeType, float>();
            float sumPos = 0f;
            foreach (var r in crime.crimeRecord)
            {
                if (r == null || r.amount <= 0f) continue;
                sumPos += r.amount;
                if (!sums.ContainsKey(r.type))
                {
                    sums[r.type] = 0f;
                    order.Add(r.type);
                }
                sums[r.type] += r.amount;
            }
            foreach (var t in order)
                Rows.Add(new KeyValuePair<string, string>(TypeName(t), Duration(sums[t] / perHour * 3600f)));

            // Time left.
            float burn = sumPos / perHour * 3600f;
            float pis = Vanilla.PrisonInSeconds(prison);
            float capRem = (100f + 12f * crime.timesInJail) * 3600f - pis;
            float minRem = 300f - pis;
            float left = Mathf.Max(0f, Mathf.Min(Mathf.Max(burn, minRem), capRem));

            if (NightRelease.HeldNow)
            {
                _line = $"Time served · released at {Cfg.MorningText}";
                return;
            }
            if (left >= capRem - 0.5f)
                _line = $"Release in {Duration(left)} (maximum sentence)";
            else if (burn < minRem)
                _line = $"Release in {Duration(left)} (minimum stay)";
            else
            {
                var sb = new StringBuilder("Release in ").Append(Duration(left));
                var tod = TimeOfDayAzure.instance != null ? TimeOfDayAzure.instance.currentTimeAndDay : null;
                if (tod != null)
                {
                    float at = tod.hours + tod.minutes / 60f + left / 3600f;
                    int days = Mathf.FloorToInt(at / 24f);
                    float h = at - days * 24f;
                    int hh = Mathf.FloorToInt(h), mm = Mathf.FloorToInt((h - hh) * 60f);
                    sb.Append($" · about {hh:00}:{mm:00} ").Append(days <= 0 ? "today" : days == 1 ? "tomorrow" : $"in {days} days");
                    if (NightRelease.Live && Cfg.InNightWindow(h)) sb.Append($" · held until {Cfg.MorningText}");
                }
                _line = sb.ToString();
            }
        }

        private static string PlaceName(Prison p)
        {
            string scene = p.gameObject.scene.name ?? "";
            if (scene.IndexOf("Bazaar", StringComparison.OrdinalIgnoreCase) >= 0) return "Bazaar jail";
            if (scene.IndexOf("Gatehouse", StringComparison.OrdinalIgnoreCase) >= 0) return "Gatehouse";
            return scene;
        }

        // [Description] name (as the game's own crime notification), else the enum name; Unspecified reads "Other".
        private static string TypeName(Crime.CrimeType t)
        {
            if (t == Crime.CrimeType.Unspecified) return "Other";
            try
            {
                var f = typeof(Crime.CrimeType).GetField(t.ToString());
                if (f != null && Attribute.GetCustomAttribute(f, typeof(DescriptionAttribute)) is DescriptionAttribute d && !string.IsNullOrEmpty(d.Description))
                    return d.Description;
            }
            catch (Exception) { }
            return t.ToString();
        }

        // Game seconds as "5 h 20 min", "24 min", "112 h" (a day or more shows hours only).
        internal static string Duration(float seconds)
        {
            int totalMin = Mathf.CeilToInt(Mathf.Max(0f, seconds) / 60f);
            if (totalMin <= 0) return "under a minute";
            int h = totalMin / 60, m = totalMin % 60;
            if (h >= 24) return $"{Mathf.RoundToInt(seconds / 3600f)} h";
            if (h == 0) return $"{m} min";
            return m == 0 ? $"{h} h" : $"{h} h {m} min";
        }

        // Inside Skin.Begin(); screen is the virtual (1080-high x Scale) size.
        internal static void OnGUI(Vector2 screen)
        {
            if (!_visible) return;
            if (_right == null || _line16 == null || _placeStyle == null)
            {
                _right = new GUIStyle(Skin.Label) { alignment = TextAnchor.UpperRight, wordWrap = false };
                _line16 = new GUIStyle(Skin.Label) { wordWrap = true };
                _placeStyle = new GUIStyle(Skin.Small) { alignment = TextAnchor.UpperRight, wordWrap = false };
                _dirty = true;
            }
            if (_dirty)
            {
                _lineHeight = _line16.CalcHeight(new GUIContent(_line), Width - 2 * Pad);
                _dirty = false;
            }
            float scale = Mathf.Max(0.01f, Cfg.Scale.Value);
            float ox = Cfg.OffsetX.Value / scale, oy = Cfg.OffsetY.Value / scale;   // offsets are 1080p pixels
            const float titleH = 30f, rowH = 22f;
            float h = Pad + titleH + _lineHeight + (Rows.Count > 0 ? 6f + Rows.Count * rowH : 0f) + Pad - 4f;

            var anchor = Cfg.Anchor.Value;
            float x = anchor == PanelAnchor.TopLeft || anchor == PanelAnchor.BottomLeft ? ox : screen.x - ox - Width;
            float y = anchor == PanelAnchor.TopLeft || anchor == PanelAnchor.TopRight ? oy : screen.y - oy - h;
            x = Mathf.Clamp(x, 0f, Mathf.Max(0f, screen.x - Width));
            y = Mathf.Clamp(y, 0f, Mathf.Max(0f, screen.y - h));

            GUI.Box(new Rect(x, y, Width, h), GUIContent.none, Skin.Box);
            float cx = x + Pad, cw = Width - 2 * Pad, cy = y + Pad - 4f;
            GUI.Label(new Rect(cx, cy, cw, titleH), "IN CUSTODY", Skin.Title);
            GUI.Label(new Rect(cx, cy + 6f, cw, titleH), _place, _placeStyle);
            cy += titleH;
            GUI.Label(new Rect(cx, cy, cw, _lineHeight), _line, _line16);
            cy += _lineHeight + 6f;
            foreach (var r in Rows)
            {
                GUI.Label(new Rect(cx, cy, cw, rowH), r.Key, Skin.Label);
                GUI.Label(new Rect(cx, cy, cw, rowH), r.Value, _right);
                cy += rowH;
            }
        }
    }
}
