using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace UnofficialPatch
{
    // U-015 the porta-potty door closes in your face (design.md "U-015"). Scope: the cabin doors of the porta potties
    // and outhouses, nothing else. Every cabin has the same Door data (State Closed, AutoClose 0, ManualClose 1,
    // CloseDistance 3, noOppositeDirection 1, unlocked, no relays) and no NPC Waypoint_Door, so the only vanilla
    // closers are the player's own E (Door.UseDoor -> ToggleInterrupt -> Toggle -> Close, SRC/Door.cs:379-454, :620-661)
    // and the 3 m close trigger (Door.Start capsule, :877-886; OnTriggerExit on the Player tag -> Close, :1049-1055),
    // which the player cannot leave geometrically from inside the cabin. The user saw the door close by itself, so
    // KeepCabinDoorOpen ships on: a bool prefix on Door.Close skips the close of a cabin door that does not come from
    // the player's own interaction (a depth counter around Door.UseDoor, which Door.Interact, DoorLockBypass and
    // BuildingSystemMenu all go through) while the player sits on that cabin's toilet, stands inside the cabin (within
    // 1.2 m of the toilet and on its side of the closed doorway plane), or is still at the cabin (within 2 m of the
    // toilet) less than CabinDoorGraceSeconds after standing up (InteractableChair.StopSitEnd, which ends both
    // StopSit and ForceStandUp, SRC/InteractableChair.cs:582-631). A skipped close is remembered: once the player is
    // farther from the doorway than the door's CloseDistance, the door is closed, so after leaving it closes as in
    // vanilla (each kept door separately: the Mines potties stand in a row inside each other's close distance).
    // Closing it with E and walking away work as before.
    //
    // Cabin resolution: from the door (or the toilet), walk up at most 3 parents to the first object whose name
    // starts with "portapotty_usable" (also "portapotty_usable (1)" in the Mines and "(Clone)" copies) or
    // "outhouse_001_usable"; that object must hold exactly one Door and exactly one Toilet, within 2.5 m of each other.
    // Checked in the scene and prefab YAML: Open Sewer Tenement (market area, plus the yard lvl0 copy whose door is
    // inactive), Open Sewer Mines (8 instances, 2 under inactive map groups, 2 more in inactive yard copies), Interior
    // Bazaar Pontikka, Interior Mines Potato Mine (outhouse), Upgrade_PLAYER_Pier_Sauna_(lvl2) - Shabby_0 (outhouse,
    // spawned at runtime), and the Upgrade_PLAYER_Yard_(lvl 0) - Bare copies (door inactive, never opened). The
    // portapotty_001_insert_usable toilets (Barrack House, Baths, LotShack3) have no Door and never match. Public
    // toilet stalls and every other seat are out of scope. If nothing resolves, vanilla runs.
    //
    // Diagnostic (General.VerboseLogging only): every Close() on an open cabin door with its caller chain and whether
    // it was kept, a mid-swing E that reverses the door (ToggleInterrupt, which never calls Close), player-tagged
    // trigger exits, and sit and stand-up times. With KeepCabinDoorOpen off and VerboseLogging on at launch, the
    // patches are applied anyway ("cabin doors (log only)") and only log, so the vanilla closer can be observed. Closes that bypass Close() (the ToggleInterrupt flip, the State /
    // position mismatch and AutoClose branches of Door.Update, :1012-1033) are only logged, never stopped.
    // No data stored; the door's position and State are vanilla saved fields.
    internal static class CabinDoors
    {
        internal static bool Patched;

        private static readonly string[] RootPrefixes = { "portapotty_usable", "outhouse_001_usable" };
        private const int MaxWalk = 3;
        private const float MaxDoorToToilet = 2.5f;
        private const float InsideRadius = 1.2f;
        private const float NearRadius = 2f;

        private static readonly Dictionary<int, Toilet> DoorToToilet = new Dictionary<int, Toilet>();
        private static readonly Dictionary<int, Door> ToiletToDoor = new Dictionary<int, Door>();

        internal static int UseDepth;          // > 0 while Door.UseDoor runs (the player's own interaction)
        private static bool _ownClose;
        private static int _standToilet;
        private static float _standTime = -1000f;
        private static readonly HashSet<Door> Pending = new HashSet<Door>();   // kept open; closed once you walk away
        private static readonly List<Door> Work = new List<Door>();

        internal static bool Live => Patched && Cfg.KeepCabinDoorOpen.Value;

        // ---- Resolution ----

        internal static Toilet ToiletOf(Door d)
        {
            if (d == null) return null;
            int id = d.GetInstanceID();
            if (!DoorToToilet.TryGetValue(id, out var t))
            {
                Resolve(d.transform, out var door, out t);
                if (door != d) t = null;
                DoorToToilet[id] = t;
            }
            return t;
        }

        internal static Door DoorOf(Toilet t)
        {
            if (t == null) return null;
            int id = t.GetInstanceID();
            if (!ToiletToDoor.TryGetValue(id, out var d))
            {
                Resolve(t.transform, out d, out var toilet);
                if (toilet != t) d = null;
                ToiletToDoor[id] = d;
            }
            return d;
        }

        private static void Resolve(Transform from, out Door door, out Toilet toilet)
        {
            door = null;
            toilet = null;
            var t = from.parent;
            for (int i = 0; i < MaxWalk && t != null; i++, t = t.parent)
            {
                if (!IsCabinRoot(t.name)) continue;
                var doors = t.GetComponentsInChildren<Door>(true);
                var toilets = t.GetComponentsInChildren<Toilet>(true);
                if (doors.Length != 1 || toilets.Length != 1) return;
                doors[0].GetClosedDoorway(out Vector3 c, out _);
                if (Horizontal(c, toilets[0].transform.position) > MaxDoorToToilet) return;
                door = doors[0];
                toilet = toilets[0];
                return;
            }
        }

        private static bool IsCabinRoot(string name)
        {
            foreach (var p in RootPrefixes)
                if (name.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        // ---- Fix ----

        // Returns false to skip the close.
        internal static bool AllowClose(Door d, MethodBase original)
        {
            if (!Patched || _ownClose || d == null || !d.doorIsOpen) return true;
            var t = ToiletOf(d);
            if (t == null) return true;
            bool player = UseDepth > 0;
            string reason = player ? null : KeepReason(t, d);
            bool keep = reason != null && Cfg.KeepCabinDoorOpen.Value && !Compat.Foreign(original, "the cabin door fix");
            if (Log.VerboseOn)
                Log.Verbose($"Cabin doors: Close() on {Name(d)} from {Callers()}; " +
                    (player ? "your own E, allowed." : reason == null ? "allowed." : keep ? $"kept open ({reason})." : $"allowed, the fix is off or stepping aside ({reason})."));
            if (!keep) return true;
            Pending.Add(d);
            return false;
        }

        private static string KeepReason(Toilet t, Door d)
        {
            if (t.PlayerSittingHere) return "you are sitting on its toilet";
            var pl = PlayerLocator.instance;
            if (pl == null) return null;
            Vector3 p = pl.transform.position;
            Vector3 tp = t.transform.position;
            float dist = Horizontal(p, tp);
            if (dist <= InsideRadius && Inside(d, p, tp)) return "you are inside the cabin";
            float since = Time.time - _standTime;
            if (_standToilet == t.GetInstanceID() && since < Cfg.CabinDoorGraceSeconds.Value && dist <= NearRadius)
                return $"you stood up {since:0.0} s ago";
            return null;
        }

        // Same side of the closed doorway plane as the toilet.
        private static bool Inside(Door d, Vector3 p, Vector3 toilet)
        {
            d.GetClosedDoorway(out Vector3 c, out Vector3 n);
            float sp = Vector3.Dot(p - c, n), st = Vector3.Dot(toilet - c, n);
            return sp * st > 0f;
        }

        // Called every frame from Plugin.Update: each kept-open door closes once you are past its close distance.
        internal static void Update()
        {
            if (Pending.Count == 0) return;
            Work.Clear();
            Work.AddRange(Pending);
            var pl = PlayerLocator.instance;
            foreach (var d in Work)
            {
                if (d == null || !Live || !d.doorIsOpen)
                {
                    Pending.Remove(d);
                    continue;
                }
                var t = ToiletOf(d);
                if (t == null || pl == null || t.PlayerSittingHere) continue;
                d.GetClosedDoorway(out Vector3 c, out _);
                if (Horizontal(pl.transform.position, c) <= d.CloseDistance) continue;
                Pending.Remove(d);
                _ownClose = true;
                try { d.Close(); }
                finally { _ownClose = false; }
                if (Log.VerboseOn) Log.Verbose($"Cabin doors: you walked away; closed {Name(d)}.");
            }
            Work.Clear();
        }

        internal static void OnStandUp(InteractableChair chair)
        {
            if (!(chair is Toilet t) || DoorOf(t) == null) return;
            _standToilet = t.GetInstanceID();
            _standTime = Time.time;
            if (Log.VerboseOn)
                Log.Verbose($"Cabin doors: stood up from {Name(t.transform)} at {Time.time:0.00} s; door {(DoorOf(t).doorIsOpen ? "open" : "closed")}.");
        }

        internal static void OnSat(Toilet t)
        {
            if (!Log.VerboseOn) return;
            var d = DoorOf(t);
            if (d == null) return;
            Log.Verbose($"Cabin doors: sat down on {Name(t.transform)} at {Time.time:0.00} s; door {(d.doorIsOpen ? "open" : "closed")}.");
        }

        internal static void OnToggleInterrupt(Door d)
        {
            if (!Log.VerboseOn || d == null || !d.moving || ToiletOf(d) == null) return;
            bool opening = Vanilla.DoorDirection != null && Vanilla.DoorDirection(d);
            Log.Verbose($"Cabin doors: E during the swing of {Name(d)} turns it from {(opening ? "opening to closing" : "closing to opening")} " +
                $"(not a Close() call; {(Live ? "not stopped by the fix" : "fix off")}).");
        }

        internal static void OnTriggerExit(Door d, Collider other)
        {
            if (!Log.VerboseOn || d == null || other == null || !other.CompareTag("Player") || ToiletOf(d) == null) return;
            var pl = PlayerLocator.instance;
            d.GetClosedDoorway(out Vector3 c, out _);
            string dist = pl != null ? $"{Horizontal(pl.transform.position, c):0.00} m" : "?";
            Log.Verbose($"Cabin doors: {other.name} left the close trigger of {Name(d)} at {dist} from the doorway " +
                $"(sitting: {ToiletOf(d).PlayerSittingHere}).");
        }

        internal static void OnLoadingDone()
        {
            DoorToToilet.Clear();
            ToiletToDoor.Clear();
            Pending.Clear();
            _standToilet = 0;
            UseDepth = 0;
        }

        // ---- Helpers ----

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        private static string Name(Door d) => Name(d.transform);

        private static string Name(Transform t)
        {
            var p = t.parent;
            for (int i = 0; i < MaxWalk && p != null; i++, p = p.parent)
                if (IsCabinRoot(p.name)) return $"{p.name} ({t.gameObject.scene.name})";
            return t.gameObject.name;
        }

        // Up to five game methods above the Close() call, Harmony wrappers and this mod skipped. Verbose only.
        private static string Callers()
        {
            var sb = new StringBuilder();
            var frames = new StackTrace(2, false).GetFrames();
            int n = 0;
            if (frames != null)
                foreach (var f in frames)
                {
                    var m = f.GetMethod();
                    var type = m?.DeclaringType;
                    if (m == null || type == null || type.Namespace == typeof(CabinDoors).Namespace) continue;
                    if (type.Assembly == typeof(Harmony).Assembly) continue;
                    if (sb.Length > 0) sb.Append(" <- ");
                    sb.Append(type.Name).Append('.').Append(m.Name.Replace("DMD<", "").TrimEnd('>'));
                    if (++n == 5) break;
                }
            return sb.Length > 0 ? sb.ToString() : "(unknown)";
        }
    }

    // Door.Close().
    [HarmonyPatch]
    internal static class DoorClosePatch
    {
        private static MethodBase TargetMethod() => Vanilla.DoorClose;

        private static bool Prefix(Door __instance, MethodBase __originalMethod)
        {
            try { return CabinDoors.AllowClose(__instance, __originalMethod); }
            catch (Exception e)
            {
                Log.Exception("Cabin doors: close prefix", e);
                return true;
            }
        }
    }

    // Door.UseDoor(bool): the player's own interaction (Door.Interact, DoorLockBypass, BuildingSystemMenu).
    [HarmonyPatch]
    internal static class DoorUseDoorPatch
    {
        private static MethodBase TargetMethod() => Vanilla.DoorUseDoor;

        private static void Prefix() => CabinDoors.UseDepth++;

        // Always runs, so the depth never stays raised.
        private static void Finalizer()
        {
            if (CabinDoors.UseDepth > 0) CabinDoors.UseDepth--;
        }
    }

    // InteractableChair.StopSitEnd(), private: the end of every stand-up (StopSit after the camera move, ForceStandUp).
    [HarmonyPatch]
    internal static class ChairStopSitEndPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ChairStopSitEnd;

        private static void Prefix(InteractableChair __instance, out bool __state)
        {
            __state = false;
            try { __state = __instance is Toilet && __instance.PlayerSittingHere; }
            catch (Exception e) { Log.Exception("Cabin doors: stand-up prefix", e); }
        }

        private static void Postfix(InteractableChair __instance, bool __state)
        {
            if (!__state) return;
            try { CabinDoors.OnStandUp(__instance); }
            catch (Exception e) { Log.Exception("Cabin doors: stand-up", e); }
        }
    }

    // ---- Diagnostic only (General.VerboseLogging) ----

    // Toilet.OnSitEnd(), protected.
    [HarmonyPatch]
    internal static class ToiletSitEndPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ToiletSitEnd;

        private static void Postfix(Toilet __instance)
        {
            try { CabinDoors.OnSat(__instance); }
            catch (Exception e) { Log.Exception("Cabin doors: sit", e); }
        }
    }

    // Door.ToggleInterrupt(bool, bool).
    [HarmonyPatch]
    internal static class DoorToggleInterruptPatch
    {
        private static MethodBase TargetMethod() => Vanilla.DoorToggleInterrupt;

        private static void Prefix(Door __instance)
        {
            try { CabinDoors.OnToggleInterrupt(__instance); }
            catch (Exception e) { Log.Exception("Cabin doors: toggle diagnostic", e); }
        }
    }

    // Door.OnTriggerExit(Collider), private.
    [HarmonyPatch]
    internal static class DoorTriggerExitPatch
    {
        private static MethodBase TargetMethod() => Vanilla.DoorTriggerExit;

        private static void Prefix(Door __instance, Collider collider)
        {
            try { CabinDoors.OnTriggerExit(__instance, collider); }
            catch (Exception e) { Log.Exception("Cabin doors: trigger diagnostic", e); }
        }
    }
}
