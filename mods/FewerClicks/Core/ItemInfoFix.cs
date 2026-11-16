using System;
using UnityEngine;

namespace FewerClicks
{
    // ItemInfo.instance is only set in ItemInfo.Awake, which runs the first time the game menu opens in a scene
    // (SRC/ItemInfo.cs:45-48). Eating through ItemInfo.UseItemDirectly before that throws. Awake does nothing else
    // and Start only caches an AudioSource that PlaySound looks up again when null (:50-83), so binding the scene's own
    // inactive component reaches the same state vanilla reaches after the menu's first open. Never creates one.
    internal static class ItemInfoFix
    {
        private static ItemInfo _cached;

        internal static void OnSceneLoaded() => _cached = null;

        internal static bool EnsureItemInfo()
        {
            if ((bool)ItemInfo.instance) return true;
            try
            {
                if (_cached == null)
                {
                    foreach (var ii in Resources.FindObjectsOfTypeAll<ItemInfo>())
                        if (ii != null && ii.gameObject.scene.isLoaded) { _cached = ii; break; }
                }
                if (_cached == null)
                {
                    Log.WarnOnce("iteminfo", "Quick Eat: the game's item panel was not found in this scene; open your inventory once, then try again.");
                    return false;
                }
                ItemInfo.instance = _cached;
                Log.Verbose("Quick Eat: bound the scene's item panel (inventory not opened yet in this scene).");
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("item info", e);
                return false;
            }
        }
    }
}
