using System;
using System.Collections.Generic;
using NPC;
using UnityEngine;

namespace UnofficialPatch
{
    // U-014 police walk through closed doors and the toll gate (design.md "U-014"). Three vanilla mechanisms combine:
    // NPC bodies are kinematic and moved by MovePosition (AstarPathfindingProject/Pathfinding/AIBase.cs:539-543), so
    // only the navmesh limits them; the shipped navmeshes run straight under many closed doors and the toll gate; and a
    // chase drives RichAI straight at the player (NPCController.SetChaseDestination -> SetFollowDestination,
    // SRC/NPCController.cs:435-476), which skips Waypoint_Door, the only generic NPC door opener
    // (SRC/NPC/Waypoint_Door.cs:60-174). The fix is visual: an NPC about to cross a closed doorway opens the door with
    // the vanilla door calls, and the door closes again after it passes. Paths, locks and routes are never changed.
    //
    // Every 0.1 s (game time) each live NPCController that is police (CurrentActivity is Act_Police), or any NPC with
    // ApplyToAllNpcs, and is not in a vanilla waypoint chain (private inWaypointchain, :46) is probed: a horizontal
    // segment from 0.3 m behind it to max(speed * Lookahead, 1 m) ahead along its RichAI velocity is tested against
    // the closed doorway of every Door nearby. The doorway is the door's BoxCollider at its closed pose (the matrix of
    // Door.GetClosedDoorway, SRC/Door.cs:900-920), with the thin axis taken in world space: GetClosedDoorway itself only
    // ever picks local X or Z as the thin axis (:916), which is wrong for doors from rotated models whose box is thin
    // along local Y (Kolhola pods, the Tenement B floodgate, wall hatches, the border city gates). A door whose thin
    // axis is vertical is a hatch and is excluded. A door that is closed or closing is opened when vanilla would let
    // that NPC through: unlocked doors always; locked doors (or lockWhenClosed doors still open) only with
    // OpenLockedDoors, a Waypoint_Door that references the door and whose TravelsableByNPC agrees (policeCanOpen for
    // police, the key list for others; :23-58), and only where GetClosedDoorway reads the doorway right (Fair Justice
    // probes that doorway; see below). The waypoint's onDoorOpen relay fires first, as in OpenDoorBasedOnNPCPosition
    // (:106-139); then a sliding door gets ForceOpen and a hinged door ForceOpen or ForceOpenOpposite by the same side
    // rule the player's own Door.UseDoor(bypassLock: true) uses (SRC/Door.cs:394-454), so it swings the way it swings
    // for the player. ForceOpen ignores the lock and never changes it (:509-523). The other half of a double door
    // (same parent, same kind, coplanar and side by side: the toll gate's Bridge_door_left/right) opens and is tracked
    // with it; at the toll gate only the left half has Waypoint_Doors, and their relays only ForceOpen the right half.
    //
    // Closing (Door.Close, :605). Strict rule, for doors the player cannot pass on their own (locked, lockWhenClosed
    // while open, or interactDisabled like the toll gate and scripted gates): once the NPCs are 0.6 m past, or after
    // MaxOpenSeconds at the latest, whatever the player's distance; then, while a locked one stays locked, any reopening
    // (the player's E on an open locked door reverses it, :389-392) is closed again on the next tick until it is shut.
    // Strict doors still open are snapped shut (CloseInstantly, plus Lock for lockWhenClosed) when the game saves
    // (SavingStarted) and when the player changes level, so no save holds them open. Other doors: once every NPC it was
    // opened for is CloseAfterMeters past the doorway (or turned away, or after 15 s); if the player is inside the
    // door's close trigger (:877-886) the door is left open and watched, and closed as soon as the player is out of
    // that trigger (vanilla's OnTriggerExit, :1049-1055, usually gets there first).
    //
    // Excluded: inactive or disabled doors (Door.Update would never move them), elevator doors (Elevator owns them) and
    // hatches. Fair Justice's StopAtLockedDoors (if present) stops a pursuing officer before this probe reaches a
    // closed or closing locked door (its probe is a superset of this one on the doorway GetClosedDoorway gives, which
    // is why locked doors are opened only where that doorway is right), so a stopped officer has no velocity and
    // nothing is opened for him; the two plugins never reference each other (design.md "U-014", Fair Justice note).
    // No patches; no data stored; nothing saved but vanilla door state.
    internal static class NpcDoors
    {
        internal static bool Ready;

        private const float TickSeconds = 0.1f;
        private const float NpcRefreshSeconds = 1f;
        private const float DoorRefreshSeconds = 5f;
        private const float MinSpeed = 0.3f;          // m/s; slower NPCs are standing
        private const float MaxSpeed = 15f;           // above this the velocity is a teleport, not a walk
        private const float MinProbe = 1f;            // m ahead, at least
        private const float BackProbe = 0.3f;         // m behind, so an NPC already in the doorway still counts
        private const float LateralMargin = 0.4f;     // m beside the doorway (NPC radius plus slack)
        private const float DefaultHalfWidth = 0.6f;  // doors without a BoxCollider
        private const float LockedPastMeters = 0.6f;
        private const float ReleaseMeters = 1f;       // this much farther back than when the door opened: it turned away
        private const float UnlockedMaxSeconds = 15f;
        private const float StrictWatchSeconds = 30f;
        private const float TriggerMargin = 0.2f;     // the player counts as inside the close trigger this far within it

        private sealed class DoorInfo
        {
            internal Door Door;
            internal Waypoint_Door Waypoint;
            internal DoorInfo Partner;    // the other half of a double door
            internal Vector3 Center, Normal, Lateral;
            internal float HalfWidth;
            internal bool Excluded;
            internal bool VanillaAxis;    // GetClosedDoorway's normal agrees with the real doorway
        }

        private sealed class Track
        {
            internal DoorInfo Info;
            internal readonly List<NPCController> Npcs = new List<NPCController>();
            internal readonly List<float> Sides = new List<float>();    // +1 / -1: the side of the doorway it came from
            internal readonly List<float> Starts = new List<float>();   // its distance from the doorway when it was opened
            internal float OpenedAt;
            internal bool Strict;         // the player could not pass the door when it was opened
            internal bool Watch;          // ... because it was locked: re-close it if the player reopens it
            internal bool Closing;        // strict rule: closed, watching until it is shut
            internal float ClosingSince;
            internal bool Handed;         // left open while the player is in its close trigger
        }

        private static readonly Dictionary<int, DoorInfo> Doors = new Dictionary<int, DoorInfo>();
        private static readonly List<DoorInfo> Active = new List<DoorInfo>();
        private static readonly Dictionary<int, Track> Tracks = new Dictionary<int, Track>();
        private static readonly List<int> Finished = new List<int>();
        private static readonly HashSet<int> Seen = new HashSet<int>();
        private static NPCController[] _npcs = new NPCController[0];

        private static float _nextTick, _nextNpcs, _nextDoors;
        private static int _scene = int.MinValue;
        private static bool _wasOn;

        internal static void Update()
        {
            if (!Ready) return;
            if (!Cfg.OpenDoors.Value)
            {
                if (_wasOn) Stop();
                return;
            }
            _wasOn = true;

            float now = Time.time;
            if (now < _nextTick) return;
            _nextTick = now + TickSeconds;

            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (scene != _scene)
            {
                Reset();
                _scene = scene;
            }
            if (now >= _nextNpcs)
            {
                _npcs = UnityEngine.Object.FindObjectsByType<NPCController>(FindObjectsSortMode.None);
                _nextNpcs = now + NpcRefreshSeconds;
            }
            if (now >= _nextDoors)
            {
                RefreshDoors();
                _nextDoors = now + DoorRefreshSeconds;
            }

            UpdateTracks(now);
            if (Active.Count == 0) return;

            bool all = Cfg.ApplyToAllNpcs.Value;
            float lookahead = Cfg.Lookahead.Value;
            foreach (var c in _npcs)
            {
                if (c == null || !c.isActiveAndEnabled) continue;
                var act = c.Data?.state?.CurrentActivity;
                if (!(act is Act_Police) && !all) continue;
                if (Vanilla.InWaypointChain(c)) continue;
                Probe(c, lookahead, now);
            }
        }

        private static void Probe(NPCController c, float lookahead, float now)
        {
            Vector3 v = c.richAI != null ? c.richAI.velocity : (c.ai != null ? c.ai.velocity : Vector3.zero);
            v.y = 0f;
            float speed = v.magnitude;
            if (speed < MinSpeed || speed > MaxSpeed) return;
            Vector3 dir = v / speed;
            Vector3 pos = c.transform.position;
            float len = Mathf.Max(speed * lookahead, MinProbe);
            Vector3 a = pos - dir * BackProbe;
            Vector3 b = pos + dir * len;

            for (int i = 0; i < Active.Count; i++)
            {
                var info = Active[i];
                var d = info.Door;
                if (d == null) continue;
                float reach = len + info.HalfWidth + 1f;
                float dx = info.Center.x - pos.x, dz = info.Center.z - pos.z;
                if (dx * dx + dz * dz > reach * reach) continue;
                float dy = info.Center.y - pos.y;
                if (dy < -1f || dy > 3f) continue;

                float da = Dot(a - info.Center, info.Normal), db = Dot(b - info.Center, info.Normal);
                if (da > 0f == db > 0f || Mathf.Approximately(da, db)) continue;   // does not cross the doorway plane
                Vector3 hit = a + (b - a) * (da / (da - db));
                if (Mathf.Abs(Dot(hit - info.Center, info.Lateral)) > info.HalfWidth + LateralMargin) continue;

                if (!NeedsOpening(d)) continue;
                if (!MayOpen(info, c)) continue;
                Open(info, c, pos, now);
            }
        }

        // Closed, or closing. A door that is open or opening (by anyone) is left alone.
        private static bool NeedsOpening(Door d)
        {
            if (!d.doorIsOpen) return !d.moving;
            return d.moving && !Vanilla.DoorDirection(d);
        }

        // Locked, or locks itself once closed (Door.OnFullyClosed, SRC/Door.cs:783).
        private static bool LockedNow(Door d) => (d._Lock != null && d._Lock.Locked) || (d.lockWhenClosed && d.doorIsOpen);

        // The player cannot pass it on their own: locked, or scripted (interactDisabled, like the toll gate).
        private static bool Barrier(Door d) => LockedNow(d) || d.IsInteractDisabled();

        private static bool MayOpen(DoorInfo info, NPCController c)
        {
            var d = info.Door;
            if (!LockedNow(d)) return true;
            // Fair Justice's StopAtLockedDoors sees a locked door through GetClosedDoorway; where that doorway is wrong it
            // would not stop the officer, so such a locked door is not opened either (he clips through, as in vanilla).
            if (!info.VanillaAxis) return false;
            if (!Cfg.OpenLockedDoors.Value || info.Waypoint == null || c.Data == null) return false;
            return info.Waypoint.TravelsableByNPC(new NPCReference(c.Data));
        }

        private static void Open(DoorInfo info, NPCController c, Vector3 pos, float now)
        {
            var d = info.Door;
            var p = info.Partner;
            bool pair = p != null && p.Door != null && p.Door.isActiveAndEnabled && NeedsOpening(p.Door) && MayOpen(p, c);

            var t = Begin(info, c, pos, now);
            if (info.Waypoint != null && info.Waypoint.onDoorOpen != null) info.Waypoint.onDoorOpen.triggerOutputs();
            Swing(d, pos);
            if (Log.VerboseOn)
                Log.Verbose($"NPC doors: opened {Name(d)}{Kind(t)} for {c.gameObject.name}.");

            if (!pair) return;
            var pt = Begin(p, c, pos, now);
            if (NeedsOpening(p.Door)) Swing(p.Door, pos);   // the waypoint relay may have opened it already
            if (Log.VerboseOn)
                Log.Verbose($"NPC doors: opened {Name(p.Door)}{Kind(pt)} with it.");
        }

        private static Track Begin(DoorInfo info, NPCController c, Vector3 pos, float now)
        {
            var d = info.Door;
            int id = d.GetInstanceID();
            if (!Tracks.TryGetValue(id, out var t))
            {
                t = new Track { Info = info };
                Tracks[id] = t;
            }
            t.OpenedAt = now;
            t.Watch = LockedNow(d);
            t.Strict = t.Watch || d.IsInteractDisabled();
            t.Closing = false;
            t.Handed = false;
            float side = Dot(pos - info.Center, info.Normal);
            if (t.Npcs.IndexOf(c) < 0)
            {
                t.Npcs.Add(c);
                t.Sides.Add(side >= 0f ? 1f : -1f);
                t.Starts.Add(Mathf.Max(Horizontal(pos, info.Center), Mathf.Abs(side)));
            }
            return t;
        }

        private static void Swing(Door d, Vector3 pos)
        {
            if (d.Sliding)
            {
                d.ForceOpen();
                return;
            }
            // Door.UseDoor(bypassLock: true) side rule, with ForceOpen in place of the toggle.
            Vector3 rhs = d.useForwardForSideDetection ? d.transform.forward : d.transform.right;
            bool far = Vector3.Dot(pos - d.transform.position, rhs) > 0f;
            bool opposite;
            if (far && !d.noOppositeDirection) opposite = !d.invertOppositeDirection;
            else if (d.ManualClose) opposite = d.invertOppositeDirection;
            else opposite = !d.invertOppositeDirection;
            if (opposite) d.ForceOpenOpposite();
            else d.ForceOpen();
        }

        private static void UpdateTracks(float now)
        {
            if (Tracks.Count == 0) return;
            Finished.Clear();
            foreach (var kv in Tracks)
            {
                var t = kv.Value;
                var d = t.Info.Door;
                if (d == null) { Finished.Add(kv.Key); continue; }

                if (t.Closing)
                {
                    if (!LockedNow(d) || !d.doorIsOpen || now - t.ClosingSince > StrictWatchSeconds) Finished.Add(kv.Key);
                    else if (!d.moving || Vanilla.DoorDirection(d)) d.Close();   // reopened: close it again
                    continue;
                }
                if (t.Handed)
                {
                    if (!d.doorIsOpen) Finished.Add(kv.Key);
                    else if (!PlayerInTrigger(d))
                    {
                        Finished.Add(kv.Key);
                        d.Close();
                        if (Log.VerboseOn) Log.Verbose($"NPC doors: you walked away; closed {Name(d)}.");
                    }
                    continue;
                }

                bool strict = t.Strict && Barrier(d);
                float past = strict ? LockedPastMeters : Cfg.CloseAfterMeters.Value;
                for (int i = t.Npcs.Count - 1; i >= 0; i--)
                {
                    var n = t.Npcs[i];
                    bool done = n == null || !n.isActiveAndEnabled;
                    if (!done)
                    {
                        Vector3 p = n.transform.position;
                        float s = Dot(p - t.Info.Center, t.Info.Normal) * t.Sides[i];   // > 0: still on the start side
                        float away = t.Starts[i] + ReleaseMeters;
                        done = -s >= past || s > away || Horizontal(p, t.Info.Center) > away + t.Info.HalfWidth + past;
                    }
                    if (done)
                    {
                        t.Npcs.RemoveAt(i);
                        t.Sides.RemoveAt(i);
                        t.Starts.RemoveAt(i);
                    }
                }

                float limit = strict ? Cfg.MaxOpenSeconds.Value : UnlockedMaxSeconds;
                if (t.Npcs.Count > 0 && now - t.OpenedAt < limit) continue;

                if (!d.doorIsOpen) { Finished.Add(kv.Key); continue; }
                t.Npcs.Clear();
                t.Sides.Clear();
                t.Starts.Clear();
                if (strict && PaidFor(d))
                {
                    Finished.Add(kv.Key);
                    if (Log.VerboseOn) Log.Verbose($"NPC doors: left {Name(d)} open; you paid the toll.");
                    continue;
                }
                if (strict)
                {
                    d.Close();
                    if (t.Watch && LockedNow(d))
                    {
                        t.Closing = true;
                        t.ClosingSince = now;
                    }
                    else Finished.Add(kv.Key);
                    if (Log.VerboseOn) Log.Verbose($"NPC doors: closed {(t.Watch ? "locked" : "scripted")} {Name(d)}.");
                    continue;
                }
                if (PlayerInTrigger(d))
                {
                    t.Handed = true;
                    if (Log.VerboseOn) Log.Verbose($"NPC doors: left {Name(d)} open; it closes when you walk away.");
                    continue;
                }
                Finished.Add(kv.Key);
                d.Close();
                if (Log.VerboseOn) Log.Verbose($"NPC doors: closed {Name(d)}.");
            }
            foreach (var id in Finished) Tracks.Remove(id);
        }

        // The player is well inside the door's close trigger (the capsule Door.Start adds, SRC/Door.cs:877-886), so the
        // game's OnTriggerExit will close the door once they leave. 3D, on the trigger's real (rotating) shape.
        // The toll gate halves sit beside 'Bridge Open Trigger Pay' (ASSETS/Scenes/Open Sewer Tenement.unity), which the coin
        // slot's relay activates once the toll is paid and 'Trigger Close' deactivates when everyone has left. While it is
        // armed the player has paid to walk through, so a gate opened for police is left to vanilla's Trigger Close.
        private static bool PaidFor(Door d)
        {
            var parent = d.transform.parent;
            if (parent == null) return false;
            var pay = parent.Find("Bridge Open Trigger Pay");
            return pay != null && pay.gameObject.activeSelf;
        }

        private static bool PlayerInTrigger(Door d)
        {
            if (d.NoCloseDistance) return false;
            var pl = PlayerLocator.instance;
            if (pl == null) return false;
            CapsuleCollider cap = null;
            foreach (var col in d.GetComponents<CapsuleCollider>())
                if (col != null && col.isTrigger && col.enabled) { cap = col; break; }
            if (cap == null) return false;

            var tr = cap.transform;
            Vector3 s = tr.lossyScale;
            float sx = Mathf.Abs(s.x), sy = Mathf.Abs(s.y), sz = Mathf.Abs(s.z);
            Vector3 axis;
            float along, radius;
            switch (cap.direction)
            {
                case 0: axis = tr.right; along = sx; radius = cap.radius * Mathf.Max(sy, sz); break;
                case 2: axis = tr.forward; along = sz; radius = cap.radius * Mathf.Max(sx, sy); break;
                default: axis = tr.up; along = sy; radius = cap.radius * Mathf.Max(sx, sz); break;
            }
            float half = Mathf.Max(0f, cap.height * along * 0.5f - radius);
            Vector3 c = tr.TransformPoint(cap.center);
            Vector3 p = pl.transform.position;
            float k = Mathf.Clamp(Vector3.Dot(p - c, axis), -half, half);
            return (p - (c + axis * k)).magnitude <= radius - TriggerMargin;
        }

        private static void RefreshDoors()
        {
            var waypoints = UnityEngine.Object.FindObjectsByType<Waypoint_Door>(FindObjectsSortMode.None);
            var elevators = UnityEngine.Object.FindObjectsByType<Elevator>(FindObjectsSortMode.None);
            var doors = UnityEngine.Object.FindObjectsByType<Door>(FindObjectsSortMode.None);

            Seen.Clear();
            foreach (var e in elevators)
            {
                if (e == null) continue;
                if (e.ElevatorDoor != null) Seen.Add(e.ElevatorDoor.GetInstanceID());
                if (e.Floors != null)
                    foreach (var f in e.Floors)
                        if (f != null && f.door != null) Seen.Add(f.door.GetInstanceID());
            }

            Active.Clear();
            foreach (var d in doors)
            {
                if (d == null || !d.isActiveAndEnabled) continue;
                int id = d.GetInstanceID();
                if (!Doors.TryGetValue(id, out var info))
                {
                    info = Build(d);
                    Doors[id] = info;
                }
                info.Excluded = info.Excluded || Seen.Contains(id);
                info.Waypoint = null;
                info.Partner = null;
                if (!info.Excluded) Active.Add(info);
            }
            foreach (var w in waypoints)
            {
                if (w == null || w.door == null) continue;
                if (Doors.TryGetValue(w.door.GetInstanceID(), out var info) && info.Waypoint == null) info.Waypoint = w;
            }
            Pair();
        }

        // Double doors: two active doors under the same parent, of the same kind, whose closed doorways are parallel,
        // coplanar and side by side. A door with more than one such neighbour gets no partner.
        private static void Pair()
        {
            for (int i = 0; i < Active.Count; i++)
            {
                var a = Active[i];
                if (a.Door == null) continue;
                var parent = a.Door.transform.parent;
                if (parent == null) continue;
                DoorInfo found = null;
                int count = 0;
                for (int j = 0; j < Active.Count; j++)
                {
                    if (i == j) continue;
                    var b = Active[j];
                    if (b.Door == null || b.Door.transform.parent != parent || b.Door.Sliding != a.Door.Sliding) continue;
                    if (Mathf.Abs(Dot(a.Normal, b.Normal)) < 0.95f) continue;
                    Vector3 off = b.Center - a.Center;
                    if (Mathf.Abs(Dot(off, a.Normal)) > 0.2f || Mathf.Abs(off.y) > 0.5f) continue;
                    float lat = Mathf.Abs(Dot(off, a.Lateral));
                    if (lat < 0.25f || lat > a.HalfWidth + b.HalfWidth + 0.2f) continue;
                    found = b;
                    count++;
                }
                if (count == 1) a.Partner = found;
            }
        }

        private static DoorInfo Build(Door d)
        {
            var info = new DoorInfo { Door = d, HalfWidth = DefaultHalfWidth };
            d.GetClosedDoorway(out Vector3 center, out Vector3 vanillaNormal);
            info.Center = center;
            vanillaNormal.y = 0f;
            bool vanillaFlat = vanillaNormal.sqrMagnitude >= 0.01f;
            if (vanillaFlat) vanillaNormal.Normalize();

            var box = d.GetComponent<BoxCollider>();
            if (box == null)
            {
                if (!vanillaFlat)
                {
                    info.Excluded = true;   // a hatch or trapdoor: no vertical doorway to cross
                    return info;
                }
                SetNormal(info, vanillaNormal);
                info.VanillaAxis = true;
                return info;
            }

            // The box's three axes in world space at the closed pose (GetClosedDoorway's matrix, without translation).
            var tr = d.transform;
            Quaternion q = Vanilla.DoorStartDone(d) ? Quaternion.Euler(Vanilla.DoorSpawnAngles(d)) : tr.localRotation;
            Matrix4x4 m = (tr.parent != null ? tr.parent.localToWorldMatrix : Matrix4x4.identity) *
                          Matrix4x4.TRS(Vector3.zero, q, tr.localScale);
            Vector3 size = box.size;
            var axes = new[]
            {
                m.MultiplyVector(new Vector3(size.x, 0f, 0f)),
                m.MultiplyVector(new Vector3(0f, size.y, 0f)),
                m.MultiplyVector(new Vector3(0f, 0f, size.z)),
            };
            int thin = 0;
            for (int i = 1; i < 3; i++)
                if (axes[i].sqrMagnitude < axes[thin].sqrMagnitude) thin = i;
            Vector3 n = axes[thin];
            Vector3 flat = new Vector3(n.x, 0f, n.z);
            if (flat.sqrMagnitude < 1e-8f || Mathf.Abs(n.y) > 0.7f * n.magnitude)
            {
                info.Excluded = true;   // thin along the vertical: a hatch or trapdoor
                return info;
            }
            SetNormal(info, flat.normalized);

            // Width: of the two other axes, the more horizontal one.
            int o1 = (thin + 1) % 3, o2 = (thin + 2) % 3;
            int wide = Steepness(axes[o1]) <= Steepness(axes[o2]) ? o1 : o2;
            Vector3 w = axes[wide];
            float width = Mathf.Sqrt(w.x * w.x + w.z * w.z);
            if (width > 0.05f) info.HalfWidth = width * 0.5f;

            info.VanillaAxis = vanillaFlat && Mathf.Abs(Dot(vanillaNormal, info.Normal)) > 0.9f;
            return info;
        }

        private static void SetNormal(DoorInfo info, Vector3 normal)
        {
            info.Normal = normal;
            info.Lateral = Vector3.Cross(Vector3.up, normal);
        }

        private static float Steepness(Vector3 v)
        {
            float m = v.magnitude;
            return m < 1e-6f ? 1f : Mathf.Abs(v.y) / m;
        }

        // Strict doors this fix opened are shut before the game writes them, so no save holds one open.
        private static void SnapStrict(string when)
        {
            foreach (var t in Tracks.Values)
            {
                var d = t.Info.Door;
                if (d == null || !t.Strict || !Barrier(d) || (!d.doorIsOpen && !d.moving) || PaidFor(d)) continue;
                bool relock = d.lockWhenClosed && !(d._Lock != null && d._Lock.Locked);
                d.CloseInstantly();
                if (relock) d.Lock();   // what OnFullyClosed would have done (SRC/Door.cs:783)
                t.Closing = false;
                t.Npcs.Clear();
                t.Sides.Clear();
                t.Starts.Clear();
                if (Log.VerboseOn) Log.Verbose($"NPC doors: shut {Name(d)} {when}.");
            }
        }

        private static void Stop()
        {
            // Switched off mid-session: shut the strict doors this fix opened, leave the rest to vanilla.
            foreach (var t in Tracks.Values)
            {
                var d = t.Info.Door;
                if (d != null && t.Strict && Barrier(d) && d.doorIsOpen) d.Close();
            }
            Reset();
            _wasOn = false;
        }

        private static void Reset()
        {
            Tracks.Clear();
            Doors.Clear();
            Active.Clear();
            _npcs = new NPCController[0];
            _nextNpcs = 0f;
            _nextDoors = 0f;
        }

        // SaveController.SavingStarted: before any door is serialized (SRC/SaveController.cs:519-521).
        internal static void OnSaving()
        {
            if (!Ready || Tracks.Count == 0) return;
            SnapStrict("before the save");
        }

        // PlayerWillChangeLevel: the level is saved next (SRC/SaveController.cs:335).
        internal static void OnLeaving()
        {
            if (Ready && Tracks.Count > 0) SnapStrict("before the level change");
            OnLevelChange();
        }

        // Load: the scene's doors and NPCs are new.
        internal static void OnLevelChange()
        {
            Reset();
            _scene = int.MinValue;
        }

        private static string Kind(Track t) => t.Watch ? " (locked)" : t.Strict ? " (scripted)" : "";

        private static float Dot(Vector3 a, Vector3 n) => a.x * n.x + a.z * n.z;

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        private static string Name(Door d)
        {
            var p = d.transform.parent;
            return p != null ? p.name + "/" + d.gameObject.name : d.gameObject.name;
        }
    }
}
