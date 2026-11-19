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
import signal
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
SETTINGS_SNAPSHOT = DATA / "settings"             # the user's global settings files, as they were before our game ran
# Global (not per-save) settings the game rewrites at startup and from its options menu. A harness game must never leave
# them changed: a relaunch once came up at 320x200 and wrote that into Display.json. KeyBindings.json usually does not
# exist; its absence is part of the snapshot.
SETTINGS_FILES = ("Display.json", "Quality.json", "Controls.json", "Volume.json", "KeyBindings.json")
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

GAME_ARG = re.compile(r"[\\/]obenseuer\.exe$", re.I)


def game_pids():
    """PIDs of the running game: processes with an argument that is a path to Obenseuer.exe (the Windows process and
    its Steam/Proton wrappers). Listed with ps, not pgrep -f, as the project rules ask; a shell or grep whose command
    line merely mentions obenseuer.exe has no such argument and does not count."""
    out = subprocess.run(["ps", "-eo", "pid,args"], capture_output=True, text=True).stdout
    pids = []
    for line in out.splitlines()[1:]:
        pid, _, args = line.strip().partition(" ")
        if "obenseuer.exe" not in args.lower():
            continue
        try:
            argv = Path(f"/proc/{pid}/cmdline").read_bytes().split(b"\0")
        except OSError:
            continue
        if any(GAME_ARG.search(t.decode("utf-8", "replace")) for t in argv):
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


def save_session(**kw):
    sess = load_session()
    sess.update(kw)
    DATA.mkdir(parents=True, exist_ok=True)
    SESSION.write_text(json.dumps(sess))


def record_pids():
    """Add every running Obenseuer.exe process to the session. Only called while our own game runs: launch refuses to
    start next to another game, and Steam does not start a second instance of the app."""
    pids = sorted(set(load_session().get("pids", [])) | set(game_pids()))
    save_session(pids=pids)
    return pids


def kill_own(reason):
    """SIGKILL the game this harness launched (only PIDs in session.json that still are Obenseuer.exe processes)."""
    own = set(load_session().get("pids", []))
    running = game_pids()
    if not any(p in own for p in running):
        return
    # While our game is alive every game process is ours (launch refuses to start next to another game, and Steam runs
    # one instance), including the Windows process, which can appear after the PIDs were last recorded.
    own |= set(running)
    save_session(pids=sorted(own))
    targets = running
    print(f"killing the harness game ({reason}): pids {targets}", file=sys.stderr)
    # The Windows process first, so the Steam/pressure-vessel wrappers can exit on their own; then whatever is left.
    exe = [p for p in targets if GAME_ARG.search(_args(p).split(" ")[0])]
    for group in (exe or targets, targets):
        for pid in group:
            try:
                os.kill(pid, signal.SIGKILL)
            except OSError:
                pass
        end = time.time() + 15
        while time.time() < end and any(p in own for p in game_pids()):
            time.sleep(0.5)
        if not any(p in own for p in game_pids()):
            break
    save_session(closed=time.time())
    if any(p in own for p in game_pids()):
        raise HarnessError(f"could not kill the harness game (pids {[p for p in game_pids() if p in own]})")
    settings_after_exit("changed by the harness game that was killed")


def _args(pid):
    try:
        return Path(f"/proc/{pid}/cmdline").read_bytes().replace(b"\0", b" ").decode("utf-8", "replace")
    except OSError:
        return ""


class GameHang(HarnessError):
    """The game stopped answering (frozen main thread) or never reached the expected state."""


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


FROZEN_AFTER = 45   # seconds without any answer from a game that answered before: its main thread is stuck


def wait_for(pred, timeout, what, poll=0.5):
    """Poll state until pred(state). Raises GameHang when the game stops answering or exits, HarnessError on timeout."""
    end = time.time() + timeout
    last, last_answer = None, time.time()
    while time.time() < end:
        st = try_send("state")
        if st is not None:
            last, last_answer = st, time.time()
            if pred(st):
                return st
        else:
            if not game_pids():
                raise GameHang(f"the game exited while waiting for {what}")
            if time.time() - last_answer > FROZEN_AFTER:
                raise GameHang(f"the game stopped answering for {FROZEN_AFTER}s while waiting for {what} (frozen)")
        time.sleep(poll)
    raise GameHang(f"timed out after {timeout:.0f}s waiting for {what}; last state: {json.dumps(last)[:400]}")


def wait_in_game(timeout=180, scene=None, settle=2.0):
    """Wait until a level is loaded and playable (optionally a given scene), then let it settle."""
    def ok(st):
        return st.get("inGame") and (scene is None or st.get("scene") == scene)
    st = wait_for(ok, timeout, f"in-game{' in ' + scene if scene else ''}")
    time.sleep(settle)
    return try_send("state") or st


# ---- user settings guard ----------------------------------------------------------------------------------------------

SETTINGS_RESTORED = []   # names restored by this process (checklist reports them as a failure)


def _settings_state():
    try:
        return json.loads((SETTINGS_SNAPSHOT / "state.json").read_text())
    except (OSError, ValueError):
        return None


def snapshot_settings(pending):
    """Copy the settings files (or note their absence) into the snapshot. pending=True: a harness game is about to run
    or running, so the next exit or launch must compare against this snapshot."""
    tmp = SETTINGS_SNAPSHOT.with_name("settings.tmp")
    if tmp.exists():
        shutil.rmtree(tmp)
    tmp.mkdir(parents=True)
    files = {}
    for name in SETTINGS_FILES:
        src = USERDATA / name
        if src.is_file():
            shutil.copy2(src, tmp / name)
            files[name] = True
        else:
            files[name] = False
    (tmp / "state.json").write_text(json.dumps({"taken": dt.datetime.now().isoformat(), "pending": pending, "files": files}))
    if SETTINGS_SNAPSHOT.exists():
        shutil.rmtree(SETTINGS_SNAPSHOT)
    tmp.rename(SETTINGS_SNAPSHOT)


def _set_pending(pending):
    st = _settings_state()
    if st is not None:
        st["pending"] = pending
        (SETTINGS_SNAPSHOT / "state.json").write_text(json.dumps(st))


def changed_settings():
    """Names of settings files that differ from the snapshot (content, or appeared/disappeared)."""
    st = _settings_state()
    if st is None:
        return []
    out = []
    for name, existed in st["files"].items():
        cur = USERDATA / name
        if existed != cur.is_file():
            out.append(name)
        elif existed and cur.read_bytes() != (SETTINGS_SNAPSHOT / name).read_bytes():
            out.append(name)
    return out


def restore_settings(why):
    """Put back every settings file that differs from the snapshot, one warning line per file. Only call it while no
    game is running (the game would write over the restored file)."""
    st = _settings_state()
    if st is None:
        return []
    names = changed_settings()
    for name in names:
        dst = USERDATA / name
        if st["files"][name]:
            tmp = dst.with_name(dst.name + ".harness-tmp")
            shutil.copy2(SETTINGS_SNAPSHOT / name, tmp)
            os.replace(tmp, dst)
        elif dst.exists():
            dst.unlink()
        print(f"warning: restored USERDATA/{name} ({why})", file=sys.stderr)
    SETTINGS_RESTORED.extend(names)
    return names


def settings_after_exit(why):
    """After our game is gone: undo any settings change it made and close the snapshot."""
    if game_pids():
        return
    restore_settings(why)
    _set_pending(False)


def settings_before_launch():
    """If the last harness game never got its after-exit check (the process died uncleanly), restore from that snapshot;
    then snapshot the current files, which are the user's settings."""
    st = _settings_state()
    if st is not None and st.get("pending"):
        restore_settings("changed by an earlier harness game that did not exit cleanly")
    snapshot_settings(pending=True)


def cmd_settings(a):
    if a.action == "check":
        st = _settings_state()
        if st is None:
            raise HarnessError("no settings snapshot yet (harness.py settings seed)")
        ch = changed_settings()
        print(f"snapshot {st['taken']} (pending: {st['pending']}); changed: {', '.join(ch) if ch else 'none'}")
    elif a.action == "seed":
        snapshot_settings(pending=own_game_running())
        print(f"snapshot of {', '.join(n for n in SETTINGS_FILES if (USERDATA / n).is_file())} saved in {SETTINGS_SNAPSHOT}")
    elif a.action == "restore":
        if game_pids():
            raise HarnessError("close the game first: it would write over the restored files")
        names = restore_settings("harness.py settings restore")
        _set_pending(False)
        print(f"restored: {', '.join(names) if names else 'nothing (unchanged)'}")


# ---- display checks --------------------------------------------------------------------------------------------------

class NoDisplay(HarnessError):
    """The game has no real screen to render to (monitor off, asleep or unplugged)."""


MIN_SHOT = (640, 360)   # a smaller capture means the game has no real screen (no monitor: 1024x768 window, 1x1 shots)


def mutter_has_monitor():
    """False when GNOME drives no monitor at all, True when it drives one, None when Mutter cannot be asked (not GNOME).
    GetCurrentState returns (serial, monitors, logical_monitors, props); GVariant prints an empty monitor list as a typed
    empty array, "@a((ssss)...) []", and a non-empty one as a plain list."""
    try:
        out = subprocess.run(["gdbus", "call", "--session", "--dest", "org.gnome.Mutter.DisplayConfig",
                              "--object-path", "/org/gnome/Mutter/DisplayConfig",
                              "--method", "org.gnome.Mutter.DisplayConfig.GetCurrentState"],
                             capture_output=True, text=True, timeout=10)
    except (OSError, subprocess.TimeoutExpired):
        return None
    if out.returncode != 0 or not out.stdout.startswith("(uint32 "):
        return None
    return not re.match(r"\(uint32 \d+, @a\(\(ssss\)[^ ]* \[\]", out.stdout)


def require_display():
    if mutter_has_monitor() is False:
        raise NoDisplay("no usable display: GNOME reports no monitor (it is off, asleep or unplugged). The game would "
                        "start at 1024x768 and screenshots would be 1x1. Wake the screen and launch again.")


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    if head[:8] != b"\x89PNG\r\n\x1a\n":
        return (0, 0)
    return int.from_bytes(head[16:20], "big"), int.from_bytes(head[20:24], "big")


def probe_display():
    """A screenshot right after the menu comes up: a tiny one means the game has no real screen to render to."""
    probe = DATA / "display-probe.png"
    try:
        r = send("screenshot", str(probe.resolve()), "full", timeout=30)
        w, h = r["width"], r["height"]
    finally:
        probe.unlink(missing_ok=True)
    if w < MIN_SHOT[0] or h < MIN_SHOT[1]:
        raise NoDisplay(f"no usable display: the game renders at {w}x{h} (monitor off or asleep?). Wake the screen and launch again.")
    return w, h


# ---- launch / quit ---------------------------------------------------------------------------------------------------

def last_game_exit():
    """Best guess of when any Obenseuer.exe (ours or the user's) last ran: our recorded close, or the newest log write."""
    times = [load_session().get("closed", 0)]
    for p in (PLAYER_LOG, BEPINEX_LOG):
        try:
            times.append(p.stat().st_mtime)
        except OSError:
            pass
    return max(times)


def cooldown(seconds):
    """Proton sometimes deadlocks a game started within about a minute of the previous one closing (0% CPU, logs stop
    after the main menu loads). Wait until `seconds` have passed since the last run."""
    wait = last_game_exit() + seconds - time.time()
    if wait > 0:
        print(f"cooldown: waiting {wait:.0f}s since the last game exit", file=sys.stderr)
        time.sleep(wait)


def _launch_once(timeout):
    save_session(launched=dt.datetime.now().isoformat(), pids=[])
    subprocess.Popen(["steam", "-applaunch", str(APP_ID)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
    t0 = time.time()
    end = t0 + timeout
    while time.time() < t0 + 60 and not game_pids():
        time.sleep(1)
    if not game_pids():
        raise HarnessError("the game process did not appear within 60s (is Steam running and logged in?)")
    pids = record_pids()
    print(f"game process up after {time.time() - t0:.0f}s (pid {pids})")
    # The plugin answers about 10 s after the process starts; give it 120 s before calling the start a hang.
    while try_send("ping") is None:
        record_pids()
        if not game_pids():
            raise GameHang("the game exited during startup; see harness.py log")
        if time.time() > min(end, t0 + 120):
            raise GameHang("the plugin did not answer within 120s of the game starting (frozen at startup)")
        time.sleep(1)
    record_pids()
    tries = {"n": 0, "next": 0.0}
    keys = ["Return", "Space", "Mouse0"]

    def menu_ready(s):
        # Dismiss the "press any key" title screen the way a player would, trying another key every few seconds, and
        # after three tries call the screen's own keyboard handler.
        if s.get("waitingForKey") and s.get("scene") == "Main Menu" and s.get("secondsSinceSceneLoad", 0) > 3 and time.time() >= tries["next"]:
            if tries["n"] < len(keys):
                try_send("key", keys[tries["n"]], "0.2")
            else:
                try_send("dismiss_title")
            tries["n"] += 1
            tries["next"] = time.time() + 4
        return s.get("mainMenuReady")
    st = wait_for(menu_ready, max(5, end - time.time()), "the main menu", poll=1.0)
    record_pids()
    w, h = probe_display()
    print(f"main menu ready after {time.time() - t0:.0f}s (game {send('ping')['game']}, {w}x{h})")
    return st


def launch(timeout=240, retries=1, cool=60):
    """Start the game and wait for the main menu. On a hang, kill only our own game, cool down and try again."""
    if game_pids():
        raise HarnessError("Obenseuer.exe is already running (maybe the user's own game). Not launching a second one.")
    if not (PLUGINS / "GameHarness/GameHarness.dll").exists():
        raise HarnessError("GameHarness plugin is not installed (harness.py install)")
    require_display()
    for attempt in range(retries + 1):
        if game_pids():
            raise HarnessError(f"game processes still running before launch attempt {attempt + 1}: {game_pids()}")
        cooldown(cool)
        settings_before_launch()
        try:
            return _launch_once(timeout)
        except NoDisplay:
            kill_own("no usable display")   # a retry would hit the same missing screen
            raise
        except GameHang as e:
            kill_own(str(e))
            if attempt == retries:
                raise
            print(f"launch hung ({e}); retrying", file=sys.stderr)


def cmd_launch(a):
    return launch(a.timeout, a.retries, a.cooldown)


def quit_game(force=False):
    """Close our game the way its Quit button does; if the plugin does not answer or the game does not exit, kill it."""
    require_own_game()
    record_pids()
    try:
        send("quit", *(["force"] if force else []), timeout=5 if force else 30)
    except HarnessError as e:
        if "busy" in str(e) and not force:
            raise
        print(f"quit through the plugin failed ({e}); killing the game instead", file=sys.stderr)
        kill_own("plugin quit failed")
    end = time.time() + (15 if force else 60)
    while time.time() < end and game_pids():
        time.sleep(0.5)
    if game_pids():
        kill_own("did not exit after quit")
    save_session(closed=time.time())
    settings_after_exit("changed by the harness game")
    print("game closed")


def cmd_quit(a):
    quit_game(a.force)


def cmd_kill(a):
    require_own_game()
    record_pids()
    kill_own("harness.py kill")
    print("game killed")


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


# BepInEx prefixes every line with its level. Unity errors and exceptions arrive as "[Error  : Unity Log] ...".
ERROR_RE = re.compile(r"^\[(Error|Fatal)\b")
WARNING_RE = re.compile(r"^\[Warning\b")


def _only_mod(lines, mod):
    return [l for l in lines if mod.lower() in l.lower().replace(" ", "")] if mod else lines


def log_errors(mod=None):
    """Error and fatal lines from this run's BepInEx log, optionally only those that name one plugin."""
    return _only_mod([l for l in read_log(BEPINEX_LOG) if ERROR_RE.search(l)], mod)


def log_warnings(mod=None):
    """Warning lines (e.g. HarmonyX's AccessTools.TypeByName misses): reported, never a failure."""
    return _only_mod([l for l in read_log(BEPINEX_LOG) if WARNING_RE.search(l)], mod)


def cmd_log(a):
    paths = {"bepinex": [BEPINEX_LOG], "player": [PLAYER_LOG], "both": [BEPINEX_LOG, PLAYER_LOG]}[a.source]
    for p in paths:
        lines = read_log(p)
        if a.errors or a.warnings:
            lines = [l for l in lines if (a.errors and ERROR_RE.search(l)) or (a.warnings and WARNING_RE.search(l))]
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
    if r["width"] < MIN_SHOT[0] or r["height"] < MIN_SHOT[1]:
        raise NoDisplay(f"screenshot is only {r['width']}x{r['height']} ({path}): no usable display (monitor off or asleep?)")
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
        raise HarnessError("close the game first: the checklist starts with a fresh launch (harness.py quit, or kill)")
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

    def excerpt(lines):
        return "".join("\n    " + l for l in lines[:10])

    def launch_and_load(slot, scene=None):
        """Launch and load, recovering once from a frozen game: kill our game, cool down, relaunch, load again."""
        for attempt in range(2):
            try:
                if not game_pids():
                    launch(240, 1, a.cooldown)
                send("load", slot)
                return wait_in_game(240, scene), attempt
            except GameHang as e:
                if attempt == 1:
                    raise
                print(f"load hung ({e}); killing the game and retrying", file=sys.stderr)
                kill_own(str(e))
                notes.append(f"Recovered from a hang while loading {slot}: {e}")

    notes = []
    try:
        # 1. Fresh launch, plugin loads with no errors.
        launch(240, 1, a.cooldown)
        guid_hit = [p for p in send("mods")["plugins"] if p["guid"].endswith("." + mod.lower())]
        errs, warns = log_errors(), log_warnings()
        mod_errs, mod_warns = _only_mod(errs, mod), _only_mod(warns, mod)
        loaded = bool(guid_hit and guid_hit[0]["loaded"])
        s1 = shot("1-menu", "full", out)
        step("1. Fresh launch", loaded and not mod_errs,
             f"{mod} loaded: {loaded} ({guid_hit[0]['version'] if guid_hit else 'not found'}); "
             f"errors from {mod}: {len(mod_errs)}, warnings from {mod}: {len(mod_warns)}; "
             f"whole log: {len(errs)} errors, {len(warns)} warnings" + excerpt(mod_errs + mod_warns), [s1])

        # 2. Load the test save (a fresh launch and load again if the game freezes).
        before = log_errors()
        st, _ = launch_and_load("Slot1")
        if game_pids() and not [p for p in send("mods")["plugins"] if p["guid"].endswith("." + mod.lower())]:
            raise HarnessError(f"{mod} is not loaded after the relaunch")
        s2 = shot("2-loaded", "full", out)
        e2 = errors_since(before)
        step("2. Load the test save", True, f"scene {st['scene']}, position {st.get('position')}; new error lines: {len(e2)}" + excerpt(e2), [s2])
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
             f"{first_scene} -> {st['scene']} via {d['object']}; new error lines: {len(e3)}" + excerpt(e3), [s3a, s3b])

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
             f"reloaded {st['save']['slot']} in {st['scene']}; new error lines: {len(e4)}" + excerpt(e4), [s4])
        quit_game()

        # 5. Remove the mod and load the same save.
        cmd_mod_off(argparse.Namespace(mod=mod))
        try:
            st, _ = launch_and_load("Slot2", saved_scene)
            gone = not [p for p in send("mods")["plugins"] if p["guid"].endswith("." + mod.lower())]
            s5 = shot("5-without-mod", "full", out)
            e5, w5 = log_errors(), log_warnings()
            step(f"5. {mod} removed, same save loads", gone and st["scene"] == saved_scene,
                 f"{mod} absent: {gone}; scene {st['scene']}; whole log: {len(e5)} errors, {len(w5)} warnings" + excerpt(e5), [s5])
            quit_game()
        finally:
            if own_game_running():
                quit_game(force=True)
            cmd_mod_on(argparse.Namespace(mod=mod))
    except HarnessError as e:
        step("aborted", False, str(e))
        if own_game_running():
            try:
                quit_game(force=True)
            except HarnessError:
                pass
    for n in notes:
        report.append(f"Note: {n}")
        report.append("")
    changed = sorted(set(SETTINGS_RESTORED) | set(changed_settings()))
    step("User settings unchanged", not changed,
         f"{', '.join(SETTINGS_FILES)} in USERDATA compared with the snapshot taken before each launch; "
         + (f"changed and restored: {', '.join(changed)}" if changed else "no change"))
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

    s = sub.add_parser("launch", help="Start the game through Steam and wait for the main menu (with hang recovery)")
    s.add_argument("--timeout", type=float, default=240)
    s.add_argument("--retries", type=int, default=1, help="relaunches after a hang (default 1)")
    s.add_argument("--cooldown", type=float, default=60, help="seconds to wait after any game exit before starting (default 60)")
    s.set_defaults(fn=cmd_launch)
    s = sub.add_parser("quit", help="Close the game the way its own Quit button does; kills it if it does not answer or exit")
    s.add_argument("--force", action="store_true", help="quit even during a save/load, and kill after 15s instead of 60s")
    s.set_defaults(fn=cmd_quit)
    s = sub.add_parser("kill", help="Kill the game harness.py launched (never any other game)")
    s.set_defaults(fn=cmd_kill)
    s = sub.add_parser("settings", help="The user's global settings guard: check against the snapshot, seed a new snapshot, or restore")
    s.add_argument("action", choices=["check", "seed", "restore"])
    s.set_defaults(fn=cmd_settings)
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
    s.add_argument("--errors", action="store_true", help="only [Error]/[Fatal] lines")
    s.add_argument("--warnings", action="store_true", help="only [Warning] lines (combine with --errors for both)")
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
    s.add_argument("--cooldown", type=float, default=60, help="seconds between a game exit and the next launch (default 60)")
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
