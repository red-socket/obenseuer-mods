using UnityEngine;

namespace GreenThumb
{
    // S-070 quick-plant ghost (spec §9.7, D19, D21; issue #6301). Growing.UpdateQuickSlot shows the quick-plant bar and
    // the plant preview whenever the crosshair is on an empty spot of a grower (SRC/Growing.cs:760-780), even while you
    // are harvesting the plant next to it. Q1 skips that per-frame call when the mode says so, and QuickSlotBar.LateUpdate
    // then hides the bar or clears the preview on the next frame (SRC/QuickSlotBar.cs:213-224). Planting (QuickPlant,
    // StartGrow) is never touched, and while the bar is hidden HasPrimaryUseActive is false, so tools swing normally.
    internal static class Ghost
    {
        internal static bool ForeignSkip;

        internal static bool Hide(Growing g, Vector3 point)
        {
            switch (Cfg.Hot.GhostMode)
            {
                case GhostMode.Off:
                    return true;
                case GhostMode.CrouchOnly:
                    var c = OpenSewerCharacterController.instance;   // LeftCtrl or RS click, the game's own crouch
                    return c == null || !c.isCrouching;
                case GhostMode.HideNearReady:
                    return ReadyNear(g, point, Cfg.Hot.NearReadySqr);
                default:
                    return false;
            }
        }

        // Height difference within which a plant counts as on the same level as the crosshair hit. Under half the tightest
        // shelf spacing of the stacked growers (prefab spot heights: Mushroom Growing Rack 0.65 m apart, Hydroponic Farm
        // 0.6, Vertical Farm 0.56, Mushroom Growing Cabinet 0.44-0.52), so a ready plant on the shelf above or below never
        // hides the preview, while planter soil and gently sloping field ground still count as level. A layout constant,
        // not a preference, so it is not in the config.
        private const float SameLevel = 0.2f;

        // A ready plant of g on the hit's level (SameLevel) and within horizontal r² of the crosshair hit: dead, or grown
        // with no fruit or any ripe fruit. Walks GrowingSlots (54 at most) without allocating; CanRepeatHarvest is avoided
        // because its fit check allocates.
        internal static bool ReadyNear(Growing g, Vector3 point, float r2)
        {
            var spots = g.GrowingSlots;
            if (spots == null) return false;
            for (int i = 0; i < spots.Count; i++)
            {
                var spot = spots[i];
                if (spot == null || spot.Plant == null) continue;
                var t = spot.Plant.GetComponentInChildren<grow>();
                if (t == null) continue;
                Vector3 d = t.transform.position - point;
                if (d.y > SameLevel || d.y < -SameLevel) continue;
                d.y = 0f;
                if (d.sqrMagnitude > r2 || !t.isActiveAndEnabled || t.IsInteractDisabled()) continue;
                if (Vanilla.GrowIsDead(t)) return true;
                if (t.Age < t.FullGrownAge) continue;
                var items = t.visibleGrownItems;
                if (items == null || items.Length == 0) return true;
                for (int k = 0; k < items.Length; k++)
                {
                    var f = items[k];
                    if (f != null && f.readyToPick && f.gameObject.activeInHierarchy) return true;
                }
            }
            return false;
        }
    }
}
