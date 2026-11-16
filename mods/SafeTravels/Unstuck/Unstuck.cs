using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SafeTravels
{
    // Unstuck (S-030, spec §4.2-4.3): moves the player back to the newest usable safe spot, or to the nearest entrance,
    // through the game's own PlayerLocator.Teleport. Never Entrypoint.TeleportPlayer (it fires spawn outputs, and the
    // prison entrypoint runs ImprisonPlayer) and never NegateFallDamage. The new position goes into the vanilla
    // PersistLocation like a door spawn; nothing else is stored.
    internal static class Unstuck
    {
        private const float SuspendAfter = 1f;
        private const float Lift = 0.05f;
        private const int CollisionMask = 42151937;   // OpenSewerCharacterController's own uncrouch check (:465)

        internal static bool Available;

        private static float _lastUsed = float.NegativeInfinity;   // PlayClock.Now
        private static int _pendingFrame = -1;
        private static GameObject _anchor;

        internal static bool On => Available && Cfg.Unstuck.Enabled.Value;

        internal static int CooldownLeft
        {
            get
            {
                float left = Cfg.Unstuck.CooldownSeconds.Value - (PlayClock.Now - _lastUsed);
                return left > 0f ? Mathf.CeilToInt(left) : 0;
            }
        }

        internal static void OnSceneLoaded()
        {
            _pendingFrame = -1;
            if (_anchor != null) Object.Destroy(_anchor);
            _anchor = null;
        }

        // A pause menu click: the pause menu has just been closed (Resume), so the request runs on the next frame, once
        // the pause lock is gone and time runs again.
        internal static void QueueFromPause() => _pendingFrame = Time.frameCount;

        internal static void Tick()
        {
            if (_pendingFrame >= 0 && Time.frameCount > _pendingFrame)
            {
                _pendingFrame = -1;
                if (On) Request(fromPause: true);
                return;
            }
            if (On && Keys.Down(Cfg.Unstuck.Hotkey)) Request(fromPause: false);
        }

        // The gate of spec §4.3 for Unstuck, in table order.
        internal static GateResult Gate()
        {
            var core = PlayGate.Core();
            if (core.Blocked) return core;
            var free = PlayGate.Free();
            if (free.Blocked) return free;
            string busy = PlayGate.Busy();
            if (busy != null) return GateResult.Note(Strings.Busy, busy);
            string vehicle = PlayGate.Vehicle();
            if (vehicle != null) return GateResult.Note(PlayGate.VehicleNote(vehicle), vehicle);
            if (PlayGate.Jailed) return GateResult.Note(Strings.Jail, "jail");
            if (PlayGate.Police) return GateResult.Note(Strings.Police, "police");
            if (PlayGate.Falling) return GateResult.Note(Strings.Falling, "falling");
            int cd = CooldownLeft;
            if (cd > 0) return GateResult.Note(Strings.Cooldown(cd), "cooldown");
            return GateResult.Clear;
        }

        private static void Request(bool fromPause)
        {
            try
            {
                var g = Gate();
                if (g.Blocked)
                {
                    Refuse(g, fromPause);
                    return;
                }
                var pl = PlayerLocator.instance;
                var from = pl.transform.position;
                if (!PickTarget(from, out var target, out int sampleIndex, out float sampleAge, out string entrance))
                {
                    Refuse(GateResult.Note(Strings.NoSpot, "no spot"), fromPause);
                    return;
                }
                if (ThirdPersonCameraController.instance == null)
                {
                    Log.Verbose("Unstuck: refused (no camera)");
                    return;
                }
                Teleport(pl, target);

                _lastUsed = PlayClock.Now;
                if (sampleIndex >= 0) SafeSpots.DropFrom(sampleIndex);
                SafeSpots.Suspend(SuspendAfter);
                float dist = Vector3.Distance(from, target);
                if (entrance == null)
                {
                    Game.Notify(Strings.Moved);
                    Log.Info($"Unstuck: moved {dist:0.0} m to a spot from {sampleAge:0} s ago ({Game.SceneName})");
                }
                else
                {
                    Game.Notify(Strings.MovedEntrance);
                    Log.Info($"Unstuck: moved {dist:0.0} m to entrance '{entrance}' ({Game.SceneName})");
                }
            }
            catch (Exception e) { Log.Exception("unstuck", e); }
        }

        private static void Refuse(GateResult g, bool fromPause)
        {
            Log.Verbose($"Unstuck: refused ({g.Reason})");
            if (g.Message == null) return;
            Game.Notify(g.Message);
            if (!fromPause) Game.Warning();
        }

        // The newest sample that is young enough, in range, on this side of the toll gate, outside an enforced
        // trespassing area and with room for the player's capsule; else the nearest usable entrance within
        // EntrypointMaxDistance. Ages are in play time (PlayClock), so the pause menu doesn't empty the trail. The
        // entrance is capped so a gap in the trail (a train ride, standing still) never turns into a long warp.
        private static bool PickTarget(Vector3 from, out Vector3 target, out int sampleIndex, out float sampleAge, out string entrance)
        {
            target = default;
            sampleIndex = -1;
            sampleAge = 0f;
            entrance = null;
            float now = PlayClock.Now;
            float maxAge = Cfg.Unstuck.MaxAgeSeconds.Value, minD = Cfg.Unstuck.MinDistance.Value, maxD = Cfg.Unstuck.MaxDistance.Value;
            int rejected = 0;
            string lastWhy = null;
            for (int i = 0; i < SafeSpots.Count; i++)
            {
                var s = SafeSpots.Get(i);
                if (now - s.T > maxAge) { rejected += SafeSpots.Count - i; lastWhy = "age"; break; }
                float d = Vector3.Distance(s.Pos, from);
                string why = null;
                if (d < minD || d > maxD) why = "distance";
                else if (TollSlot.AcrossGate(from, s.Pos)) why = "gate";
                else if (SafeSpots.InEnforcedTrespass(s.Pos)) why = "trespass";
                else if (!Clear(s.Pos)) why = "clearance";
                if (why != null) { rejected++; lastWhy = why; continue; }
                target = s.Pos;
                sampleIndex = i;
                sampleAge = now - s.T;
                if (rejected > 0) Log.Verbose($"Unstuck: skipped {rejected} spots (last: {lastWhy})");
                return true;
            }
            if (rejected > 0) Log.Verbose($"Unstuck: skipped {rejected} spots (last: {lastWhy})");

            if (!Cfg.Unstuck.EntrypointFallback.Value) return false;
            var eps = PlayerLevelEntrypoints.instance != null ? PlayerLevelEntrypoints.instance.Entrypoints : null;
            if (eps == null) return false;
            string prisonA = info_game_logic.instance != null ? info_game_logic.instance.prisonEntrypoint : null;
            var prison = PlayGate.CachedPrison;
            string prisonB = prison != null && prison.entrypoint != null ? prison.entrypoint.Name : null;
            float best = Cfg.Unstuck.EntrypointMaxDistance.Value;
            int far = 0;
            foreach (var ep in eps)
            {
                if (ep == null || ep.Location == null || ep.dontUseAsDefaultSpawn) continue;
                string n = ep.Name;
                if (n == "UnderMapDrop" || n == "prison" || (!string.IsNullOrEmpty(prisonA) && n == prisonA) || (!string.IsNullOrEmpty(prisonB) && n == prisonB))
                    continue;
                var p = ep.Location.position;
                float d = Vector3.Distance(p, from);
                if (d > Cfg.Unstuck.EntrypointMaxDistance.Value) { far++; continue; }
                if (d >= best) continue;
                string why = null;
                if (TollSlot.AcrossGate(from, p)) why = "gate";
                else if (SafeSpots.InEnforcedTrespass(p)) why = "trespass";
                else if (!Clear(p)) why = "clearance";
                if (why != null) { Log.Verbose($"Unstuck: entrance '{n}' skipped ({why})"); continue; }
                best = d;
                target = p;
                entrance = string.IsNullOrEmpty(n) ? "(unnamed)" : n;
            }
            if (entrance == null && far > 0) Log.Verbose($"Unstuck: {far} entrances further than {Cfg.Unstuck.EntrypointMaxDistance.Value:0} m");
            return entrance != null;
        }

        // Room for the standing capsule at p (feet position), against the same layers the game's uncrouch check uses.
        private static bool Clear(Vector3 p)
        {
            var occ = OpenSewerCharacterController.instance;
            if (occ == null || occ.m_Capsule == null) return true;
            float r = occ.m_Capsule.radius * 0.95f;
            var bottom = p + Vector3.up * (r + Lift);
            var top = p + Vector3.up * (occ.m_CapsuleHeight - r + Lift);
            return !Physics.CheckCapsule(bottom, top, r, CollisionMask, QueryTriggerInteraction.Ignore);
        }

        // Our own anchor rather than the entrypoint's Location, so its scale and parent never reach the player: same
        // parent as the player (PersistLocation stores local values for a local player), the player's own yaw and scale.
        private static void Teleport(PlayerLocator pl, Vector3 target)
        {
            if (_anchor == null)
            {
                _anchor = new GameObject("SafeTravels Anchor") { hideFlags = HideFlags.HideAndDontSave };
            }
            var t = _anchor.transform;
            if (t.parent != pl.root) t.SetParent(pl.root, false);
            var player = pl.transform;
            t.position = target;
            t.rotation = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
            t.localScale = player.localScale;
            pl.Teleport(t);
        }
    }
}
