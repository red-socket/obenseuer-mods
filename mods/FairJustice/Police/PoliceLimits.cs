using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NPC;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace FairJustice
{
    // U-014 part B (spec §3.7), both off by default: following the player indoors and opening locked doors are
    // Loiste's design (0.4.06 V2), so these only ever make a chase end early, the way vanilla ends one when an officer
    // loses sight of you (SRC/NPC/Act_Police.cs:751, :775). Patrols, wandering, searching and scheduled crossings are
    // never touched, and no door, lock or route is changed. Nothing is saved: a stopped officer is in a vanilla search
    // state. Fair Justice never references Unofficial Patch; with its NpcDoors fix on, the locked-door stop probes a superset
    // of that fix's probe, so a pursuing officer is stopped before it would open the door (design.md §3.7B).
    internal static class PoliceLimits
    {
        internal static bool ScenePatched;

        private static MethodBase _follow;
        internal static MethodBase FollowTarget =>
            _follow ?? (_follow = AccessTools.Method(typeof(NPCSceneUtilities), nameof(NPCSceneUtilities.OnFollowingTarget),
                new[] { typeof(NPCData), typeof(GameObject) }));

        // P26 body. True: the chase ends here and vanilla does not start the transition.
        internal static bool StopAtScene(NPCData npc, GameObject followTarget)
        {
            if (npc?.state == null || followTarget == null) return false;
            if (!(npc.state.CurrentActivity is Act_Police p) || p.state != Act_Police.State.ChasePlayer) return false;
            string scene = followTarget.scene.name;
            if (string.IsNullOrEmpty(scene) || !Cfg.NoFollowSceneSet.Contains(scene)) return false;
            if (npc.state.currentScene == scene) return false;   // already there: vanilla does nothing either
            if (Compat.Foreign(FollowTarget, "StopChaseAtTenement")) return false;
            // A transition into a listed level that started earlier (option just turned on) is dropped as well, the way
            // Crime.StopPolicePursuits drops transitions (SRC/Crime.cs:350).
            if (npc.state.activePath != null && npc.state.targetScene != null && Cfg.NoFollowSceneSet.Contains(npc.state.targetScene))
                npc.state.ResetTransition();
            // Off-scene, StartState returns at once (_NPC is null, :620-626); the officer stays where he was, searching.
            p.StartNewState(Act_Police.State.SearchingPlayerAgressive, Random.Range(1000, 3600));
            Log.Info($"Police chase stopped at {scene}: {npc.character?.FullName ?? "an officer"} searches for you outside.");
            return true;
        }
    }

    // StopAtLockedDoors (no patch). Runs from Plugin.Update while the option is on and the vanilla members were found.
    // For each live officer in ChasePlayer or QuestionPlayer it probes a horizontal segment from BackProbe behind him to
    // max(speed * Lookahead, MinProbe) ahead along his RichAI velocity, and looks for a locked Door that is closed or
    // swinging shut, whose doorway that segment crosses with you on the far side. Then it stops him where he stands and
    // starts the vanilla search state for that chase (ChasePlayer -> SearchingPlayerAgressive, QuestionPlayer ->
    // SearchingPlayer), as vanilla does when he loses sight of you.
    // The probe is a superset of Unofficial Patch's NpcDoors probe (same velocity source; longer reach, more behind,
    // wider and taller doorway band, twice the rate) as long as Lookahead is at least that fix's Lookahead. Only the
    // stop is guaranteed: once he is searching, his route is vanilla and may still take him through the door
    // (design.md §3.7B).
    internal static class LockedDoorStop
    {
        private const float Interval = 0.05f;        // twice the rate of Unofficial Patch's 0.1 s door probe
        private const float MinSpeed = 0.2f;         // m/s; Unofficial Patch ignores NPCs under 0.3
        private const float MaxSpeed = 15f;          // above this the velocity is a teleport, not a walk
        private const float MinProbe = 1.5f;         // m ahead, at least (Unofficial Patch: 1 m)
        private const float BackProbe = 0.4f;        // m behind (Unofficial Patch: 0.3 m)
        private const float MinHalfWidth = 1.2f;     // GetClosedDoorway's centre can be the hinge when a door has no box collider
        private const float LateralMargin = 0.5f;    // beside a boxed doorway (Unofficial Patch: 0.4 m)
        private const float BelowCenter = 1.5f, AboveCenter = 3.5f;   // doorway centre height range from his feet (UP: 1, 3)
        private const float RefreshSeconds = 10f;    // upgrade doors appear on renovation without a scene load

        private struct Geom
        {
            internal Door door;
            internal Vector3 center, normal, lateral;
            internal float half;
        }

        private static readonly List<Geom> Doors = new List<Geom>();
        private static bool _haveDoors;
        private static float _doorsAt = -999f, _next;

        internal static bool Live => Cfg.StopAtLockedDoors.Value && Vanilla.HasPolice;

        // sceneLoaded and LoadingDone: doors belong to the old level.
        internal static void Reset()
        {
            Doors.Clear();
            _haveDoors = false;
        }

        internal static void Tick()
        {
            // Not HasControl: police keep moving while you read, wait or sleep, and Unofficial Patch keeps opening doors.
            if (!Live || !Game.WorldRunning) return;
            float now = Time.time;
            if (now < _next) return;
            _next = now + Interval;
            var list = Vanilla.ActivePolices(Crime.instance);
            if (list == null || list.Count == 0) return;
            var player = PlayerLocator.instance;
            if (player == null) return;
            Vector3 you = player.transform.position;
            float lookahead = Cfg.LockedDoorLookahead.Value;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null || !Pursuing(p)) continue;
                var npc = Vanilla.ActivityNpc(p);
                if (npc == null || !npc.isActiveAndEnabled) continue;   // off-scene officers are in the list too (Act_Police.cs:213-221)
                // The instantaneous velocity Unofficial Patch reads (NpcDoors.Probe), so a chase that just started or a
                // corner just turned is tested on the first tick.
                Vector3 v = npc.richAI != null ? npc.richAI.velocity : (npc.ai != null ? npc.ai.velocity : Vector3.zero);
                v.y = 0f;
                float speed = v.magnitude;
                if (speed < MinSpeed || speed > MaxSpeed) continue;
                Vector3 dir = v / speed;
                Vector3 pos = npc.transform.position;
                float len = Mathf.Max(speed * lookahead, MinProbe);
                var door = LockedDoorAhead(pos, pos - dir * BackProbe, pos + dir * len, len, you);
                if (door != null) Stop(p, npc, door);
            }
        }

        private static bool Pursuing(Act_Police p) =>
            p.state == Act_Police.State.ChasePlayer || p.state == Act_Police.State.QuestionPlayer;

        // Closed, or swinging shut: Door.Close sets moving (direction false) while doorIsOpen stays true until
        // OnFullyClosed (SRC/Door.cs:605-617, :765-771). A door that is opening or open is left alone.
        private static bool ClosedOrClosing(Door d)
        {
            if (!d.doorIsOpen) return !d.moving;
            return Vanilla.DoorClosing(d);
        }

        // Locked, or a lockWhenClosed door that is still open and locks itself once shut (Door.OnFullyClosed,
        // SRC/Door.cs:783). Combined with ClosedOrClosing, the latter only counts while it swings shut.
        private static bool LockedOrLocking(Door d) =>
            (d._Lock != null && d._Lock.Locked) || (d.lockWhenClosed && d.doorIsOpen);

        private static Door LockedDoorAhead(Vector3 pos, Vector3 a, Vector3 b, float len, Vector3 you)
        {
            Refresh();
            for (int i = 0; i < Doors.Count; i++)
            {
                var g = Doors[i];
                var d = g.door;
                if (d == null || !d.gameObject.activeInHierarchy) continue;
                if (!LockedOrLocking(d) || !ClosedOrClosing(d)) continue;
                float reach = len + g.half + 1.5f;
                float dx = g.center.x - pos.x, dz = g.center.z - pos.z;
                if (dx * dx + dz * dz > reach * reach) continue;
                float dy = g.center.y - pos.y;
                if (dy < -BelowCenter || dy > AboveCenter) continue;
                float da = Dot(a - g.center, g.normal), db = Dot(b - g.center, g.normal);
                if (da > 0f == db > 0f || Mathf.Approximately(da, db)) continue;    // the segment doesn't cross the doorway plane
                if (Dot(you - g.center, g.normal) * Dot(pos - g.center, g.normal) >= 0f) continue;   // you are on his side (or he is through)
                Vector3 hit = a + (b - a) * (da / (da - db));
                if (Mathf.Abs(Dot(hit - g.center, g.lateral)) > g.half) continue;
                return d;
            }
            return null;
        }

        private static void Refresh()
        {
            if (_haveDoors && Time.time - _doorsAt <= RefreshSeconds) return;
            _haveDoors = true;
            _doorsAt = Time.time;
            Doors.Clear();
            foreach (var d in Object.FindObjectsByType<Door>(FindObjectsSortMode.None))
            {
                if (d == null) continue;
                d.GetClosedDoorway(out Vector3 c, out Vector3 n);
                n.y = 0f;
                if (n.sqrMagnitude < 1e-4f) continue;   // a hatch or trapdoor: no vertical doorway to cross
                n.Normalize();
                var g = new Geom { door = d, center = c, normal = n, lateral = Vector3.Cross(Vector3.up, n), half = MinHalfWidth };
                // The doorway's width, as Unofficial Patch's NpcDoors.Build reads it: GetClosedDoorway's normal is the
                // box's thin axis, the width is the other horizontal axis.
                var box = d.GetComponent<BoxCollider>();
                if (box != null)
                {
                    Vector3 s = d.transform.lossyScale;
                    float w = box.size.z < box.size.x ? box.size.x * Mathf.Abs(s.x) : box.size.z * Mathf.Abs(s.z);
                    if (w > 0.05f) g.half = Mathf.Max(MinHalfWidth, w * 0.5f + LateralMargin);
                }
                Doors.Add(g);
            }
        }

        private static float Dot(Vector3 a, Vector3 n) => a.x * n.x + a.z * n.z;

        private static void Stop(Act_Police p, NPCController npc, Door d)
        {
            bool chase = p.state == Act_Police.State.ChasePlayer;
            npc.SetFollowDestination(npc.transform.position, 0.1f);   // as ChasingPlayer's standoff does (Act_Police.cs:765)
            if (chase) p.StartNewState(Act_Police.State.SearchingPlayerAgressive, Random.Range(1000, 3600));
            else p.StartNewState(Act_Police.State.SearchingPlayer);
            Log.Verbose($"Police {(chase ? "chase" : "questioning")} stopped at the locked door {d.name}: " +
                        $"{npc.Data?.character?.FullName ?? "an officer"} searches for you instead.");
        }
    }

    // P26: NPC.NPCSceneUtilities.OnFollowingTarget(NPCData npc, GameObject followTarget) (SRC/NPC/NPCSceneUtilities.cs:11),
    // called every game minute by NPCDirector for an officer chasing you (Act_Police.GetFollowTarget, :127-134). It is the
    // only place a chase starts a scene transition after you.
    [HarmonyPatch]
    internal static class FollowTargetPatch
    {
        private static MethodBase TargetMethod() => PoliceLimits.FollowTarget;

        private static bool Prefix(NPCData npc, GameObject followTarget)
        {
            if (!PoliceLimits.ScenePatched || !Cfg.StopChaseAtTenement.Value) return true;
            try { return !PoliceLimits.StopAtScene(npc, followTarget); }
            catch (Exception e)
            {
                Log.Exception("OnFollowingTarget prefix", e);
                return true;
            }
        }
    }
}
