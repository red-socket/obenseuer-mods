using UnityEngine;

namespace DisplayOptions
{
    // S-005 part 2: the shadow budget's preset table (spec §5.1, D5). 0 turns a part off. Every value only ever lowers
    // the game's: nothing here raises a distance or a cascade count. Everything is runtime-only; Quality.json and the
    // options menu's current* indices are never written.
    internal static class Budget
    {
        internal struct Values
        {
            internal float MicroMax, LightCap, MaxDist;
            internal bool IncludeLoose;
            internal int MaxCasc;
        }

        internal static Values Effective()
        {
            var s = Cfg.Shadows.Preset != null ? Cfg.Shadows.Preset.Value : ShadowPreset.Off;
            switch (s)
            {
                case ShadowPreset.Light: return new Values { MicroMax = 0.15f, LightCap = 25f };
                case ShadowPreset.Balanced: return new Values { MicroMax = 0.25f, LightCap = 15f, MaxDist = 80f, MaxCasc = 2 };
                case ShadowPreset.Strong: return new Values { MicroMax = 0.40f, LightCap = 10f, MaxDist = 50f, MaxCasc = 2 };
                case ShadowPreset.Custom:
                    return new Values
                    {
                        MicroMax = Mathf.Clamp(Cfg.Shadows.CustomMicroPropMaxSize.Value, 0f, 1f),
                        IncludeLoose = Cfg.Shadows.CustomIncludeLooseItems.Value,
                        LightCap = Floor(Cfg.Shadows.CustomLightShadowDistance.Value, 8f, 60f),
                        MaxDist = Floor(Cfg.Shadows.CustomMaxShadowDistance.Value, 10f, 150f),
                        MaxCasc = Mathf.Clamp(Cfg.Shadows.CustomMaxShadowCascades.Value, 0, 2),
                    };
                default: return new Values();
            }
        }

        // "0 or min-max": 0 stays off, anything else is clamped into the range.
        private static float Floor(float v, float min, float max) => v <= 0f ? 0f : Mathf.Clamp(v, min, max);

        // Config change in [Shadows]: every part re-applies from the new effective values (spec §3.2).
        internal static void Changed()
        {
            Plugin.Step("shadows (quality caps)", QualityCaps.Reapply);
            Plugin.Step("shadows (light cap)", LightCap.OnConfigChanged);
            Plugin.Step("shadows (micro-props)", MicroProps.OnConfigChanged);
        }

        internal static string Describe()
        {
            var p = Cfg.Shadows.Preset.Value;
            if (p != ShadowPreset.Custom) return p.ToString();
            var h = Effective();
            return $"Custom (props < {h.MicroMax:0.##} m{(h.IncludeLoose ? " incl. pickups" : "")}, lamps {h.LightCap:0} m, " +
                   $"distance {h.MaxDist:0} m, cascades {h.MaxCasc}; 0 = off)";
        }
    }
}
