using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DisplayOptions
{
    // S-095 UI scale (spec §7). For each allowlisted root scaler, referenceResolution = original / kEff. That multiplies
    // the final scaleFactor by exactly kEff in every match mode (UGUI/CanvasScaler.cs:283-305), and Handle() re-derives it
    // every preWillRenderCanvases, so nothing polls. Canvas.scaleFactor is never written. Writes are always computed from
    // the recorded original, so applying twice is idempotent. While UI scale is 1, UltrawideDialogue is off and nothing is
    // scaled, P6 and the sweeps return at once: no scaler is read or written at the defaults.
    internal static class Scaling
    {
        internal static bool Broken, StoodDown;

        private sealed class Orig
        {
            internal Vector2 Ref;
            internal CanvasScaler.ScreenMatchMode Mode;
            internal UiKind Kind;
            internal Vector2 LastRef;
            internal CanvasScaler.ScreenMatchMode LastMode;
        }

        private static readonly Dictionary<CanvasScaler, Orig> Map = new Dictionary<CanvasScaler, Orig>();
        private static readonly HashSet<int> Dropped = new HashSet<int>();   // rewritten by someone else: never ours again
        private static readonly List<CanvasScaler> PauseWaiting = new List<CanvasScaler>();   // open pause menus with a change due
        private static float? _preview;
        private static float _lastClampAspect = -1f;

        private static readonly Vector2 GameRef = new Vector2(1920f, 1080f), DialogueRef = new Vector2(800f, 600f);

        internal static bool Active => !Broken && !StoodDown && (Cfg.Hot.UiWanted || _preview.HasValue || Map.Count > 0);

        // The slider's live value while it is being dragged, else the config value.
        private static float K => _preview ?? Cfg.Hot.Scale;

        // ---- Entry points ----

        // P6 prefix: before the Handle() inside OnEnable, so a canvas is never drawn at the old size.
        internal static void OnScalerEnable(CanvasScaler s)
        {
            if (!Active || s == null) return;
            var kind = Allowlist.Classify(s);
            if (kind == UiKind.Never) return;
            if (kind == UiKind.Pause && PauseOpen(s))
            {
                Defer(s);
                return;
            }
            Apply(s, kind);
        }

        // sceneLoaded: prune, then sweep every scaler (also the DDOL Loading Screen, inactive shop and Start Selection
        // canvases and the pause menu GameLogic.Awake already made).
        internal static void OnSceneLoaded()
        {
            Allowlist.Clear();
            Prune();
            PauseWaiting.RemoveAll(x => x == null);
            if (!Active) return;
            Sweep(includePause: true);
            LogScaled();
        }

        // Config change: every kind but an open pause menu, which follows when it closes (Tick).
        internal static void ApplyAll()
        {
            if (Broken || StoodDown) return;
            Sweep(includePause: true);
            if (!Cfg.Hot.UiWanted && !_preview.HasValue) RestoreAll(force: false);
        }

        // Slider drag: live for everything but the pause menu, no config write.
        internal static void Preview(float k)
        {
            if (Broken || StoodDown) return;
            _preview = Mathf.Clamp(k, 0.75f, 1.5f);
            Sweep(includePause: false);
        }

        internal static void EndPreview() => _preview = null;

        // Fallback when PauseMenu.hidePause is missing (every pause menu then counts as open): RowHook.OnDisable applies the
        // Pause kind when the row closes, as before.
        internal static void ApplyPause()
        {
            if (!Active) return;
            foreach (var s in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (s != null && Allowlist.Classify(s) == UiKind.Pause) Apply(s, UiKind.Pause);
        }

        // Plugin.Update: a pause menu that was open when a change came (config, slider commit, display Apply) takes it as
        // soon as it closes, however the change was made. Costs nothing while no change is waiting.
        internal static void Tick()
        {
            if (PauseWaiting.Count == 0) return;
            for (int i = PauseWaiting.Count - 1; i >= 0; i--)
            {
                var s = PauseWaiting[i];
                if (s != null && PauseOpen(s)) continue;
                PauseWaiting.RemoveAt(i);
                if (s == null || Broken || StoodDown) continue;
                Apply(s, UiKind.Pause);
            }
        }

        // Without hidePause nothing can tell when the menu closes; ApplyPause on row close is the fallback then.
        private static void Defer(CanvasScaler s)
        {
            if (Vanilla.PauseHide != null && !PauseWaiting.Contains(s)) PauseWaiting.Add(s);
        }

        // Display Apply (P2, 2 frames later): the fit limits depend on the aspect.
        internal static void Refit()
        {
            if (!Active) return;
            Sweep(includePause: true);
        }

        // ---- Core ----

        private static void Sweep(bool includePause)
        {
            if (!Active) return;
            foreach (var s in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (s == null) continue;
                var kind = Allowlist.Classify(s);
                if (kind == UiKind.Never) continue;
                if (kind == UiKind.Pause)
                {
                    if (!includePause) continue;   // slider preview: the commit's ApplyAll handles the pause menu
                    if (PauseOpen(s))
                    {
                        Defer(s);
                        continue;
                    }
                }
                Apply(s, kind);
            }
        }

        private static void Apply(CanvasScaler s, UiKind kind)
        {
            if (!s.gameObject.scene.IsValid()) return;   // prefab assets are never touched
            if (!Map.TryGetValue(s, out var o))
            {
                if (!Wants(kind) || Dropped.Contains(s.GetInstanceID())) return;
                var r = s.referenceResolution;
                if (r != GameRef && r != DialogueRef)
                {
                    // A copy of an already-scaled live canvas, or a canvas the game sized differently: leave it alone.
                    Log.WarnOnce("clone " + s.name, $"Ui: {s.name} has an unexpected reference size {r}; Display Options leaves it alone.");
                    return;
                }
                o = new Orig { Ref = r, Mode = s.screenMatchMode, Kind = kind, LastRef = r, LastMode = s.screenMatchMode };
                Map[s] = o;
            }
            else if (s.referenceResolution != o.LastRef || s.screenMatchMode != o.LastMode)
            {
                // Someone else rewrote it since our last write: theirs now.
                Map.Remove(s);
                Dropped.Add(s.GetInstanceID());
                Log.WarnOnce("foreign " + s.name, $"Ui: something else changed {s.name}'s scaling; Display Options leaves that canvas alone.");
                return;
            }

            var mode = o.Mode;
            var baseRef = o.Ref;
            float k = 1f;
            if (Wants(kind))
            {
                k = KEff(kind);
                if (kind == UiKind.Dialogue && Cfg.Hot.Ultrawide)
                {
                    // Expand (800, 450): min(W/800, H/450) = W/800 (vanilla) at 16:9 and narrower, the 16:9 size on wider.
                    mode = CanvasScaler.ScreenMatchMode.Expand;
                    baseRef = new Vector2(800f, 450f);
                }
            }
            var target = baseRef / k;
            if (s.referenceResolution != target) s.referenceResolution = target;
            if (s.screenMatchMode != mode) s.screenMatchMode = mode;
            o.LastRef = s.referenceResolution;
            o.LastMode = s.screenMatchMode;
            if (!Wants(kind)) Map.Remove(s);   // back to its original: tracked only while scaled
        }

        // Whether this kind changes at all with the current settings.
        private static bool Wants(UiKind kind)
        {
            bool scaled = !Mathf.Approximately(K, 1f);
            switch (kind)
            {
                case UiKind.Dialogue: return (Cfg.Hot.ScaleDialogue && scaled) || Cfg.Hot.Ultrawide;
                case UiKind.Pause: return Cfg.Hot.ScalePause && scaled;
                case UiKind.Never: return false;
                default: return scaled;
            }
        }

        // Effective scale per kind (spec §7.3): the config value, clamped to what fits on this screen.
        internal static float KEff(UiKind kind)
        {
            float k = K;
            switch (kind)
            {
                case UiKind.GameUi:
                case UiKind.CardGame:
                case UiKind.StartSel:
                    float fit = FitGame();
                    float kg = Fit(k, fit);
                    if (kg < k - 0.001f) NoteClamp(k, kg);
                    return kg;
                case UiKind.Shop: return Fit(k, FitShop());
                case UiKind.Loading: return Fit(k, 1.5f);
                case UiKind.Dialogue: return Cfg.Hot.ScaleDialogue ? Fit(k, FitDialogue()) : 1f;
                case UiKind.Pause: return Fit(k, FitPause());
                default: return 1f;
            }
        }

        // Never above the fit (unless the fit is below 1: then never above 1), never below 0.75.
        private static float Fit(float k, float fit) => Mathf.Clamp(k, 0.75f, Mathf.Max(1f, fit));

        private static float Floor05(float v) => Mathf.Floor(v * 20f + 0.0001f) / 20f;

        private static float W => Mathf.Max(1, Screen.width);
        private static float H => Mathf.Max(1, Screen.height);

        // Game UI, card game, Start Selection (Expand 1920x1080). 976 units: the game menu's 900-unit content, its 45-unit
        // tab row and offset. 1405 units: the right edge of the top-left-anchored phone and note panels (bank: 1402).
        internal static float FitGame()
        {
            float uH = Mathf.Max(1080f, 1920f * H / W), uW = Mathf.Max(1920f, 1080f * W / H);
            return Floor05(Mathf.Min(uH / 976f, uW / 1405f));
        }

        // Shops (match height 1920x1080): two 410-unit columns and a 400-unit minimum centre.
        private static float FitShop() => Floor05(1080f * W / H / 1220f);

        // Dialogue (match width 800x600): 800 units wide and 800·H/W high. With UltrawideDialogue the canvas is Expand
        // (800, 450): max(800, 450·W/H) wide and max(800·H/W, 450) high, the same at 16:9 and narrower. Height: at least 280
        // units. Width: at least 650, because Portrait Name stretches to the canvas width minus 500 units and an NPC's full
        // name needs about 150 at the game's 11-point size (it breaks into letters below that). About 1.2 at 16:9.
        private static float FitDialogue()
        {
            float uD = 800f * H / W, uW = 800f;
            if (Cfg.Hot.Ultrawide)
            {
                uD = Mathf.Max(uD, 450f);
                uW = Mathf.Max(uW, 450f * W / H);
            }
            return Floor05(Mathf.Min(uD / 280f, uW / 650f));
        }

        // Pause (match height 800x600, so always 600 units high): the tallest page, or Menu/Main's preferred height when our
        // sibling mods' entries make it taller. About 1.2 in 0.5.19 (pages are 500 units).
        private static float FitPause()
        {
            float maxH = 0f;
            foreach (var p in Object.FindObjectsByType<PauseMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var menu = p != null ? p.transform.Find("Hide/Pause Menu/Menu") : null;
                if (menu == null) continue;
                foreach (Transform c in menu)
                {
                    var rt = c as RectTransform;
                    if (rt == null || c.name == "Background") continue;
                    if (!Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y)) continue;   // stretched (Save menu): not a page height
                    maxH = Mathf.Max(maxH, rt.rect.height);
                    if (c.name == "Main") maxH = Mathf.Max(maxH, LayoutUtility.GetPreferredHeight(rt));
                }
            }
            return maxH > 1f ? Floor05(600f / maxH) : 1f;
        }

        private static bool PauseOpen(CanvasScaler s) => Vanilla.PauseOpen(s.GetComponent<PauseMenu>());

        private static void NoteClamp(float k, float kEff)
        {
            float aspect = W / H;
            if (Mathf.Abs(aspect - _lastClampAspect) < 0.001f) return;
            _lastClampAspect = aspect;
            Log.Info($"Ui: scale {k:0.00} is limited to {kEff:0.00} on this screen so the game menu fits.");
        }

        // ---- Restore ----

        private static void Prune()
        {
            var dead = new List<CanvasScaler>();
            foreach (var kv in Map)
                if (kv.Key == null) dead.Add(kv.Key);
            foreach (var d in dead) Map.Remove(d);
        }

        // UI scale back to 1 with UltrawideDialogue off, stand-down and teardown: every original back. An open pause menu
        // keeps its size until it closes (Tick applies it then), unless force.
        internal static void RestoreAll(bool force)
        {
            var done = new List<CanvasScaler>();
            foreach (var kv in Map)
            {
                var s = kv.Key;
                if (s == null)
                {
                    done.Add(s);
                    continue;
                }
                if (!force && kv.Value.Kind == UiKind.Pause && PauseOpen(s))
                {
                    Defer(s);
                    continue;
                }
                done.Add(s);
                try
                {
                    if (s.referenceResolution != kv.Value.LastRef || s.screenMatchMode != kv.Value.LastMode) continue;   // not ours any more
                    s.referenceResolution = kv.Value.Ref;
                    s.screenMatchMode = kv.Value.Mode;
                }
                catch (Exception e) { Log.Exception("Ui: restore", e); }
            }
            foreach (var d in done) Map.Remove(d);
        }

        internal static void StandDown()
        {
            if (StoodDown) return;
            PauseWaiting.Clear();
            RestoreAll(force: true);
            StoodDown = true;
        }

        private static void LogScaled()
        {
            if (!Log.VerboseOn || Map.Count == 0) return;
            var parts = new List<string>();
            foreach (var kv in Map)
                if (kv.Key != null) parts.Add($"{kv.Key.name} ({kv.Value.Kind} {KEff(kv.Value.Kind):0.00})");
            Log.Verbose($"Ui: scale {K:0.00}; canvases: {string.Join(", ", parts.ToArray())}.");
        }

        // ---- Api ----

        internal static float GameKEff => Active && Wants(UiKind.GameUi) ? KEff(UiKind.GameUi) : 1f;
    }
}
