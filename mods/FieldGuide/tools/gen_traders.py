#!/usr/bin/env python3
"""Build Field Guide's trader data (Data/traders.json) from the game's exported scenes.

Usage (from mods/FieldGuide):
  python3 -I tools/gen_traders.py --assets <AssetRipper/ExportedProject/Assets> --out Data/traders.json \
      [--game <Steam/.../common/Obenseuer>] [--check]

--check rebuilds in memory and exits 1 if the file on disk differs.

Every input comes from the AssetRipper export: the scenes, the characters, ProjectSettings' bundleVersion and the
exported StreamingAssets/Items.json. The Steam build id can't be read from the export, so trader_overrides.json
"export" declares which build the export was made from (game version and Steam buildid); the generator stops when
it doesn't match the export's bundleVersion, and, with --game, when the installed build (appmanifest_951240.acf)
differs from it. Re-export, then update "export", after every game update.

Reads scenes only (ASSETS/Scenes/*.unity), never prefabs: 11 character prefabs carry blank "buy anything" Trades
that the scenes override. Standard library only. The output is deterministic: sorted by scene, path and GUID, fixed
key order, no timestamps. See .internal/design.md §8 for the schema and the checks.
"""
import argparse
import glob
import hashlib
import json
import os
import re
import sys

HDR = re.compile(r'^--- !u!(\d+) &(-?\d+)( stripped)?[^\n]*$', re.M)
SCHEMA = 2


class GenError(Exception):
    pass


# ---- YAML helpers (just enough for Unity's text serialization) ----

def unquote(v):
    v = v.strip()
    if len(v) >= 2 and v[0] == "'" and v[-1] == "'":
        return v[1:-1].replace("''", "'")
    if len(v) >= 2 and v[0] == '"' and v[-1] == '"':
        return json.loads(v)
    return v


def field(body, name, indent=2):
    m = re.search(r'^' + ' ' * indent + re.escape(name) + r':(?: (.*))?$', body, re.M)
    if not m:
        return None
    return unquote(m.group(1) or '')


def str_list(body, name):
    m = re.search(r'^  ' + re.escape(name) + r':(.*)$', body, re.M)
    if not m:
        raise GenError('list field %s missing' % name)
    rest = m.group(1).strip()
    if rest == '[]':
        return []
    if rest:
        raise GenError('unexpected inline list for %s: %r' % (name, rest))
    out = []
    for line in body[m.end() + 1:].split('\n'):
        mm = re.match(r'^  - (.*)$', line)
        if not mm:
            break
        out.append(unquote(mm.group(1)))
    return out


def sub_block(body, name):
    """Returns {key: value} of a two-space nested block such as owner: / certificateItem:."""
    m = re.search(r'^  ' + re.escape(name) + r':\s*\n((?:    .*\n?)*)', body, re.M)
    if not m:
        return None
    out = {}
    for line in m.group(1).split('\n'):
        mm = re.match(r'^    (\w+):(?: (.*))?$', line)
        if mm:
            out[mm.group(1)] = unquote(mm.group(2) or '')
    return out


def num(text, what):
    try:
        return float(text)
    except (TypeError, ValueError):
        raise GenError('%s is not a number: %r' % (what, text))


def docs(text):
    ms = list(HDR.finditer(text))
    for i, m in enumerate(ms):
        end = ms[i + 1].start() if i + 1 < len(ms) else len(text)
        yield int(m.group(1)), m.group(2), bool(m.group(3)), text[m.end():end]


def script_guid(assets, name):
    path = os.path.join(assets, 'Scripts', 'Assembly-CSharp', name + '.cs.meta')
    with open(path, encoding='utf-8') as f:
        m = re.search(r'^guid: (\w+)', f.read(), re.M)
    if not m:
        raise GenError('no guid in ' + path)
    return m.group(1)


# ---- Inputs ----

def load_items(assets):
    path = os.path.join(assets, 'StreamingAssets', 'Items.json')
    with open(path, encoding='utf-8-sig') as f:
        items = json.load(f)
    cats, titles, metas = set(), {}, set()
    for it in items:
        for c in it.get('Categories') or []:
            cats.add(c)
        t = (it.get('Title') or '').lower()
        titles.setdefault(t, []).append(it['ID'])
        for m in it.get('Meta') or []:
            ty = m.get('$type') if isinstance(m, dict) else None
            if ty:
                metas.add(ty.split('.')[-1].lower())
    return cats, titles, metas


def load_meta_classes(assets):
    names = set()
    for p in glob.glob(os.path.join(assets, 'Scripts', 'Assembly-CSharp', 'OS', 'Items', '*.cs')):
        names.add(os.path.basename(p)[:-3].lower())
    return names


def load_characters(assets):
    chars = {}
    for p in sorted(glob.glob(os.path.join(assets, 'Resources', 'Characters', '*.asset'))):
        with open(p, encoding='utf-8') as f:
            body = f.read()
        cid = field(body, 'ID')
        if cid is None:
            continue
        name = ' '.join(x for x in (field(body, 'Name') or '', field(body, 'LastName') or '') if x).strip()
        chars[int(cid)] = name
    return chars


def game_version(assets):
    path = os.path.join(assets, '..', 'ProjectSettings', 'ProjectSettings.asset')
    with open(path, encoding='utf-8') as f:
        m = re.search(r'^  bundleVersion: (.+)$', f.read(), re.M)
    if not m:
        raise GenError('bundleVersion not found in ' + path)
    return m.group(1).strip()


def install_build_id(game):
    path = os.path.join(game, '..', '..', 'appmanifest_951240.acf')
    with open(path, encoding='utf-8') as f:
        m = re.search(r'"buildid"\s+"(\d+)"', f.read())
    if not m:
        raise GenError('buildid not found in ' + path)
    return m.group(1)


# ---- Scene scan ----

def scan_scene(path, guids):
    """Returns the scene's Trade records (raw), or [] when it has none."""
    with open(path, encoding='utf-8', errors='strict') as f:
        text = f.read()
    if guids['Trade'] not in text and guids['TradeSpecial'] not in text:
        return []
    gos, trs, trades, storages = {}, {}, [], {}
    starts = set()
    for cls, fid, stripped, body in docs(text):
        if cls == 1001 or stripped:
            raise GenError('%s: prefab instance or stripped object in a scene with a Trade; the scene scan cannot '
                           'resolve overrides' % os.path.basename(path))
        if cls == 1:
            gos[fid] = {'name': field(body, 'm_Name') or '', 'active': field(body, 'm_IsActive') == '1'}
        elif cls in (4, 224):
            go = re.search(r'm_GameObject: \{fileID: (-?\d+)\}', body).group(1)
            fa = re.search(r'm_Father: \{fileID: (-?\d+)\}', body)
            trs[go] = fa.group(1) if fa else '0'
            trs['t' + fid] = go
        elif cls == 114:
            g = re.search(r'm_Script: \{fileID: \d+, guid: (\w+)', body)
            go = re.search(r'm_GameObject: \{fileID: (-?\d+)\}', body)
            sg = g.group(1) if g else None
            if sg in (guids['Trade'], guids['TradeSpecial']):
                trades.append((fid, go.group(1), sg == guids['TradeSpecial'], body))
            elif sg == guids['Storage']:
                storages[go.group(1)] = body
        for m in re.finditer(r'm_Target: \{fileID: (-?\d+)\}\n\s+m_TargetAssemblyTypeName:[^\n]*\n\s+m_MethodName: StartTrade\b',
                             body):
            starts.add(m.group(1))

    def parent_go(go):
        fa = trs.get(go, '0')
        return trs.get('t' + fa)

    def path_of(go):
        parts, g, seen = [], go, set()
        while g is not None and g in gos and g not in seen:
            seen.add(g)
            parts.append(gos[g]['name'])
            g = parent_go(g)
        return '/'.join(reversed(parts))

    def active(go):
        g, seen = go, set()
        while g is not None and g in gos and g not in seen:
            seen.add(g)
            if not gos[g]['active']:
                return False
            g = parent_go(g)
        return True

    out = []
    for fid, go, special, body in trades:
        st = storages.get(go)
        # Trade.storage is GetComponent<Storage>() (SRC/Trade.cs:227); CheckIfFull tests its slots (design.md §9.3).
        out.append({'fid': fid, 'go': go, 'special': special, 'body': body, 'path': path_of(go),
                    'active': active(go), 'relay': fid in starts, 'hasStorage': st is not None,
                    'storageTitle': field(st, 'title') if st else '',
                    'storageAllowed': str_list(st, 'AllowedCategories') if st else [],
                    'storageForbidden': str_list(st, 'ForbiddenCategories') if st else []})
    return out


def rules_hash(r):
    keys = ('allowed', 'forbidden', 'sell', 'sellCurrency', 'buysStolen', 'stolenFactor', 'ownerId', 'kind', 'certificate',
            'destroyPurchased', 'storageAllowed', 'storageForbidden')
    canon = json.dumps([r[k] for k in keys], ensure_ascii=False, sort_keys=True, separators=(',', ':'))
    return hashlib.sha1(canon.encode('utf-8')).hexdigest()[:10]


def build(assets, game, overrides_path, report):
    with open(overrides_path, encoding='utf-8') as f:
        ov = json.load(f)
    if ov.get('schema') != 1:
        raise GenError('trader_overrides.json: schema must be 1')
    export = ov.get('export') or {}
    version = game_version(assets)
    if export.get('game') != version or not re.match(r'^\d+$', str(export.get('buildid') or '')):
        raise GenError('trader_overrides.json export: game %r / buildid %r, but the export is %s; update "export" to the '
                       'build the export was made from' % (export.get('game'), export.get('buildid'), version))
    if game:
        installed = install_build_id(game)
        if installed != export['buildid']:
            raise GenError('the installed game is build %s, the export was made from build %s; re-export the game (or '
                           'run without --game to rebuild from the export as it is)' % (installed, export['buildid']))
    else:
        report.append('note: no --game, so the installed build was not compared with the export\'s')
    guids = {n: script_guid(assets, n) for n in ('Trade', 'TradeSpecial', 'Storage')}
    cats, titles, item_metas = load_items(assets)
    meta_classes = load_meta_classes(assets) | item_metas
    chars = load_characters(assets)
    areas = ov.get('areas') or []
    names = ov.get('names') or {}
    excl = ov.get('exclude') or {}
    excl_scenes = ov.get('excludeScenes') or {}
    ack = ov.get('acknowledgedDeadTokens') or {}
    used = set()

    records = []
    scene_files = sorted(glob.glob(os.path.join(assets, 'Scenes', '*.unity')))
    if not scene_files:
        raise GenError('no scenes under ' + os.path.join(assets, 'Scenes'))
    for sp in scene_files:
        scene = os.path.basename(sp)[:-len('.unity')]
        for raw in scan_scene(sp, guids):
            b = raw['body']
            guid = field(b, 'GUID') or ''
            key = scene + '|' + guid
            owner = sub_block(b, 'owner') or {}
            owner_id = int(owner.get('ID') or 0)
            title = field(b, 'title') or ''
            st_title = raw['storageTitle'] or ''
            mode = field(b, 'sellPriceMode')
            if mode not in ('0', '1'):
                raise GenError('%s: sellPriceMode %r' % (key, mode))
            cert = None
            if raw['special'] and field(b, 'requireCertificates') == '1':
                if field(b, 'acceptGenericCertificates') == '1':
                    cert = {'generic': True}
                else:
                    ci = sub_block(b, 'certificateItem') or {}
                    cert = {'generic': False, 'itemId': int(ci.get('ID') or 0)}
            allowed = str_list(b, 'allowedCategories')
            forbidden = str_list(b, 'forbiddenCategories')
            direct = field(b, 'isInteractable') == '1' and field(b, 'interactDisabled') != '1'
            rec = {
                'key': key, 'scene': scene, 'area': next((a for p, a in areas if scene.startswith(p)), ''),
                'path': raw['path'], 'guid': guid, 'sharedId': field(b, 'sharedId') or '',
                'kind': 'TradeSpecial' if raw['special'] else 'Trade',
                'name': '', 'nameSource': '', 'title': title, 'storageTitle': st_title,
                'ownerId': owner_id, 'ownerName': chars.get(owner_id, '') if owner_id > 0 else '',
                'allowed': allowed, 'forbidden': forbidden,
                'sell': num(field(b, 'baseSellPrice'), key + ' baseSellPrice'),
                'sellCurrency': 'RM' if mode == '0' else 'OC',
                'buysStolen': field(b, 'buysStolen') == '1',
                'stolenFactor': num(field(b, 'stolenPriceFactor'), key + ' stolenPriceFactor'),
                'certificate': cert,
                'destroyPurchased': field(b, 'destroyPurchasedItems') == '1',
                'storageAllowed': raw['storageAllowed'], 'storageForbidden': raw['storageForbidden'],
                'reach': {'direct': direct, 'relay': raw['relay']},
                'deadTokens': [], 'group': '', 'exclude': None,
            }
            if field(b, 'm_Enabled', 2) != '1':
                report.append('note: %s has its Trade component disabled in the scene' % key)

            # Exclusions (D16): scene list, hand list, then the scan's own rules.
            if scene in excl_scenes:
                rec['exclude'] = 'scene: ' + excl_scenes[scene]
            elif key in excl:
                rec['exclude'] = excl[key]
                used.add('exclude|' + key)
            elif not raw['active']:
                rec['exclude'] = 'inactive at scene load'
            elif not direct and not raw['relay']:
                rec['exclude'] = 'unreachable'

            # Names (D15): Trade.title, then Storage.title, then a curated override.
            if title:
                rec['name'], rec['nameSource'] = title, 'title'
            elif st_title:
                rec['name'], rec['nameSource'] = st_title, 'storage'
            if key in names:
                used.add('name|' + key)
                if rec['name']:
                    raise GenError('%s: name override not needed, the game names it %r' % (key, rec['name']))
                rec['name'], rec['nameSource'] = names[key]['name'], 'override'
            if not rec['name'] and rec['exclude'] is None:
                raise GenError('%s (%s): no name; add one to trader_overrides.json names' % (key, raw['path']))
            if not raw['hasStorage'] and rec['exclude'] is None:
                raise GenError('%s (%s): no Storage next to the Trade, so the game could never sell to it' % (key, raw['path']))

            # Token validation against Items.json.
            for tok in allowed + forbidden + raw['storageAllowed'] + raw['storageForbidden']:
                low = tok.lower()
                if low.startswith('_item_'):
                    ids = titles.get(low[6:])
                    if not ids:
                        rec['deadTokens'].append(tok)
                    elif len(ids) > 1:
                        report.append('note: %s token %r matches %d items (%s)' % (key, tok, len(ids), ', '.join(map(str, ids))))
                elif low.startswith('_meta_'):
                    if low[6:] not in meta_classes:
                        rec['deadTokens'].append(tok)
                elif tok not in cats:
                    rec['deadTokens'].append(tok)
            if rec['deadTokens']:
                known = ack.get(key) or []
                used.add('ack|' + key)
                new = [t for t in rec['deadTokens'] if t not in known]
                if new and rec['exclude'] is None:
                    raise GenError('%s: unacknowledged dead token(s) %s; check them and list them under '
                                   'acknowledgedDeadTokens' % (key, new))
            records.append(rec)

    keys = [r['key'] for r in records]
    dup = sorted(set(k for k in keys if keys.count(k) > 1))
    if dup:
        raise GenError('duplicate trader keys: %s' % dup)
    for k in names:
        if 'name|' + k not in used:
            raise GenError('trader_overrides.json names: %s matches no scene Trade' % k)
    for k in excl:
        if 'exclude|' + k not in used:
            raise GenError('trader_overrides.json exclude: %s matches no scene Trade' % k)
    for k in ack:
        if 'ack|' + k not in used:
            raise GenError('trader_overrides.json acknowledgedDeadTokens: %s has no dead token' % k)
    scenes = set(r['scene'] for r in records)
    for s in excl_scenes:
        if s not in scenes:
            raise GenError('trader_overrides.json excludeScenes: %s has no Trade' % s)

    for r in records:
        r['group'] = r['name'] + '|' + rules_hash(r)
    records.sort(key=lambda r: (r['scene'], r['path'], r['guid']))

    # Report (stderr) for the review in design.md §8.2 / O4.
    ex = [r for r in records if r['exclude']]
    live = [r for r in records if not r['exclude']]
    report.append('scene records: %d (%d excluded, %d kept, %d groups)' %
                  (len(records), len(ex), len(live), len(set(r['group'] for r in live))))
    for r in ex:
        report.append('excluded: %s (%s): %s' % (r['key'], r['name'] or r['path'], r['exclude']))
    for r in live:
        if not r['reach']['direct']:
            report.append('relay-only: %s (%s) at %s' % (r['name'], r['scene'], r['path']))
    for r in records:
        if r['deadTokens']:
            report.append('dead tokens: %s (%s): %s' % (r['key'], r['name'], r['deadTokens']))

    order = ('key', 'scene', 'area', 'path', 'guid', 'sharedId', 'kind', 'name', 'nameSource', 'title', 'storageTitle',
             'ownerId', 'ownerName', 'allowed', 'forbidden', 'sell', 'sellCurrency', 'buysStolen', 'stolenFactor',
             'certificate', 'destroyPurchased', 'storageAllowed', 'storageForbidden', 'reach', 'deadTokens', 'group', 'exclude')
    doc = {
        'schema': SCHEMA, 'game': version, 'buildid': export['buildid'], 'generator': 'tools/gen_traders.py',
        'traders': [{k: r[k] for k in order} for r in records],
    }
    return json.dumps(doc, ensure_ascii=False, indent=1) + '\n'


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--assets', required=True, help='AssetRipper/ExportedProject/Assets')
    ap.add_argument('--game', help='the Obenseuer install folder: checks that it is the build the export came from')
    ap.add_argument('--out', required=True, help='output file (Data/traders.json)')
    ap.add_argument('--overrides', default=os.path.join(here, 'trader_overrides.json'))
    ap.add_argument('--check', action='store_true', help='exit 1 if --out differs from a fresh build')
    a = ap.parse_args()
    report = []
    try:
        text = build(a.assets, a.game, a.overrides, report)
    except (GenError, OSError, ValueError) as e:
        print('gen_traders: error: %s' % e, file=sys.stderr)
        return 2
    for line in report:
        print(line, file=sys.stderr)
    if a.check:
        try:
            with open(a.out, encoding='utf-8', newline='') as f:
                old = f.read()
        except OSError:
            old = None
        if old != text:
            print('gen_traders: %s is out of date; run without --check' % a.out, file=sys.stderr)
            return 1
        print('gen_traders: %s is up to date' % a.out, file=sys.stderr)
        return 0
    with open(a.out, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    print('gen_traders: wrote %s' % a.out, file=sys.stderr)
    return 0


if __name__ == '__main__':
    sys.exit(main())
