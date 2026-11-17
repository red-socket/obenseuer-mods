using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DisplayOptions
{
    // Micro-prop shadows off (spec §5.2, D7). When a level loads, every MeshRenderer in the scene is examined in slices of
    // at most 3 ms per frame (Mines has about 113k renderers); small ones that cast shadows stop casting them. A second,
    // incremental pass on LoadingDone catches objects that savables restored after sceneLoaded. Every change is recorded
    // and written back on a config change or teardown; the records die with the scene. Objects spawned after load stay
    // vanilla.
    internal static class MicroProps
    {
        internal static bool Broken, Available;
        private const double SliceMs = 3.0;
        private static readonly int[] ExcludedLayers = { 10, 11, 12, 15, 22, 24 };   // Player, ItemPreview, Placeable, NPC, DontHideFirstPerson, FirstPerson

        private static readonly List<KeyValuePair<MeshRenderer, ShadowCastingMode>> Records = new List<KeyValuePair<MeshRenderer, ShadowCastingMode>>();
        private static readonly HashSet<int> Seen = new HashSet<int>();
        private static MeshRenderer[] _items;
        private static int _idx, _sceneHandle, _changed, _batched, _frames;
        private static float _max;
        private static bool _loose;
        private static Transform _playerRoot;
        private static readonly Stopwatch Total = new Stopwatch();
        private static string _sceneName;

        internal static void OnSceneLoaded()
        {
            // The old scene's objects are gone: its records and examined IDs are meaningless now.
            Records.Clear();
            Seen.Clear();
            _items = null;
            Start();
        }

        internal static void OnLoadingDone() => Start();

        internal static void OnConfigChanged()
        {
            Restore();
            Start();
        }

        // A level is a scene with the game's HUD (not the main menu or the Open Sewer intro). GameUIInstance.instance is set
        // only in its Start (SRC/GameUIInstance.cs:10-14), after sceneLoaded, so at sceneLoaded it is null or the previous
        // scene's: look for the scene's own Game UI instead.
        private static bool IsLevel(Scene s)
        {
            var g = GameUIInstance.instance;
            if (g != null && g.gameObject.scene.handle == s.handle) return true;
            foreach (var u in Object.FindObjectsByType<GameUIInstance>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (u != null && u.gameObject.scene.handle == s.handle) return true;
            return false;
        }

        private static void Start()
        {
            if (!Available || Broken || Cfg.Hot.MicroMax <= 0f) return;
            try
            {
                var scene = SceneManager.GetActiveScene();
                if (!IsLevel(scene)) return;
                _sceneHandle = scene.handle;
                _sceneName = scene.name;
                _max = Cfg.Hot.MicroMax;
                _loose = Cfg.Hot.IncludeLoose;
                _playerRoot = PlayerLocator.instance != null ? PlayerLocator.instance.transform.root : null;
                _items = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                _idx = 0;
                _changed = _batched = _frames = 0;
                Total.Reset();
            }
            catch (Exception e)
            {
                Broken = true;
                _items = null;
                Log.Exception("Shadows: micro-props start", e);
            }
        }

        // Plugin.Update: one slice.
        internal static void Tick()
        {
            if (_items == null) return;
            if (Broken || SceneManager.GetActiveScene().handle != _sceneHandle)
            {
                _items = null;   // the scene changed under the pass: abandon it
                return;
            }
            Total.Start();
            var sw = Stopwatch.StartNew();
            try
            {
                while (_idx < _items.Length)
                {
                    Examine(_items[_idx++]);
                    if ((_idx & 63) == 0 && sw.Elapsed.TotalMilliseconds >= SliceMs) break;
                }
            }
            catch (Exception e)
            {
                Broken = true;
                _items = null;
                Log.Exception("Shadows: micro-props", e);
            }
            Total.Stop();
            _frames++;
            if (_items != null && _idx >= _items.Length)
            {
                Log.Verbose($"Shadows: {_changed} small props without shadows in {_sceneName} ({Total.Elapsed.TotalMilliseconds:0} ms over " +
                            $"{_frames} frames; {_batched} static-batched skipped).");
                _items = null;
            }
        }

        private static void Examine(MeshRenderer r)
        {
            if (r == null) return;
            var go = r.gameObject;
            if (go.scene.handle != _sceneHandle) return;   // DDOL objects and assets
            var mode = r.shadowCastingMode;
            if (mode != ShadowCastingMode.On && mode != ShadowCastingMode.TwoSided) return;
            if (!Seen.Add(r.GetInstanceID())) return;
            if (r.isPartOfStaticBatch)
            {
                _batched++;
                return;
            }
            int layer = go.layer;
            for (int i = 0; i < ExcludedLayers.Length; i++)
                if (layer == ExcludedLayers[i]) return;
            var mf = go.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) return;
            // Mesh bounds times scale: Renderer.bounds is wrong for inactive renderers.
            var size = Vector3.Scale(mesh.bounds.size, r.transform.lossyScale);
            if (Mathf.Max(Mathf.Abs(size.x), Mathf.Max(Mathf.Abs(size.y), Mathf.Abs(size.z))) >= _max) return;
            var t = r.transform;
            if (_playerRoot != null && t.root == _playerRoot) return;
            if (Under(r, Vanilla.Door) || Under(r, Vanilla.DoorChangelevel) || Under(r, Vanilla.CharacterModel) ||
                Under(r, Vanilla.NPCInfo) || Under(r, Vanilla.FurniturePlaceable) || Under(r, Vanilla.FurnitureCollectable) ||
                Under(r, typeof(Animator)) || (!_loose && Under(r, Vanilla.CollectibleItem))) return;
            Records.Add(new KeyValuePair<MeshRenderer, ShadowCastingMode>(r, mode));
            r.shadowCastingMode = ShadowCastingMode.Off;
            _changed++;
        }

        private static bool Under(Component c, Type t) => c.GetComponentInParent(t, true) != null;

        // Config change and teardown: every renderer we changed gets its own mode back.
        internal static void Restore()
        {
            _items = null;
            int n = 0;
            foreach (var kv in Records)
            {
                if (kv.Key == null) continue;
                try
                {
                    kv.Key.shadowCastingMode = kv.Value;
                    n++;
                }
                catch (Exception e) { Log.Exception("Shadows: micro-props restore", e); }
            }
            Records.Clear();
            Seen.Clear();
            if (n > 0) Log.Verbose($"Shadows: {n} small props have their shadows back.");
        }
    }
}
