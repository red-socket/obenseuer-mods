using System;

namespace VitalSigns
{
    // Addiction clamp (spec §8.1-8.2, D11, D12). Vanilla never clamps alcohol and mushroom addiction
    // (SRC/PlayerStatsAddictions.cs:323-324; Methanol leaves mushroom at -0.1, which vanilla never recovers): that is the
    // approved fix, Fixes.ClampAddictions. Smoking is clamped by vanilla, then the item's raw SmokingAddiction is added
    // after the clamp (:318-325); clamping that is an extension with its own switch, Fixes.ClampSmoking (off by default).
    // Nothing is written unless a value is out of range.
    internal static class Clamp
    {
        internal static bool Patched;

        // C1, after each consumption.
        internal static void AfterItem(PlayerStatsAddictions a)
        {
            if (a == null || (!Cfg.Hot.Clamp && !Cfg.Hot.ClampSmoking)) return;
            string item = null;
            if (Log.VerboseOn)
            {
                var top = Ctx.Top;
                item = top.Kind == K.Item ? Ctx.Label(top.Id) : null;
            }
            if (Cfg.Hot.Clamp)
            {
                a.AlcoholAddiction = One(a.AlcoholAddiction, Strings.AddAlcohol, item, false);
                a.MushroomAddiction = One(a.MushroomAddiction, Strings.AddMushroom, item, false);
            }
            if (Cfg.Hot.ClampSmoking) a.SmokingAddiction = One(a.SmokingAddiction, Strings.AddSmoking, item, false);
        }

        // C2, after the scene-event writers (Mines Electrical Room 2, Bazaar Murju A, Tenement Gatehouse).
        internal static void Alcohol(PlayerStatsAddictions a)
        {
            if (a != null && Cfg.Hot.Clamp) a.AlcoholAddiction = One(a.AlcoholAddiction, Strings.AddAlcohol, Strings.SceneEvent, false);
        }

        internal static void Mushroom(PlayerStatsAddictions a)
        {
            if (a != null && Cfg.Hot.Clamp) a.MushroomAddiction = One(a.MushroomAddiction, Strings.AddMushroom, Strings.SceneEvent, false);
        }

        // C3, after the stats are restored from a save (loads and doors; idempotent).
        internal static void OnLoad(PlayerStatsAddictions a)
        {
            if (a == null || !Cfg.Hot.ClampOnLoad) return;
            if (Cfg.Hot.Clamp)
            {
                a.AlcoholAddiction = One(a.AlcoholAddiction, Strings.AddAlcohol, null, true);
                a.MushroomAddiction = One(a.MushroomAddiction, Strings.AddMushroom, null, true);
            }
            if (Cfg.Hot.ClampSmoking) a.SmokingAddiction = One(a.SmokingAddiction, Strings.AddSmoking, null, true);
        }

        private static float One(float v, string name, string source, bool load)
        {
            if (v >= 0f && v <= 100f) return v;
            if (float.IsNaN(v)) return v;   // not ours to repair
            float c = v < 0f ? 0f : 100f;
            if (load) Log.Info($"Addict: {name} addiction {v:0.##} -> {c:0} (saved value outside 0-100).");
            else Log.Verbose($"Addict: {name} addiction {v:0.##} -> {c:0}{(source != null ? " after " + source : "")}.");
            return c;
        }
    }
}
