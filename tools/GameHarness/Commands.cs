using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GameHarness
{
    // Command handlers. Each one runs on the main thread and answers with Json.Ok/Json.Error, either at once or from a
    // coroutine (screenshot, load, quit). Angles: yaw in degrees (0 = +Z, 90 = +X), pitch in degrees with positive = up.
    internal static class Commands
    {
        private delegate void Handler(Server.Request r, string[] a);

        private static readonly Dictionary<string, Handler> Table = new Dictionary<string, Handler>
        {
            { "ping", Ping },
            { "state", State },
            { "mods", Mods },
            { "console", Console },
            { "key", Key },
            { "action", ActionKey },
            { "move", Move },
            { "release", (r, a) => { VirtualInput.ReleaseAll(); r.Reply(Json.Ok()); } },
            { "look", Look },
            { "turn", Turn },
            { "lookat", LookAt },
            { "tp", Tp },
            { "find", Find },
            { "doors", Doors },
            { "door", Door },
            { "goto_door", GotoDoor },
            { "buttons", Buttons },
            { "click", Click },
            { "screenshot", Screenshot },
            { "load", Load },
            { "save", Save },
            { "quit", Quit },
        };

        internal static void Dispatch(Server.Request r)
        {
            Plugin.SessionActive = true;
            if (!Table.TryGetValue(r.Command, out var h))
            {
                r.Reply(Json.Error($"unknown command '{r.Command}'. Known: {string.Join(", ", Table.Keys.ToArray())}"));
                return;
            }
            h(r, r.Args);
        }

        // ---- state --------------------------------------------------------------------------------------------------

        private static void Ping(Server.Request r, string[] a) => r.Reply(Json.Ok(new Dictionary<string, object>
        {
            { "harness", Plugin.Version }, { "game", Application.version }, { "scene", SceneManager.GetActiveScene().name },
            { "frame", Time.frameCount },
        }));

        private static void State(Server.Request r, string[] a)
        {
            var d = new Dictionary<string, object>();
            string scene = SceneManager.GetActiveScene().name;
            d["scene"] = scene;
            d["loading"] = SaveController.Loading;
            bool fading = false;
            TryDo(d, "fading", () => fading = LoadingScreen.instance != null && (LoadingScreen.instance.Fading || LoadingScreen.instance.FadeActive));
            d["fading"] = fading;
            float sinceLoad = Time.realtimeSinceStartup - Plugin.SceneLoadedAt;
            d["secondsSinceSceneLoad"] = sinceLoad;
            // The title screen waits for any key before the menu buttons appear (PressAnyKey.pressed).
            bool waitingForKey = false;
            TryDo(d, "waitingForKey", () =>
            {
                var pak = Object.FindFirstObjectByType<PressAnyKey>();
                waitingForKey = pak != null && !Traverse.Create(pak).Field("pressed").GetValue<bool>();
            });
            d["waitingForKey"] = waitingForKey;
            d["mainMenuReady"] = scene == "Main Menu" && MainMenu.Instance != null && !SaveController.Loading && !fading && !waitingForKey && sinceLoad > 2f;
            bool inGame = PlayerLocator.instance != null && GameController.instance != null && !SaveController.Loading;
            d["inGame"] = inGame && !fading;
            Try(d, "save", () => new Dictionary<string, object>
            {
                { "folder", SaveController.LastSaveFolderPath },
                { "character", Traverse.Create(typeof(SaveController)).Field("CharacterName").GetValue<string>() },
                { "slot", Traverse.Create(typeof(SaveController)).Field("SaveName").GetValue<string>() },
                { "identity", PlayerIdentity.identity != null ? Plugin.CurrentCharacter() : null },
            });
            if (PlayerLocator.instance != null)
            {
                Try(d, "position", () => PlayerLocator.instance.transform.position);
                Try(d, "look", () => { GetLook(out float yaw, out float pitch); return new Dictionary<string, object> { { "yaw", yaw }, { "pitch", pitch } }; });
                Try(d, "aim", Aim);
                Try(d, "target", Target);
            }
            if (TimeOfDayAzure.instance != null)
                Try(d, "time", () =>
                {
                    var t = TimeOfDayAzure.instance.currentTimeAndDay;
                    return new Dictionary<string, object>
                    {
                        { "clock", $"{(int)t.hours:00}:{(int)t.minutes:00}" }, { "day", t.day }, { "month", t.month },
                        { "dayCounter", TimeOfDayAzure.instance.dayCounter },
                    };
                });
            Try(d, "ui", Ui);
            d["timeScale"] = Time.timeScale;
            d["fps"] = Time.smoothDeltaTime > 0 ? 1f / Time.smoothDeltaTime : 0f;
            d["focused"] = Application.isFocused;
            d["heldKeys"] = VirtualInput.HeldKeys();
            d["blockedSaves"] = Plugin.BlockedSaves;
            r.Reply(Json.Ok(d));
        }

        private static object Ui()
        {
            var u = new Dictionary<string, object>();
            u["cursorVisible"] = Cursor.visible;
            u["cursorLock"] = Cursor.lockState.ToString();
            u["paused"] = PauseMenu.paused;
            if (DeveloperConsole.Instance != null) u["console"] = DeveloperConsole.Instance.consoleOpened;
            if (GameUIController.instance != null)
            {
                u["menuOpen"] = GameUIController.instance.IsAnyMenuOpen();
                u["inventory"] = GameUIController.instance.InventoryIsVisible();
                u["storage"] = GameUIController.instance.StorageIsVisible();
                u["hudVisible"] = GameUIController.instance.GameUIVisible();
            }
            if (GameController.instance != null)
            {
                var locks = Traverse.Create(GameController.instance).Field("disabledKeysList").GetValue<List<GameObject>>();
                if (locks != null) u["controlLocks"] = locks.Where(g => g != null).Select(g => g.name).ToList();
                u["gamePaused"] = GameController.instance.GameIsPaused;
            }
            return u;
        }

        private static object Aim()
        {
            var cam = Camera.main;
            if (cam == null) return null;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 100f, ~0, QueryTriggerInteraction.Ignore)) return null;
            return new Dictionary<string, object> { { "object", Path(hit.collider.transform) }, { "point", hit.point }, { "distance", hit.distance } };
        }

        private static object Target()
        {
            var io = InteractObjects.instance;
            if (io == null || !(io.interactable is Object o) || o == null) return null;
            var t = new Dictionary<string, object> { { "type", o.GetType().Name }, { "object", o is Component c ? Path(c.transform) : o.name } };
            if (o is Interactable it) { t["title"] = it.title; t["useMessage"] = it.useMessage; }
            return t;
        }

        private static void Mods(Server.Request r, string[] a)
        {
            var list = Chainloader.PluginInfos.Values
                .Select(p => (object)new Dictionary<string, object> { { "guid", p.Metadata.GUID }, { "name", p.Metadata.Name }, { "version", p.Metadata.Version.ToString() }, { "loaded", p.Instance != null } })
                .ToList();
            r.Reply(Json.Ok(new Dictionary<string, object> { { "plugins", list } }));
        }

        // ---- console --------------------------------------------------------------------------------------------------

        private static readonly MethodInfo HandleInput = AccessTools.Method(typeof(DeveloperConsole), "HandleInput", new[] { typeof(string) });
        internal static List<string> Captured;

        private static void Console(Server.Request r, string[] a)
        {
            string text = string.Join(" ", a).Trim();
            if (text.Length == 0) { r.Reply(Json.Error("usage: console <command line>")); return; }
            if (DeveloperConsole.Instance == null) { r.Reply(Json.Error("no DeveloperConsole yet")); return; }
            var lines = new List<string>();
            var logs = new List<string>();
            Application.LogCallback cb = (msg, trace, type) => logs.Add(type + ": " + msg);
            Captured = lines;
            Application.logMessageReceived += cb;
            string error = null;
            try { HandleInput.Invoke(DeveloperConsole.Instance, new object[] { text }); }
            catch (TargetInvocationException e) { error = e.InnerException?.GetType().Name + ": " + e.InnerException?.Message; }
            finally { Captured = null; Application.logMessageReceived -= cb; }
            var d = new Dictionary<string, object> { { "printed", lines }, { "log", logs } };
            if (error != null) d["exception"] = error;
            r.Reply(Json.Ok(d));
        }

        // ---- input ----------------------------------------------------------------------------------------------------

        private static void Key(Server.Request r, string[] a)
        {
            if (a.Length < 1) { r.Reply(Json.Error("usage: key <KeyCode> [seconds]")); return; }
            KeyCode k;
            try { k = (KeyCode)Enum.Parse(typeof(KeyCode), a[0], true); }
            catch { r.Reply(Json.Error($"unknown KeyCode '{a[0]}' (Unity names: E, Tab, Escape, F1, Alpha1, Mouse0, ...)")); return; }
            float secs = a.Length > 1 ? F(a[1]) : 0f;
            VirtualInput.Press(k, secs);
            r.Reply(Json.Ok(new Dictionary<string, object> { { "key", k.ToString() }, { "seconds", secs } }));
        }

        private static void ActionKey(Server.Request r, string[] a)
        {
            if (a.Length < 1) { r.Reply(Json.Error("usage: action <game action, e.g. Interact|Inventory|Menu|Map|Pause Menu|Hide Hud> [seconds]")); return; }
            if (InputManager.instance == null) { r.Reply(Json.Error("no InputManager yet")); return; }
            float secs = 0f;
            string name = string.Join(" ", a);
            var m = Regex.Match(name, @"^(.*?)\s+([0-9.]+)$");
            if (m.Success) { name = m.Groups[1].Value; secs = F(m.Groups[2].Value); }
            KeyCode k = InputManager.instance.GetKeyCode(name, ignoreController: true);
            if (k == KeyCode.None) { r.Reply(Json.Error($"action '{name}' has no keyboard binding")); return; }
            VirtualInput.Press(k, secs);
            r.Reply(Json.Ok(new Dictionary<string, object> { { "action", name }, { "key", k.ToString() }, { "seconds", secs } }));
        }

        private static void Move(Server.Request r, string[] a)
        {
            if (a.Length < 2) { r.Reply(Json.Error("usage: move <forward|backward|left|right|jump|crouch|walk> <seconds>")); return; }
            string action = char.ToUpperInvariant(a[0][0]) + a[0].Substring(1).ToLowerInvariant();
            if (action == "Back") action = "Backward";
            ActionKey(r, new[] { action, a[1] });
        }

        // ---- camera and position ----------------------------------------------------------------------------------------

        // The camera controller's pivot faces away from the view (PlayerLocator.Teleport also adds 180 to rotY), so
        // the view is read from the rendering camera and the controller gets yaw + 180 and the pitch with its sign flipped.
        private static void GetLook(out float yaw, out float pitch)
        {
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam == null) { yaw = pitch = 0f; return; }
            Vector3 e = cam.eulerAngles;
            yaw = Mathf.Repeat(e.y, 360f);
            pitch = -Mathf.DeltaAngle(0f, e.x);
        }

        private static string SetLook(float yaw, float pitch)
        {
            var cam = ThirdPersonCameraController.instance;
            if (cam == null) return "no camera controller in this scene";
            float y = Mathf.Repeat(yaw + 180f, 360f);
            cam.rotY = cam.InvertX ? -y : y;
            cam.rotX = cam.InvertY ? -pitch : pitch;
            return null;
        }

        private static void ReplyLook(Server.Request r, string err)
        {
            if (err != null) { r.Reply(Json.Error(err)); return; }
            r.Reply(Json.Ok());
        }

        private static void Look(Server.Request r, string[] a)
        {
            if (a.Length < 1) { r.Reply(Json.Error("usage: look <yaw> [pitch]")); return; }
            ReplyLook(r, SetLook(F(a[0]), a.Length > 1 ? F(a[1]) : 0f));
        }

        private static void Turn(Server.Request r, string[] a)
        {
            if (a.Length < 1) { r.Reply(Json.Error("usage: turn <dyaw> [dpitch]")); return; }
            GetLook(out float yaw, out float pitch);
            ReplyLook(r, SetLook(yaw + F(a[0]), pitch + (a.Length > 1 ? F(a[1]) : 0f)));
        }

        private static void LookAt(Server.Request r, string[] a)
        {
            if (a.Length < 3) { r.Reply(Json.Error("usage: lookat <x> <y> <z>")); return; }
            ReplyLook(r, LookAtPoint(new Vector3(F(a[0]), F(a[1]), F(a[2]))));
        }

        private static string LookAtPoint(Vector3 p)
        {
            var cam = Camera.main;
            Vector3 from = cam != null ? cam.transform.position : PlayerLocator.instance != null ? PlayerLocator.instance.playerHeadPosition : Vector3.zero;
            Vector3 dir = p - from;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
            return SetLook(yaw, pitch);
        }

        private static string Teleport(Vector3 pos, float? yaw, float? pitch)
        {
            var pl = PlayerLocator.instance;
            if (pl == null) return "no player in this scene";
            GetLook(out float curYaw, out float curPitch);
            var tmp = new GameObject("GameHarnessTeleport");
            try
            {
                tmp.transform.position = pos;
                tmp.transform.eulerAngles = new Vector3(0f, yaw ?? curYaw, 0f);
                tmp.transform.localScale = pl.transform.localScale;
                pl.Teleport(tmp.transform);
            }
            finally { Object.Destroy(tmp); }
            return SetLook(yaw ?? curYaw, pitch ?? curPitch);
        }

        private static void Tp(Server.Request r, string[] a)
        {
            if (a.Length < 3) { r.Reply(Json.Error("usage: tp <x> <y> <z> [yaw] [pitch]")); return; }
            ReplyLook(r, Teleport(new Vector3(F(a[0]), F(a[1]), F(a[2])), a.Length > 3 ? F(a[3]) : (float?)null, a.Length > 4 ? F(a[4]) : (float?)null));
        }

        // ---- world queries ----------------------------------------------------------------------------------------------

        private static void Find(Server.Request r, string[] a)
        {
            if (a.Length < 1) { r.Reply(Json.Error("usage: find <name substring> [limit]")); return; }
            string q = a[0];
            int limit = a.Length > 1 ? (int)F(a[1]) : 20;
            Vector3 me = PlayerLocator.instance != null ? PlayerLocator.instance.transform.position : Vector3.zero;
            var hits = new List<Transform>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                foreach (var root in s.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(false))
                        if (t.name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add(t);
            }
            var list = hits.OrderBy(t => (t.position - me).sqrMagnitude).Take(limit)
                .Select(t => (object)new Dictionary<string, object> { { "path", Path(t) }, { "position", t.position }, { "distance", (t.position - me).magnitude } })
                .ToList();
            r.Reply(Json.Ok(new Dictionary<string, object> { { "count", hits.Count }, { "objects", list } }));
        }

        private static List<Changelevel> SortedDoors()
        {
            Vector3 me = PlayerLocator.instance != null ? PlayerLocator.instance.transform.position : Vector3.zero;
            return Object.FindObjectsByType<Changelevel>(FindObjectsSortMode.None).OrderBy(c => (c.transform.position - me).sqrMagnitude).ToList();
        }

        private static void Doors(Server.Request r, string[] a)
        {
            Vector3 me = PlayerLocator.instance != null ? PlayerLocator.instance.transform.position : Vector3.zero;
            var list = SortedDoors().Select((c, i) =>
            {
                var d = new Dictionary<string, object>
                {
                    { "index", i }, { "object", Path(c.transform) }, { "type", c.GetType().Name }, { "to", c.OtherLevel },
                    { "entrypoint", c.OtherEntrypoint }, { "position", c.transform.position }, { "distance", (c.transform.position - me).magnitude },
                };
                if (c is DoorChangelevel dc && dc._Lock != null) d["locked"] = dc._Lock.Locked;
                return (object)d;
            }).ToList();
            r.Reply(Json.Ok(new Dictionary<string, object> { { "doors", list } }));
        }

        private static Changelevel DoorAt(Server.Request r, string[] a)
        {
            var doors = SortedDoors();
            int i = a.Length > 0 ? (int)F(a[0]) : 0;
            if (i < 0 || i >= doors.Count) { r.Reply(Json.Error($"no door {i} ({doors.Count} level-change doors here; see 'doors')")); return null; }
            return doors[i];
        }

        // Uses the door exactly as the interact key would: DoorChangelevel checks its lock, then saves and changes level.
        private static void Door(Server.Request r, string[] a)
        {
            var c = DoorAt(r, a);
            if (c == null) return;
            if (SaveController.Loading) { r.Reply(Json.Error("busy: a save or load is running")); return; }
            c.Interact();
            r.Reply(Json.Ok(new Dictionary<string, object> { { "object", Path(c.transform) }, { "to", c.OtherLevel } }));
        }

        // Puts the player on the door's own arrival point (where you spawn when entering through it) and faces the door.
        private static void GotoDoor(Server.Request r, string[] a)
        {
            var c = DoorAt(r, a);
            if (c == null) return;
            var spot = c.ThisEntrypoint?.Location;
            if (spot == null) { r.Reply(Json.Error("door has no entrypoint; use tp")); return; }
            string err = Teleport(spot.position, spot.eulerAngles.y, 0f);
            if (err == null) Plugin.Instance.StartCoroutine(FaceNextFrame(c.transform));
            ReplyLook(r, err);
        }

        private static IEnumerator FaceNextFrame(Transform t)
        {
            yield return null;
            var col = t.GetComponentInChildren<Collider>();
            LookAtPoint(col != null ? col.bounds.center : t.position);
        }

        // ---- UI -------------------------------------------------------------------------------------------------------

        private static List<GameObject> Clickables()
        {
            var seen = new HashSet<GameObject>();
            var list = new List<GameObject>();
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (!(mb is IPointerClickHandler || mb is IPointerDownHandler) || !mb.isActiveAndEnabled) continue;
                if (mb is Selectable s && !s.IsInteractable()) continue;
                var canvas = mb.GetComponentInParent<Canvas>();
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                if (seen.Add(mb.gameObject)) list.Add(mb.gameObject);
            }
            return list;
        }

        private static string Label(GameObject go)
        {
            var tmp = go.GetComponentInChildren<TMP_Text>();
            if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text)) return tmp.text.Trim();
            var txt = go.GetComponentInChildren<Text>();
            if (txt != null && !string.IsNullOrWhiteSpace(txt.text)) return txt.text.Trim();
            return "";
        }

        private static void Buttons(Server.Request r, string[] a)
        {
            string q = string.Join(" ", a);
            var list = Clickables()
                .Select(g => new { g, label = Label(g), path = Path(g.transform) })
                .Where(x => q.Length == 0 || x.label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || x.path.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(x => (object)new Dictionary<string, object> { { "label", x.label }, { "object", x.path } })
                .ToList();
            r.Reply(Json.Ok(new Dictionary<string, object> { { "buttons", list } }));
        }

        // Matches a visible label exactly (case-insensitive), then by substring, then the object path; sends a pointer
        // down, up and click to it, which is what a mouse click on a uGUI element does.
        private static void Click(Server.Request r, string[] a)
        {
            string q = string.Join(" ", a).Trim();
            if (q.Length == 0) { r.Reply(Json.Error("usage: click <label or object path>")); return; }
            var all = Clickables().Select(g => new { g, label = Label(g), path = Path(g.transform) }).ToList();
            var hit = all.FirstOrDefault(x => string.Equals(x.label, q, StringComparison.OrdinalIgnoreCase))
                      ?? all.FirstOrDefault(x => x.label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                      ?? all.FirstOrDefault(x => x.path.EndsWith(q, StringComparison.OrdinalIgnoreCase))
                      ?? all.FirstOrDefault(x => x.path.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            if (hit == null) { r.Reply(Json.Error($"no clickable matches '{q}' (see 'buttons')")); return; }
            var ped = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(hit.g, ped, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(hit.g, ped, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(hit.g, ped, ExecuteEvents.pointerClickHandler);
            r.Reply(Json.Ok(new Dictionary<string, object> { { "label", hit.label }, { "object", hit.path } }));
        }

        // ---- screenshots ------------------------------------------------------------------------------------------------

        // Modes: "full" (as the player sees it), "nohud" (the game's own HUD hidden, mod windows and menus stay) and
        // "clean" (every screen-space canvas hidden: no HUD, crosshair, menus or mod windows; IMGUI is not affected).
        private static void Screenshot(Server.Request r, string[] a)
        {
            if (a.Length < 1) { r.Reply(Json.Error("usage: screenshot <absolute path.png> [full|nohud|clean]")); return; }
            string mode = a.Length > 1 ? a[1].ToLowerInvariant() : "full";
            if (mode != "full" && mode != "nohud" && mode != "clean") { r.Reply(Json.Error("mode must be full, nohud or clean")); return; }
            Plugin.Instance.StartCoroutine(Shot(r, a[0], mode));
        }

        private static IEnumerator Shot(Server.Request r, string path, string mode)
        {
            var hidden = new List<Canvas>();
            bool hudHidden = false;
            if (mode == "clean")
            {
                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (c.enabled && c.isRootCanvas && c.renderMode != RenderMode.WorldSpace) { c.enabled = false; hidden.Add(c); }
            }
            else if (mode == "nohud" && GameUIController.instance != null && GameUIController.instance.GameUIVisible())
            {
                GameUIController.instance.HideGameUI(Plugin.Instance.gameObject);
                hudHidden = true;
            }
            if (mode != "full") yield return null;
            yield return new WaitForEndOfFrame();
            byte[] png = null;
            int w = 0, h = 0;
            string error = null;
            try
            {
                // The backbuffer's alpha is near 0 after post-processing, so copy into an RGB texture: an RGBA PNG would
                // show up washed out on any viewer that blends it over white.
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                w = tex.width;
                h = tex.height;
                var rgb = new Texture2D(w, h, TextureFormat.RGB24, false);
                rgb.SetPixels32(tex.GetPixels32());
                png = rgb.EncodeToPNG();
                Object.Destroy(tex);
                Object.Destroy(rgb);
            }
            catch (Exception e) { error = e.Message; }
            finally
            {
                foreach (var c in hidden) if (c != null) c.enabled = true;
                if (hudHidden && GameUIController.instance != null) GameUIController.instance.ShowGameUI(Plugin.Instance.gameObject);
            }
            if (error == null)
            {
                try
                {
                    string win = WinPath(path);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(win));
                    File.WriteAllBytes(win, png);
                }
                catch (Exception e) { error = e.Message; }
            }
            r.Reply(error != null ? Json.Error(error) : Json.Ok(new Dictionary<string, object> { { "path", path }, { "width", w }, { "height", h }, { "mode", mode } }));
        }

        // ---- saves --------------------------------------------------------------------------------------------------

        private static string SavesRoot => Application.persistentDataPath + "/Saves/";

        private static void Load(Server.Request r, string[] a)
        {
            string slot = a.Length > 0 ? a[0] : "Slot1";
            string folder = a.Length > 1 ? a[1] : Plugin.TestCharacter.Value;
            if (!Plugin.IsTestCharacter(folder)) { r.Reply(Json.Error($"the harness only loads the test character '{Plugin.TestCharacter.Value}'")); return; }
            if (!Regex.IsMatch(slot, @"^(Slot([1-9]|10)|Autosave2?)$")) { r.Reply(Json.Error("slot must be Slot1..Slot10, Autosave or Autosave2")); return; }
            string path = SavesRoot + folder;
            if (!File.Exists(path + "/" + slot + "/Globals.tnmt")) { r.Reply(Json.Error($"no save at Saves/{folder}/{slot} (run 'harness.py test-save')")); return; }
            if (SaveController.Loading) { r.Reply(Json.Error("busy: a save or load is running")); return; }
            Plugin.Instance.StartCoroutine(LoadRoutine(path, slot));
            r.Reply(Json.Ok(new Dictionary<string, object> { { "loading", folder + "/" + slot } }));
        }

        // Same steps as LoadMenu.LoadCoroutine, hosted on the plugin's persistent object so a scene change cannot kill it.
        private static IEnumerator LoadRoutine(string path, string slot)
        {
            if (LoadingScreen.instance != null && !LoadingScreen.instance.FadeActive)
            {
                LoadingScreen.instance.Fade(Fadein: true, soundsMuted: true, 0f);
                if (GameController.instance != null) GameController.instance.GameIsPaused = false;
                while (LoadingScreen.instance != null && LoadingScreen.instance.Fading) yield return new WaitForSecondsRealtime(0.01f);
                yield return null;
            }
            yield return Plugin.Instance.StartCoroutine(SaveController.LoadGameWithMigration(path, slot, fromMenu: true));
        }

        private static void Save(Server.Request r, string[] a)
        {
            string slot = a.Length > 0 ? a[0] : "Slot1";
            if (!Regex.IsMatch(slot, @"^Slot([1-9]|10)$")) { r.Reply(Json.Error("slot must be Slot1..Slot10")); return; }
            if (PlayerLocator.instance == null || SaveController.Loading) { r.Reply(Json.Error("not in a loaded game, or a save/load is running")); return; }
            string who = Plugin.CurrentCharacter();
            if (!Plugin.IsTestCharacter(who)) { r.Reply(Json.Error($"refusing to save: the loaded character is '{who}', not the test character")); return; }
            SaveController.SaveGame(slot);
            r.Reply(Json.Ok(new Dictionary<string, object> { { "saved", who + "/" + slot } }));
        }

        // The game's own quit (PauseMenu.QuitDesktop / MainMenu.QuitOK) kills the process; do the same once no save is
        // being written and the map PNG task has finished.
        private static void Quit(Server.Request r, string[] a)
        {
            if (SaveController.Loading && !(a.Length > 0 && a[0] == "force")) { r.Reply(Json.Error("busy: a save or load is running (pass 'force' to quit anyway)")); return; }
            RevealCamera.WaitForPendingMapWrite();
            Plugin.Log.LogInfo("Quit requested by harness.");
            r.Reply(Json.Ok());
            Plugin.Instance.StartCoroutine(KillSoon());
        }

        private static IEnumerator KillSoon()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            Process.GetCurrentProcess().Kill();
        }

        // ---- helpers --------------------------------------------------------------------------------------------------

        private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        // Linux absolute paths are reachable through Wine's Z: drive.
        private static string WinPath(string p) => p.StartsWith("/") ? "Z:" + p.Replace('/', '\\') : p;

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            for (; t != null && parts.Count < 6; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static void Try(Dictionary<string, object> d, string key, Func<object> f)
        {
            try { d[key] = f(); }
            catch (Exception e) { d[key] = "error: " + e.GetType().Name + ": " + e.Message; }
        }

        private static void TryDo(Dictionary<string, object> d, string key, Action f)
        {
            try { f(); }
            catch (Exception e) { d[key] = "error: " + e.GetType().Name + ": " + e.Message; }
        }
    }

    [HarmonyPatch(typeof(DeveloperConsole), nameof(DeveloperConsole.Print))]
    internal static class ConsolePrintPatch
    {
        private static void Postfix(string text) => Commands.Captured?.Add(text);
    }
}
