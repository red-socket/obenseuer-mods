using System;
using System.Reflection;
using HarmonyLib;
using NPC;
using UnityEngine;
using WieldableItem;

namespace FewerClicks
{
    // S-049(b) Auto Swing (spec §2 P3, §4). MeleeWeapon swings only on release: holding Primary Use winds up (Use) and
    // waits. While the button is held and the crosshair is on a resource the tool can work, the postfix releases for the
    // player as soon as the wind-up is ready, with the same call the key-up makes (PlayerHandItems.PrimaryStopHold,
    // GameController.cs:202-205). Vanilla then swings, hits and starts the cooldown; after it, the held key's next
    // onPrimaryHold winds up again. Nothing private is written, so time, tool wear, stock, yields and skill XP are those
    // of a manual swing. 0.5.19 has no stamina.
    // The hit itself lands later, when the swing animation enables it, on whatever the melee ray points at then
    // (MeleeWeapon.Update -> Hit). A swing we released lands only on a target TargetOk accepts: if a person steps in
    // front or the camera turns away mid-swing, HitPrefix closes that swing's hit window, so it counts as a miss (no
    // damage, no wear, no crime), as a swing into the air does. Manual swings are never touched.
    internal static class AutoSwing
    {
        internal static bool Patched;

        // The weapon whose current swing we released; cleared when that swing's hit is resolved or a new wind-up starts.
        private static MeleeWeapon _released;

        private static int _lastHoldFrame = -10;
        private static bool _session;
        private static bool _suspendedThisScene;
        private static bool? _postfixOnly;
        private static int _swings;
        private static string _sessionTarget;

        internal static bool Active => Patched && Cfg.AutoSwing.Enabled.Value && !_suspendedThisScene;

        internal static void OnSceneLoaded()
        {
            EndSession();
            _released = null;
            _suspendedThisScene = false;
        }

        // Another mod's skipping prefix on onPrimaryHold: our prefix then never blocks a wind-up (postfix-only mode).
        private static bool PostfixOnly
        {
            get
            {
                if (_postfixOnly == null)
                    _postfixOnly = Compat.Foreign(Vanilla.OnPrimaryHold, "Auto Swing", "keeps auto-releasing but no longer holds back off-target wind-ups");
                return _postfixOnly.Value;
            }
        }

        internal static void Suspend(string key, Exception e)
        {
            Log.Exception(key, e);
            _suspendedThisScene = true;
            _released = null;
            EndSession();
        }

        // Toggle key (unbound by default): flips and saves Enabled. Only in free gameplay (PlayGate), so typing on a
        // sign or the computer (keysDisabled without writeLock) never flips it.
        internal static void Update()
        {
            if (!Patched || !Keys.Bound(Cfg.AutoSwing.ToggleKey)) return;
            if (!Keys.Down(Cfg.AutoSwing.ToggleKey)) return;
            string blocked = PlayGate.Blocked();
            if (blocked != null)
            {
                Log.Verbose($"Auto Swing: toggle key ignored ({blocked}).");
                return;
            }
            Cfg.AutoSwing.Enabled.Value = !Cfg.AutoSwing.Enabled.Value;
            if (!Cfg.AutoSwing.Enabled.Value) EndSession();
            Game.Notify(Cfg.AutoSwing.Enabled.Value ? "Auto Swing on" : "Auto Swing off");
        }

        internal static bool Prefix(MeleeWeapon w)
        {
            if (!Active || !IsHeld(w)) return true;
            if (Time.frameCount - _lastHoldFrame > 1) EndSession();   // the button was released, or the hold path was gated
            _lastHoldFrame = Time.frameCount;
            // After an auto release, only wind up again on a valid target: turning to a person or an empty node while
            // holding leaves the tool idle, and releasing then does nothing (holdStarted is false).
            if (_session && !PostfixOnly && !Vanilla.HoldStarted(w) && !TargetOk(w, RayCollider(), out _)) return false;
            return true;
        }

        internal static void Postfix(MeleeWeapon w)
        {
            // A wind-up in progress means the previous swing (and its hit window) is over: Use needs notReady cleared,
            // which the swing animation does after its hit (MeleeAnimationEvents.ReadyToUse).
            if (w != null && ReferenceEquals(_released, w) && Vanilla.HoldStarted(w)) _released = null;
            if (!Active || !IsHeld(w)) return;
            if (!Vanilla.HoldStarted(w) || !w.holding || !TargetOk(w, RayCollider(), out var target)) return;
            if (!_session)
            {
                _session = true;
                _swings = 0;
                _sessionTarget = target;
                Log.Verbose($"Auto Swing: started with {ToolName(w)} on {target}.");
            }
            _swings++;
            PlayerHandItems.instance.PrimaryStopHold();
            if (!Vanilla.HoldStarted(w)) _released = w;
        }

        // MeleeWeapon.Hit prefix. Mirrors Hit's own early returns, so only a hit that would land is looked at, once.
        internal static bool HitPrefix(MeleeWeapon w, RaycastHit hit)
        {
            if (_released == null || !ReferenceEquals(_released, w)) return true;
            var io = InteractObjects.instance;
            if (io == null || !io.meleeHit || hit.collider == null || !w.hitEnabled) return true;
            _released = null;
            if (TargetOk(w, hit.collider, out _)) return true;
            w.hitEnabled = false;   // what Hit does first; DisableHit would do the same at the end of the swing
            Log.Verbose($"Auto Swing: the swing missed on purpose ({hit.collider.name} moved into the swing).");
            return false;
        }

        private static Collider RayCollider()
        {
            var i = InteractObjects.instance;
            return i != null && i.meleeHit ? i.meleeRaycastHit.collider : null;
        }

        private static bool IsHeld(MeleeWeapon w)
        {
            var hands = PlayerHandItems.instance;
            return hands != null && w != null && ReferenceEquals(hands.righthandItem, w);
        }

        // The collider under the ray MeleeWeapon.Hit uses (InteractObjects.meleeRaycastHit) is a WorkableResourceSource
        // with stock that takes this tool's damage type, never a person, and by default never someone else's node.
        private static bool TargetOk(MeleeWeapon w, Collider col, out string name)
        {
            name = null;
            if (col == null) return false;
            if (col.GetComponentInParent<NPCInfo>() != null || col.GetComponentInParent<CharacterBarkStarter>() != null) return false;
            var src = col.GetComponent<WorkableResourceSource>();
            if (src == null || Vanilla.CurrentStock(src) < 1) return false;
            if (!src.CheckDamageType(Vanilla.DamageType(w))) return false;
            if (Cfg.AutoSwing.SkipOwnedSources.Value && src.owner != null && src.owner.ID > 1 && !src.permissionToTake) return false;
            name = src.name;
            return true;
        }

        private static void EndSession()
        {
            if (_session) Log.Verbose($"Auto Swing: stopped on {_sessionTarget ?? "?"} after {_swings} swings.");
            _session = false;
            _swings = 0;
            _sessionTarget = null;
        }

        private static string ToolName(MeleeWeapon w)
        {
            try { return w.item != null ? w.item.Title : w.name; }
            catch (Exception) { return "?"; }
        }
    }

    // P3: MeleeWeapon.onPrimaryHold() (SRC/WieldableItem/MeleeWeapon.cs:491). Reached only from PlayerHandItems.PrimaryHold
    // in GameController.Update while Primary Use is held (:197-200), after every menu, pause, dialogue, shop, keysDisabled,
    // itemUseDisabled, quick-slot and console gate. Any exception falls back to vanilla and suspends Auto Swing for the scene.
    [HarmonyPatch]
    internal static class OnPrimaryHoldPatch
    {
        private static MethodBase TargetMethod() => Vanilla.OnPrimaryHold;

        private static bool Prefix(MeleeWeapon __instance)
        {
            try { return AutoSwing.Prefix(__instance); }
            catch (Exception e)
            {
                AutoSwing.Suspend("auto swing prefix", e);
                return true;
            }
        }

        private static void Postfix(MeleeWeapon __instance)
        {
            try { AutoSwing.Postfix(__instance); }
            catch (Exception e) { AutoSwing.Suspend("auto swing postfix", e); }
        }
    }

    // P3b: MeleeWeapon.Hit(RaycastHit) (private, SRC/WieldableItem/MeleeWeapon.cs:266). Only skips the hit of a swing
    // Auto Swing released whose ray no longer points at a valid resource; any exception lets vanilla run.
    [HarmonyPatch]
    internal static class MeleeHitPatch
    {
        private static MethodBase TargetMethod() => Vanilla.MeleeHit;

        private static bool Prefix(MeleeWeapon __instance, RaycastHit hit)
        {
            try { return AutoSwing.HitPrefix(__instance, hit); }
            catch (Exception e)
            {
                AutoSwing.Suspend("auto swing hit", e);
                return true;
            }
        }
    }
}
