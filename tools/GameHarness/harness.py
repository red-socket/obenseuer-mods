#!/usr/bin/env python3
"""Drive Obenseuer through the GameHarness plugin (localhost TCP) and the Steam client.

Run `python3 tools/GameHarness/harness.py --help`. Usage guide: tools/GameHarness/README.md.
Stdlib only. Safe to run with `python3 -I`.
"""
import argparse
import datetime as dt
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import time
from pathlib import Path

HOME = Path.home()
GAME = Path(os.environ.get("OBENSEUER_GAME", HOME / ".local/share/Steam/steamapps/common/Obenseuer"))
USERDATA = Path(os.environ.get(
    "OBENSEUER_USERDATA",
    HOME / ".local/share/Steam/steamapps/compatdata/951240/pfx/drive_c/users/steamuser/AppData/LocalLow/Loiste Interactive/Obenseuer"))
SAVES = USERDATA / "Saves"
PLUGINS = GAME / "BepInEx/plugins"
PARKED = GAME / "BepInEx/plugins-harness-off"   # where mod-off parks a mod (never plugins-disabled: that one is not ours)
BEPINEX_LOG = GAME / "BepInEx/LogOutput.log"
PLAYER_LOG = USERDATA / "Player.log"
DATA = HOME / ".cache/obenseuer-harness"          # session file, test-save snapshot, screenshots
SESSION = DATA / "session.json"
SHOTS = DATA / "shots"
REPO = Path(__file__).resolve().parents[2]

APP_ID = 951240
PORT = int(os.environ.get("OBENSEUER_HARNESS_PORT", "47913"))
TEST_CHAR = "Harness_Test"                        # must match [Harness] TestCharacter in the plugin config
TEST_FIRST, TEST_LAST = "Harness", "Test"
DEFAULT_BASE = "Helmi_Betoni/Slot1"
NEVER_TOUCH = {"ObenseuerPTBR", "GameHarness"}     # mod-off refuses these


class HarnessError(Exception):
    pass


# ---- process and channel ----------------------------------------------------------------------------------------------

def game_pids():
    """PIDs of running Obenseuer.exe processes (ps args, not pgrep -f, as the project rules ask)."""
    out = subprocess.run(["ps", "-eo", "pid,args"], capture_output=True, text=True).stdout
    pids = []
    for line in out.splitlines()[1:]:
        pid, _, args = line.strip().partition(" ")
        if "obenseuer.exe" in args.lower() and "harness.py" not in args:
            pids.append(int(pid))
    return pids


def load_session():
    try:
        return json.loads(SESSION.read_text())
    except (OSError, ValueError):
        return {}


def own_game_running():
    pids = game_pids()
    sess = load_session()
    return bool(pids) and bool(set(pids) & set(sess.get("pids", [])))


def require_own_game():
    pids = game_pids()
    if not pids:
        raise HarnessError("the game is not running (harness.py launch)")
    if not set(pids) & set(load_session().get("pids", [])):
        raise HarnessError("a game is running that harness.py did not launch: it may be the user's own game. Not touching it.")


def send(cmd, *args, timeout=130.0):
    """Send one command, return the parsed JSON reply (raises HarnessError when ok is false)."""
    line = "\t".join([cmd] + [str(a) for a in args]) + "\n"
    try:
        with socket.create_connection(("127.0.0.1", PORT), timeout=5) as s:
            s.settimeout(timeout)
            s.sendall(line.encode("utf-8"))
            buf = b""
            while not buf.endswith(b"\n"):
                chunk = s.recv(65536)
                if not chunk:
                    break
                buf += chunk
    except OSError as e:
        raise HarnessError(f"no harness on 127.0.0.1:{PORT} ({e})")
    reply = json.loads(buf.decode("utf-8"))
    if not reply.get("ok"):
        raise HarnessError(reply.get("error", "unknown error"))
    return reply


def try_send(cmd, *args):
    try:
        return send(cmd, *args, timeout=5)
    except (HarnessError, ValueError):
        return None


def wait_for(pred, timeout, what, poll=0.5):
    end = time.time() + timeout
    last = None
    while time.time() < end:
        st = try_send("state")
        if st is not None:
            last = st
            if pred(st):
                return st
        time.sleep(poll)
    raise HarnessError(f"timed out after {timeout:.0f}s waiting for {what}; last state: {json.dumps(last)[:400]}")


def wait_in_game(timeout=180, scene=None, settle=2.0):
    """Wait until a level is loaded and playable (optionally a given scene), then let it settle."""
    def ok(st):
        return st.get("inGame") and (scene is None or st.get("scene") == scene)
    st = wait_for(ok, timeout, f"in-game{' in ' + scene if scene else ''}")
    time.sleep(settle)
    return try_send("state") or st


# ---- launch / quit ---------------------------------------------------------------------------------------------------

def cmd_launch(a):
    if game_pids():
        raise HarnessError("Obenseuer.exe is already running (maybe the user's own game). Not launching a second one.")
    if not (PLUGINS / "GameHarness/GameHarness.dll").exists():
        raise HarnessError("GameHarness plugin is not installed (harness.py install)")
    DATA.mkdir(parents=True, exist_ok=True)
    SESSION.write_text(json.dumps({"launched": dt.datetime.now().isoformat(), "pids": []}))
    subprocess.Popen(["steam", "-applaunch", str(APP_ID)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
    t0 = time.time()
    end = t0 + a.timeout
    while time.time() < end and not game_pids():
        time.sleep(1)
    pids = game_pids()
    if not pids:
        raise HarnessError("the game process did not appear (is Steam running and logged in?)")
    SESSION.write_text(json.dumps({"launched": dt.datetime.now().isoformat(), "pids": pids}))
    print(f"game process up after {time.time() - t0:.0f}s (pid {pids})")
    while time.time() < end and try_send("ping") is None:
        if not game_pids():
            raise HarnessError("the game exited during startup; see harness.py log")
        time.sleep(1)
    def menu_ready(s):
        # Dismiss the "press any key" title screen the way a player would.
        if s.get("waitingForKey") and s.get("scene") == "Main Menu" and s.get("secondsSinceSceneLoad", 0) > 3:
            try_send("key", "Return")
        return s.get("mainMenuReady")
    st = wait_for(menu_ready, max(5, end - time.time()), "the main menu", poll=1.0)
    print(f"main menu ready after {time.time() - t0:.0f}s (game {send('ping')['game']})")
    return st


def cmd_quit(a):
    require_own_game()
    try:
        send("quit", *(["force"] if a.force else []), timeout=30)
    except HarnessError as e:
        if "no harness" not in str(e):
            raise
    end = time.time() + 60
    while time.time() < end and game_pids():
        time.sleep(0.5)
    if game_pids():
        raise HarnessError("the game did not exit within 60s")
    print("game closed")


# ---- install / mods --------------------------------------------------------------------------------------------------

def cmd_install(a):
    if game_pids():
        raise HarnessError("close the game first: a loaded plugin DLL must not be replaced")
    subprocess.run(["dotnet", "build", str(REPO / "tools/GameHarness"), "-c", "Release", "-p:Deploy=true"], check=True)
    print(f"installed into {PLUGINS / 'GameHarness'}")


def cmd_uninstall(a):
    if game_pids():
        raise HarnessError("close the game first")
    target = PLUGINS / "GameHarness"
    if target.exists():
        shutil.rmtree(target)
    print(f"removed {target} (config stays in BepInEx/config/redsocket.obenseuer.gameharness.cfg)")


def _mod_dir_check(name):
    if name in NEVER_TOUCH or "/" in name or name.startswith("."):
        raise HarnessError(f"harness.py does not move '{name}'")
    if game_pids():
        raise HarnessError("close the game first: plugin folders only change while the game is closed")


def cmd_mod_off(a):
    _mod_dir_check(a.mod)
    src, dst = PLUGINS / a.mod, PARKED / a.mod
    if not src.exists():
        raise HarnessError(f"{src} does not exist" + (" (already parked)" if dst.exists() else ""))
    PARKED.mkdir(parents=True, exist_ok=True)
    if dst.exists():
        shutil.rmtree(dst)
    src.rename(dst)
    print(f"parked {a.mod} in {PARKED}")


def cmd_mod_on(a):
    _mod_dir_check(a.mod)
    src, dst = PARKED / a.mod, PLUGINS / a.mod
    if not src.exists():
        raise HarnessError(f"{a.mod} is not parked")
    if dst.exists():
        raise HarnessError(f"{dst} already exists; remove the parked copy by hand if it is stale")
    src.rename(dst)
    print(f"restored {a.mod}")


# ---- test save -------------------------------------------------------------------------------------------------------

def _rename_identity(slot_dir, old_first, old_last):
    """Rewrite the copied save so the game saves it as the test character (Saves/Harness_Test), never the original."""
    old_char = re.sub(r"[^\w]", "", f"{old_first}_{old_last}")
    reps = [
        (f'"CharacterName":"{old_char}"'.encode(), f'"CharacterName":"{TEST_CHAR}"'.encode()),
        (f'"firstName":"{old_first}"'.encode(), f'"firstName":"{TEST_FIRST}"'.encode()),
        (f'"lastName":"{old_last}"'.encode(), f'"lastName":"{TEST_LAST}"'.encode()),
        (f'PlayerFirstName="{old_first}"'.encode(), f'PlayerFirstName="{TEST_FIRST}"'.encode()),
        (f'PlayerLastName="{old_last}"'.encode(), f'PlayerLastName="{TEST_LAST}"'.encode()),
        (f'FirstName="{old_first}"'.encode(), f'FirstName="{TEST_FIRST}"'.encode()),
        (f'LastName="{old_last}"'.encode(), f'LastName="{TEST_LAST}"'.encode()),
    ]
    for f in slot_dir.iterdir():
        if f.suffix not in (".tnmt", ".dialogue"):
            continue
        data = f.read_bytes()
        new = data
        for old, rep in reps:
            new = new.replace(old, rep)
        if new != data:
            f.write_bytes(new)
    g = (slot_dir / "Globals.tnmt").read_text(encoding="utf-8-sig")
    if f'"firstName":"{TEST_FIRST}","lastName":"{TEST_LAST}"' not in g:
        raise HarnessError("could not rewrite PlayerIdentity in Globals.tnmt; the copy is not safe to play")


def cmd_test_save(a):
    """Snapshot a base slot (read-only on the source) and install it as Saves/Harness_Test/Slot1."""
    if own_game_running() or game_pids():
        raise HarnessError("close the game first")
    base = DATA / "base"
    if a.source:
        src = SAVES / a.source
        if not (src / "Globals.tnmt").exists():
            raise HarnessError(f"{src} is not a save slot")
        info = json.loads((src / "Info.tnmt").read_text(encoding="utf-8-sig"))
        g = (src / "Globals.tnmt").read_text(encoding="utf-8-sig")
        m = re.search(r'"GUID":"PlayerIdentity","Data":\{[^}]*?"firstName":"([^"]*)","lastName":"([^"]*)"', g)
        if not m:
            raise HarnessError("PlayerIdentity not found in the source Globals.tnmt")
        if base.exists():
            shutil.rmtree(base)
        shutil.copytree(src, base / "Slot1")
        _rename_identity(base / "Slot1", m.group(1), m.group(2))
        (base / "SOURCE").write_text(f"{a.source}\nlevel={info.get('LevelName')}\ncopied={dt.datetime.now().isoformat()}\n")
    elif not (base / "Slot1").exists():
        raise HarnessError(f"no snapshot yet: run test-save --from <Character/Slot> (e.g. {DEFAULT_BASE})")
    target = SAVES / TEST_CHAR
    assert target.name == TEST_CHAR and target.parent == SAVES
    if target.exists():
        shutil.rmtree(target)
    shutil.copytree(base / "Slot1", target / "Slot1")
    print(f"installed {target / 'Slot1'} from {(base / 'SOURCE').read_text().splitlines()[0]}")


def saves_manifest():
    """(size, mtime) of every file under Saves/ except the test character: proof that a run left the user's saves alone."""
    m = {}
    for f in SAVES.rglob("*"):
        rel = f.relative_to(SAVES)
        # Top-level files such as steam_autocloud.vdf belong to Steam Cloud, not to any character.
        if f.is_file() and len(rel.parts) > 1 and rel.parts[0] != TEST_CHAR:
            st = f.stat()
            m[str(rel)] = (st.st_size, st.st_mtime_ns)
    return m


# ---- logs ------------------------------------------------------------------------------------------------------------

def read_log(path):
    try:
        return path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return []


ERROR_RE = re.compile(r"^\[(Error|Fatal)|Exception|NullReferenceException|^\[Warning *: *HarmonyX", re.I)


def log_errors(mod=None):
    """Error lines from this run's BepInEx log, optionally only those from one plugin's log source."""
    lines = read_log(BEPINEX_LOG)
    out = [l for l in lines if ERROR_RE.search(l)]
    if mod:
        out = [l for l in out if mod.lower() in l.lower().replace(" ", "")]
    return out


def cmd_log(a):
    paths = {"bepinex": [BEPINEX_LOG], "player": [PLAYER_LOG], "both": [BEPINEX_LOG, PLAYER_LOG]}[a.source]
    for p in paths:
        lines = read_log(p)
        if a.errors:
            lines = [l for l in lines if ERROR_RE.search(l)]
        if a.grep:
            rx = re.compile(a.grep, re.I)
            lines = [l for l in lines if rx.search(l)]
        print(f"==> {p} ({len(lines)} lines shown from the end, up to {a.lines})")
        for l in lines[-a.lines:]:
            print(l)


# ---- screenshots -----------------------------------------------------------------------------------------------------

def shot(name="shot", mode="full", out=None):
    out_dir = Path(out) if out else SHOTS
    out_dir.mkdir(parents=True, exist_ok=True)
    path = out_dir / f"{dt.datetime.now():%Y%m%d-%H%M%S}-{name}.png"
    r = send("screenshot", str(path.resolve()), mode)
    for _ in range(50):
        if path.exists() and path.stat().st_size > 0:
            break
        time.sleep(0.1)
    print(f"{path} ({r['width']}x{r['height']}, {mode})")
    return path


def cmd_shot(a):
    require_own_game()
    shot(a.name, a.mode, a.out)


# ---- generic commands --------------------------------------------------------------------------------------------------

def cmd_raw(a):
    if a.command not in ("ping", "state", "mods", "doors", "find", "buttons"):
        require_own_game()
    r = send(a.command, *a.args)
    r.pop("ok", None)
    print(json.dumps(r, indent=1, ensure_ascii=False))


def cmd_wait(a):
    require_own_game()
    if a.menu:
        wait_for(lambda s: s.get("mainMenuReady"), a.timeout, "the main menu")
    else:
        wait_in_game(a.timeout, a.scene)
    print(json.dumps(send("state"), indent=1, ensure_ascii=False))


# ---- release checklist (CLAUDE.md) -----------------------------------------------------------------------------------

def cmd_checklist(a):
    """Run the CLAUDE.md release checklist for one mod on the test save, with a screenshot at every step."""
    mod = a.mod
    if not (PLUGINS / mod).exists():
        raise HarnessError(f"{mod} is not installed in {PLUGINS}")
    if game_pids():
        raise HarnessError("close the game first: the checklist starts with a fresh launch")
    if not (SAVES / TEST_CHAR / "Slot1/Globals.tnmt").exists():
        raise HarnessError("no test save (harness.py test-save --from <Character/Slot>)")
    out = Path(a.out) if a.out else SHOTS / f"checklist-{mod}-{dt.datetime.now():%Y%m%d-%H%M%S}"
    out.mkdir(parents=True, exist_ok=True)
    report = [f"# Release checklist: {mod}", "", f"Started {dt.datetime.now():%Y-%m-%d %H:%M}. Test save: Saves/{TEST_CHAR}.", ""]
    results = []
    manifest = saves_manifest()

    def step(title, passed, detail="", shots=()):
        results.append(passed)
        mark = "PASS" if passed else "FAIL"
        print(f"[{mark}] {title}{': ' + detail if detail else ''}")
        report.append(f"## {mark}: {title}")
        if detail:
            report.append("")
            report.append(detail)
        for s in shots:
            report.append(f"- {s}")
        report.append("")

    def errors_since(before):
        return [l for l in log_errors() if l not in before]

    try:
        # 1. Fresh launch, plugin loads with no errors.
        cmd_launch(argparse.Namespace(timeout=240))
        guid_hit = [p for p in send("mods")["plugins"] if p["guid"].endswith("." + mod.lower())]
        errs = log_errors()
        mod_errs = [l for l in errs if mod.lower() in l.lower().replace(" ", "")]
        loaded = bool(guid_hit and guid_hit[0]["loaded"])
        s1 = shot("1-menu", "full", out)
        step("1. Fresh launch", loaded and not mod_errs,
             f"{mod} loaded: {loaded} ({guid_hit[0]['version'] if guid_hit else 'not found'}); "
             f"errors from {mod}: {len(mod_errs)}; error lines in the whole log: {len(errs)}"
             + ("".join("\n    " + l for l in mod_errs[:10])), [s1])

        # 2. Load the test save.
        before = log_errors()
        send("load", "Slot1")
        st = wait_in_game(240)
        s2 = shot("2-loaded", "full", out)
        e2 = errors_since(before)
        step("2. Load the test save", True, f"scene {st['scene']}, position {st.get('position')}; new error lines: {len(e2)}"
             + "".join("\n    " + l for l in e2[:10]), [s2])
        first_scene = st["scene"]

        # 3. Go through a door.
        before = log_errors()
        doors = [d for d in send("doors")["doors"] if not d.get("locked")]
        if not doors:
            raise HarnessError("no unlocked level-change door in this scene")
        d = doors[0]
        send("goto_door", d["index"])
        time.sleep(1.5)
        s3a = shot("3-door", "full", out)
        send("door", d["index"])
        wait_for(lambda s: s.get("loading") or s.get("scene") != first_scene, 30, "the level change to start")
        st = wait_in_game(240, d["to"])
        s3b = shot("3-through-door", "full", out)
        e3 = errors_since(before)
        step("3. Go through a door", st["scene"] == d["to"],
             f"{first_scene} -> {st['scene']} via {d['object']}; new error lines: {len(e3)}" + "".join("\n    " + l for l in e3[:10]), [s3a, s3b])

        # 4. Save, then load that save again.
        before = log_errors()
        send("save", "Slot2")
        time.sleep(3)
        saved_scene = st["scene"]
        send("load", "Slot2")
        wait_for(lambda s: s.get("loading"), 30, "the load to start")
        st = wait_in_game(240, saved_scene)
        s4 = shot("4-reloaded", "full", out)
        e4 = errors_since(before)
        step("4. Save to Slot2 and load it again", st["scene"] == saved_scene and st["save"]["slot"] == "Slot2",
             f"reloaded {st['save']['slot']} in {st['scene']}; new error lines: {len(e4)}" + "".join("\n    " + l for l in e4[:10]), [s4])
        cmd_quit(argparse.Namespace(force=False))

        # 5. Remove the mod and load the same save.
        cmd_mod_off(argparse.Namespace(mod=mod))
        try:
            cmd_launch(argparse.Namespace(timeout=240))
            gone = not [p for p in send("mods")["plugins"] if p["guid"].endswith("." + mod.lower())]
            send("load", "Slot2")
            st = wait_in_game(240, saved_scene)
            s5 = shot("5-without-mod", "full", out)
            e5 = log_errors()
            step(f"5. {mod} removed, same save loads", gone and st["scene"] == saved_scene,
                 f"{mod} absent: {gone}; scene {st['scene']}; error lines in the whole log: {len(e5)}" + "".join("\n    " + l for l in e5[:10]), [s5])
            cmd_quit(argparse.Namespace(force=False))
        finally:
            if game_pids() and own_game_running():
                cmd_quit(argparse.Namespace(force=True))
            cmd_mod_on(argparse.Namespace(mod=mod))
    except HarnessError as e:
        step("aborted", False, str(e))
        if own_game_running():
            try:
                cmd_quit(argparse.Namespace(force=True))
            except HarnessError:
                pass
    after = saves_manifest()
    changed = sorted(k for k in set(manifest) | set(after) if manifest.get(k) != after.get(k))
    step("Other characters' saves untouched", not changed,
         f"{len(manifest)} files outside Saves/{TEST_CHAR} compared before and after; changed: {len(changed)}"
         + "".join("\n    " + k for k in changed[:20]))
    verdict = "PASS" if results and all(results) else "FAIL"
    report.insert(1, f"\n**{verdict}**\n")
    (out / "report.md").write_text("\n".join(report) + "\n")
    print(f"{verdict}: report in {out / 'report.md'}")
    if verdict != "PASS":
        sys.exit(1)


# ---- CLI -------------------------------------------------------------------------------------------------------------

SHORTCUTS = {
    # name: (help, fixed command)
    "state": "Game state: scene, loading, menu, position, look, aim, target, time, UI, held keys",
    "mods": "Loaded BepInEx plugins",
    "console": "Run an F1 console command, e.g. console add_item 12 5 (output comes back)",
    "key": "Press a Unity KeyCode: key E | key Tab | key F4 0.5 (hold seconds)",
    "action": "Press a game action by its binding: action Interact | action Inventory | action Pause Menu",
    "move": "Hold a movement action: move forward 1.5 | move left 0.3 | move jump 0.1",
    "release": "Release every held virtual key",
    "look": "Look at yaw/pitch degrees (pitch + = up): look 90 -10",
    "turn": "Turn by dyaw/dpitch: turn 45 0",
    "lookat": "Face a world point: lookat x y z",
    "tp": "Teleport: tp x y z [yaw] [pitch]",
    "find": "Find scene objects by name: find Sink [limit]",
    "doors": "List level-change doors, nearest first",
    "door": "Use door N (same as pressing E on it): door 0",
    "goto_door": "Stand at door N's arrival point, facing it",
    "buttons": "List clickable UI elements: buttons [filter]",
    "click": "Click a UI element by label or path: click Load",
    "load": "Load a test-character slot: load Slot1",
    "save": "Save the test character into a slot: save Slot2",
}


def main():
    p = argparse.ArgumentParser(description="Play Obenseuer from scripts through the GameHarness plugin.")
    sub = p.add_subparsers(dest="sub", required=True)

    s = sub.add_parser("launch", help="Start the game through Steam and wait for the main menu")
    s.add_argument("--timeout", type=float, default=240)
    s.set_defaults(fn=cmd_launch)
    s = sub.add_parser("quit", help="Close the game the way its own Quit button does")
    s.add_argument("--force", action="store_true", help="quit even during a save/load")
    s.set_defaults(fn=cmd_quit)
    s = sub.add_parser("wait", help="Wait until in game (optionally in a scene) or at the main menu")
    s.add_argument("--scene")
    s.add_argument("--menu", action="store_true")
    s.add_argument("--timeout", type=float, default=180)
    s.set_defaults(fn=cmd_wait)
    s = sub.add_parser("shot", help="Screenshot to a PNG and print its path")
    s.add_argument("name", nargs="?", default="shot")
    s.add_argument("--mode", choices=["full", "nohud", "clean"], default="full",
                   help="full: as seen; nohud: game HUD hidden; clean: every screen-space UI hidden (for art)")
    s.add_argument("--out", help=f"directory (default {SHOTS})")
    s.set_defaults(fn=cmd_shot)
    s = sub.add_parser("log", help="Tail the BepInEx and/or Unity logs of the current run")
    s.add_argument("--source", choices=["bepinex", "player", "both"], default="bepinex")
    s.add_argument("--lines", type=int, default=40)
    s.add_argument("--grep")
    s.add_argument("--errors", action="store_true")
    s.set_defaults(fn=cmd_log)
    s = sub.add_parser("test-save", help=f"Install the test save as Saves/{TEST_CHAR}/Slot1 (resets it)")
    s.add_argument("--from", dest="source", metavar="CHAR/SLOT", help=f"take a new snapshot from this slot (read only), e.g. {DEFAULT_BASE}")
    s.set_defaults(fn=cmd_test_save)
    s = sub.add_parser("install", help="Build and deploy the GameHarness plugin (game closed)")
    s.set_defaults(fn=cmd_install)
    s = sub.add_parser("uninstall", help="Remove the GameHarness plugin folder (game closed)")
    s.set_defaults(fn=cmd_uninstall)
    s = sub.add_parser("mod-off", help="Park a mod's plugin folder outside plugins/ (game closed)")
    s.add_argument("mod")
    s.set_defaults(fn=cmd_mod_off)
    s = sub.add_parser("mod-on", help="Put a parked mod back")
    s.add_argument("mod")
    s.set_defaults(fn=cmd_mod_on)
    s = sub.add_parser("checklist", help="Run the CLAUDE.md release checklist for one mod (game closed at start)")
    s.add_argument("mod")
    s.add_argument("--out")
    s.set_defaults(fn=cmd_checklist)
    s = sub.add_parser("cmd", help="Send any plugin command: cmd <command> [args...]")
    s.add_argument("command")
    s.add_argument("args", nargs=argparse.REMAINDER)
    s.set_defaults(fn=cmd_raw)
    for name, helptext in SHORTCUTS.items():
        s = sub.add_parser(name.replace("_", "-"), help=helptext)
        s.add_argument("args", nargs=argparse.REMAINDER)
        s.set_defaults(fn=cmd_raw, command=name)

    a = p.parse_args()
    if a.sub in ("console", "action") and a.args:
        a.args = [" ".join(a.args)] if a.sub == "console" else a.args
    try:
        a.fn(a)
    except HarnessError as e:
        print(f"error: {e}", file=sys.stderr)
        sys.exit(2)


if __name__ == "__main__":
    main()
