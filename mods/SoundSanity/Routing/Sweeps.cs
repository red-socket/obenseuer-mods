using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SoundSanity
{
    // When routing looks at sources that no hook sees being created (spec §3.3):
    //  - SweepSceneImmediate: the roots of a scene that just loaded (after Awake, before Start: serialized sources only);
    //  - SweepRoot: one subtree (furniture, tenant upgrades, shop previews, residents), inactive objects included;
    //  - SweepSettle: every loaded gameplay scene once the level is ready, duplicate and Main cases only.
    // One try/catch per source: a bad source is skipped and logged once.
    internal static class Sweeps
    {
        private static readonly List<AudioSource> Buf = new List<AudioSource>();

        internal static void SweepSceneImmediate(Scene scene)
        {
            if (!Router.Active || !scene.IsValid()) return;
            int before = Router.Moved, seen = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                root.GetComponentsInChildren(true, Buf);
                seen += Buf.Count;
                foreach (var s in Buf) One(s, Router.Ctx.SweepImmediate, "scene load");
            }
            Buf.Clear();
            if (Log.VerboseOn) Log.Verbose($"Routing: scene '{scene.name}' swept, {seen} sources, {Router.Moved - before} moved.");
        }

        internal static void SweepRoot(GameObject root, bool duplicateOnly, string where)
        {
            if (!Router.Active || root == null) return;
            root.GetComponentsInChildren(true, Buf);
            var ctx = duplicateOnly ? Router.Ctx.SweepSettle : Router.Ctx.SweepRoot;
            foreach (var s in Buf) One(s, ctx, where);
            Buf.Clear();
        }

        internal static void SweepSettle()
        {
            if (!Router.Active) return;
            int before = Router.Moved, seen = 0;
            var all = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Scene last = default;
            bool lastOk = false;
            foreach (var s in all)
            {
                if (s == null) continue;
                var sc = s.gameObject.scene;
                if (sc != last)
                {
                    last = sc;
                    lastOk = sc.IsValid() && sc.isLoaded && sc.name != "DontDestroyOnLoad" && sc.name != "Main Menu";
                }
                if (!lastOk) continue;
                seen++;
                One(s, Router.Ctx.SweepSettle, "settle");
            }
            Log.Verbose($"Routing: settle sweep, {seen} sources, {Router.Moved - before} moved.");
        }

        private static void One(AudioSource s, Router.Ctx ctx, string where)
        {
            try { Router.Handle(s, ctx, where); }
            catch (Exception e) { Log.Exception("routing sweep (" + where + ")", e); }
        }
    }
}
