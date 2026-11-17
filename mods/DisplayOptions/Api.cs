using System;
using UnityEngine;

namespace DisplayOptions
{
    // Read-only values for our other mods (spec §11.3, D17). Passive: reading them changes nothing. A sibling opts in with
    // a cached reflection lookup of "DisplayOptions.Api, DisplayOptions" and a fallback of 1, behind its own setting.
    public static class Api
    {
        // The Game UI canvas's effective UI scale (clamped to fit); 1 when UI scale is inactive.
        public static float UiScale
        {
            get
            {
                try { return Scaling.GameKEff; }
                catch (Exception) { return 1f; }
            }
        }

        // Screen pixels per Game UI canvas unit: the root canvas's scaleFactor, or Screen.height / 1080.
        public static float GameUiPixelScale
        {
            get
            {
                try
                {
                    var g = GameUIInstance.instance;
                    var c = g != null ? g.GetComponent<Canvas>() : null;
                    if (c != null && c.scaleFactor > 0f) return c.scaleFactor;
                }
                catch (Exception) { }
                return Screen.height / 1080f;
            }
        }
    }
}
