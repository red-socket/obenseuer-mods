#!/usr/bin/env python3
"""Build a Nexus release archive for one mod.

Usage: python3 -I tools/package.py <ModName>

Builds the Release DLL (no deploy), checks that the [BepInPlugin] version in
Plugin.cs, the csproj <Version> and the newest CHANGELOG.md heading agree, and
writes .internal/releases/<ModName>/<ModName>-<version>.zip from an explicit allowlist:

    BepInEx/plugins/<ModName>/<ModName>.dll
    BepInEx/plugins/<ModName>/CHANGELOG.md

Nothing else ever goes into the archive (no NEXUS.md, .internal/, art/,
DESCRIPTION.txt, Debug or test builds).
"""

import hashlib
import re
import subprocess
import sys
import time
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RELEASES = ROOT / ".internal" / "releases"
FRAMEWORK = "net472"


def fail(msg):
    print(f"package.py: error: {msg}", file=sys.stderr)
    sys.exit(1)


def strip_comments(src):
    src = re.sub(r"/\*.*?\*/", "", src, flags=re.S)
    return re.sub(r"//[^\n]*", "", src)


def plugin_version(mod_dir):
    plugin = mod_dir / "Plugin.cs"
    if not plugin.is_file():
        fail(f"{plugin} not found")
    src = strip_comments(plugin.read_text(encoding="utf-8"))
    m = re.search(r"\[BepInPlugin\(\s*([^,]+?)\s*,\s*([^,]+?)\s*,\s*([^)]+?)\s*\)\]", src)
    if not m:
        fail(f"no [BepInPlugin(...)] attribute in {plugin}")
    arg = m.group(3)
    lit = re.fullmatch(r'"([^"]*)"', arg)
    if lit:
        return lit.group(1)
    ident = arg.split(".")[-1]
    c = re.search(r"\bconst\s+string\s+" + re.escape(ident) + r'\s*=\s*"([^"]*)"\s*;', src)
    if not c:
        fail(f"cannot resolve BepInPlugin version argument {arg!r} in {plugin}")
    return c.group(1)


def csproj_info(mod_dir, mod):
    proj = mod_dir / f"{mod}.csproj"
    if not proj.is_file():
        fail(f"{proj} not found")
    text = proj.read_text(encoding="utf-8")
    v = re.search(r"<Version>\s*([^<\s]+)\s*</Version>", text)
    if not v:
        fail(f"no <Version> in {proj}")
    a = re.search(r"<AssemblyName>\s*([^<\s]+)\s*</AssemblyName>", text)
    return proj, v.group(1), (a.group(1) if a else mod)


def changelog_head(mod_dir):
    log = mod_dir / "CHANGELOG.md"
    if not log.is_file():
        fail(f"{log} not found")
    for line in log.read_text(encoding="utf-8").splitlines():
        m = re.match(r"##\s+\[([^\]]+)\]\s*(?:-\s*(.*))?$", line.strip())
        if m:
            return log, m.group(1), (m.group(2) or "").strip()
    fail(f"no '## [version]' heading in {log}")


def build(proj):
    cmd = ["dotnet", "build", str(proj), "-c", "Release", "--no-incremental",
           "-p:Deploy=false", "-nologo", "-v:minimal"]
    print("$ " + " ".join(cmd))
    r = subprocess.run(cmd, cwd=proj.parent)
    if r.returncode != 0:
        fail(f"dotnet build failed (exit {r.returncode})")


def main():
    sys.stdout.reconfigure(line_buffering=True)
    if len(sys.argv) != 2:
        print(__doc__.strip(), file=sys.stderr)
        sys.exit(2)
    mod = sys.argv[1].strip("/")
    if not re.fullmatch(r"[A-Za-z0-9_]+", mod):
        fail(f"bad mod name {mod!r}")
    if mod == "ObenseuerStarter":
        fail("ObenseuerStarter is the internal template and is never published")
    mod_dir = ROOT / "mods" / mod
    if not mod_dir.is_dir():
        fail(f"{mod_dir} not found")

    v_plugin = plugin_version(mod_dir)
    proj, v_proj, asm = csproj_info(mod_dir, mod)
    log, v_log, log_date = changelog_head(mod_dir)
    print(f"[BepInPlugin] version: {v_plugin}")
    print(f"csproj <Version>:      {v_proj}")
    print(f"newest CHANGELOG:      {v_log} - {log_date or '(no date)'}")
    if not (v_plugin == v_proj == v_log):
        fail("versions differ; fix them before packaging")
    if not re.fullmatch(r"\d+\.\d+\.\d+", v_plugin):
        fail(f"version {v_plugin!r} is not MAJOR.MINOR.PATCH")
    if asm != mod:
        fail(f"AssemblyName {asm!r} differs from the mod name {mod!r}")
    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", log_date):
        print(f"WARNING: CHANGELOG heading date is {log_date or 'missing'!r}; "
              "set it to the release date (YYYY-MM-DD) before publishing.")

    started = time.time()
    build(proj)
    dll = mod_dir / "bin" / "Release" / FRAMEWORK / f"{mod}.dll"
    if not dll.is_file():
        fail(f"{dll} not found after the build")
    if dll.stat().st_mtime < started - 2:
        fail(f"{dll} was not rewritten by this build")
    data = dll.read_bytes()
    if v_plugin.encode("utf-16-le") not in data:
        fail(f"version string {v_plugin} not found in {dll.name}")

    allow = [
        (dll, f"BepInEx/plugins/{mod}/{mod}.dll"),
        (log, f"BepInEx/plugins/{mod}/CHANGELOG.md"),
    ]
    (RELEASES / mod).mkdir(parents=True, exist_ok=True)
    out = RELEASES / mod / f"{mod}-{v_plugin}.zip"
    tmp = out.with_suffix(".zip.partial")
    with zipfile.ZipFile(tmp, "w", compression=zipfile.ZIP_DEFLATED) as z:
        for src, arc in allow:
            z.write(src, arc)
    tmp.replace(out)

    with zipfile.ZipFile(out) as z:
        names = z.namelist()
        if sorted(names) != sorted(a for _, a in allow):
            fail(f"archive content differs from the allowlist: {names}")
        print(f"\nWrote {out} ({out.stat().st_size} bytes)")
        print(f"{'size':>9}  path")
        for info in z.infolist():
            print(f"{info.file_size:>9}  {info.filename}")
    print(f"\n{mod}.dll sha256 {hashlib.sha256(data).hexdigest()}")


if __name__ == "__main__":
    main()
