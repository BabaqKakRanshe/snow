"""Pack contract -> <Pack>_Unity.json for PackPropsBuilder (Unity, Tools / Snow / Packs), and the pack's textures into its
Unity folder. Reads source/assets/<pack> (contract, lights; read only) and tools/forest_packs/out/<pack>_export.json and
<pack>_materials.json (export_fbx.py, blend_materials.py). Everything is in the Unity frame of each root (the contract's
*Unity fields). Lit variants of lamps, LEDs and displays stay separate materials; the prefab gets the _Off one. Parts whose
'rest' is not 0 get that pose (Unity: axisUnity, angle -rest, about pivotUnity). Lights are written switched off.

python tools/forest_packs/make_unity_data.py <pack> [<pack> ...]      packs: rocks trash devices parts gear world sky bunker
"""
import json, os, shutil, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, 'source', 'assets')
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out')
UNITY = os.path.join(ROOT, 'Snow')

# pack: (source folder, file stem, Unity folder, data file)
PACKS = {
    'rocks': ('forest_rocks', 'rocks', 'Assets/Locations/Forest/Rocks', 'Rocks_Unity.json'),
    'trash': ('forest_trash', 'trash', 'Assets/Locations/Forest/Trash', 'Trash_Unity.json'),
    'devices': ('forest_devices', 'devices', 'Assets/Locations/Forest/Devices', 'Devices_Unity.json'),
    'parts': ('forest_parts', 'parts', 'Assets/Locations/Forest/Parts', 'Parts_Unity.json'),
    'gear': ('forest_gear', 'gear', 'Assets/Locations/Forest/Gear', 'Gear_Unity.json'),
    'world': ('forest_world', 'world', 'Assets/Locations/Forest/World', 'World_Unity.json'),
    'sky': ('sky_systems', 'sky', 'Assets/SkyIsland/Systems', 'Sky_Unity.json'),
    'bunker': ('forest_bunker', 'bunker', 'Assets/Locations/Bunker', 'Bunker_Unity.json'),
}

# the carried things' names in the game (CargoItem.label), as forest_items' catalogue
LABELS = {
    'Item_Battery': 'Батарейка', 'Item_BatteryPack': 'Упаковка батареек', 'Item_Battery6V': 'Батарейка 6 В',
    'Item_Walkie': 'Рация', 'Item_Lantern': 'Фонарь 6 В', 'Item_Headlamp': 'Налобный фонарь', 'Item_Cart': 'Тележка',
    'Item_Sled': 'Волокуша', 'Item_Doll': 'Кукла', 'Item_Postcard': 'Открытка', 'Item_Bulb': 'Лампочка',
    'Item_BulbBox': 'Коробка лампочки', 'Item_WorkerTag': 'Жетон', 'Item_Key_Stationsleiter': 'Ключ начальника станции',
    'Item_IceAxe': 'Ледоруб', 'Item_DosingPump': 'Насос-дозатор', 'Item_SodiumLamp': 'Натриевая лампа',
    'Item_SodiumLampBox': 'Коробка натриевой лампы', 'Item_RadarCRT': 'Кинескоп радара', 'Item_RelayUnit': 'Блок ретранслятора',
    'Item_BackupGenerator': 'Резервный генератор',
}
# shown later by the game: hidden in the prefab
HIDDEN = {
    'DosingPump_Hose',        # PARTS: hidden until the pump is installed in the pump house
    'FireBarrel_Flames',      # WORLD: the barrel is cold in the file (embers off); flames go with the fire
}
TWO_SIDED = ('Tarp', 'Grating', 'Fire')          # Render Face Both (contracts)
REPEAT = ('Ice', 'Grating')                       # tiled box-mapped textures (UV 1 = 1 m / 0.5 m)


def load(path):
    return json.load(open(path, encoding='utf-8'))


def light_intensity(kind, rng):
    if kind == 'spot':
        return 8.0 if rng >= 12 else 6.0
    return 0.8 if rng <= 3.5 else 3.0 if rng <= 10 else 4.0


def materials(pack, used):
    m = load(os.path.join(OUT, f'{pack}_materials.json'))['materials']
    out, textures = [], {}
    names = set(m)
    for name in sorted(names):
        if name.startswith('M_Kit_'):
            continue
        d = m[name]
        if name not in used and not (name + '_Off' in used or name.endswith('_Off') and name[:-4] in used) and not d['fake_user']:
            continue
        base, emap = d.get('base_image'), d.get('emission_image')
        alpha_img = d.get('alpha_image')
        transparent = d.get('blend_method') == 'BLEND'
        mode = 'transparent' if transparent else 'cutout' if alpha_img else 'opaque'
        strength = float(d.get('emission_strength') or 0.0)
        ec = (d.get('emission_color') or [1, 1, 1, 1])[:3]
        entry = {'name': name, 'base': base, 'color': (d.get('base_color') or [1, 1, 1, 1])[:3] if not base else [1.0, 1.0, 1.0],
                 'alpha': float(d.get('alpha') if d.get('alpha') is not None else 1.0), 'mode': mode,
                 'twoSided': transparent or mode == 'cutout' or any(k in name for k in TWO_SIDED),
                 'emission': strength, 'emissionColor': ec if strength > 0 and not emap else [1.0, 1.0, 1.0],
                 'emissionMap': emap if strength > 0 else None}
        out.append(entry)
        for t in (base, emap if strength > 0 else None):
            if t:
                textures[t] = textures.get(t, False) or any(k in name for k in REPEAT)
    # the prefab gets the switched-off variant of every lamp, LED and display
    remap = {}
    for e in out:
        n = e['name']
        if n + '_Off' in names:
            remap[n] = n + '_Off'
    return out, remap, [{'name': t, 'repeat': r} for t, r in sorted(textures.items())]


def generic_items(pack, c, rep, lights):
    items = []
    parts = c.get('parts', {})
    for root, r in rep.items():
        it = {'name': root, 'kind': 'item' if root.startswith('Item_') else 'static', 'tris': r['tris'],
              'colliders': [], 'meshColliders': [], 'sockets': [], 'parents': [], 'lights': [], 'hidden': [], 'kit': [], 'poses': []}
        if it['kind'] == 'item':
            it['mass'] = float(parts.get(root, {}).get('mass_kg', 1.0))
            it['label'] = LABELS.get(root, root)
        for child, parent in r['parents'].items():
            if parent != root:
                it['parents'].append({'child': child, 'parent': parent})
        for col in c.get('colliders', []):
            if col['root'] != root:
                continue
            if col['type'] == 'mesh':
                it['meshColliders'].append({'object': col['name'], 'convex': False})
                continue
            # centerUnity is in the parent part's frame; center_root (Blender, root frame) gives the root-frame centre
            cr = col.get('center_root')
            centre = [-cr[0], cr[2], -cr[1]] if cr else col['centerUnity']
            it['colliders'].append({'name': col['name'], 'parent': col['parent'], 'center': centre, 'size': col['sizeUnity'],
                                    'euler': col.get('rotationUnity') or [0.0, 0.0, 0.0], 'trigger': col['type'] == 'hit'})
        filed = {l['name'] for l in lights}
        for sn, s in c.get('sockets', {}).items():
            if s['root'] != root:
                continue
            if s['kind'] == 'light' and sn in filed:
                continue                                         # the lights file has it
            if s['kind'] == 'light':
                rng = float(s.get('range', 10.0))
                it['lights'].append({'name': sn, 'parent': s['parent'], 'spot': True, 'angle': float(s.get('spot_angle', 60.0)),
                                     'pos': s['posUnity'], 'dir': s.get('dirUnity') or [0, 0, 1], 'color': s.get('color', [1, 1, 1]),
                                     'intensity': light_intensity('spot', rng), 'range': rng})
                continue
            if s['kind'] == 'kit':
                continue
            e = s.get('eulerUnity') or ([0.0, float(s['yawUnity']), 0.0] if s.get('yawUnity') is not None else None)
            it['sockets'].append({'name': sn, 'parent': s['parent'], 'kind': s['kind'], 'pos': s['posUnity'],
                                  'dir': s.get('dirUnity') or [0.0, 0.0, 0.0], 'euler': e or [0.0, 0.0, 0.0], 'hasEuler': e is not None})
        for k in c.get('kit_instances', {}).values():
            if k['root'] == root:
                it['kit'].append({'prefab': f"Assets/Kit/Prefabs/{k['item']}.prefab", 'parent': k['parent'], 'pos': k['posUnity'],
                                  'rot': k['rotUnity_xyzw']})
        for pn, p in parts.items():
            obj = p.get('object', pn)
            if p.get('root') != root or not p.get('rest') or p.get('kind') not in ('hinge', 'twist'):
                continue
            pose = r['poses'].get(obj, {}).get('rot_euler', [0, 0, 0])
            if any(abs(v) > 1e-4 for v in pose):
                continue                                         # modelled at its rest pose already
            it['poses'].append({'object': obj, 'pivot': p['pivotUnity'], 'axis': p['axisUnity'], 'angle': -float(p['rest'])})
        for l in lights:
            if l['root'] != root:
                continue
            spot = l['type'] == 'spot'
            rng = float(l.get('range', 8.0))
            it['lights'].append({'name': l['name'], 'parent': l['parent'], 'spot': spot, 'angle': float(l.get('spot_angle', 0.0)),
                                 'pos': l['posUnity'], 'dir': l.get('dirUnity') or [0.0, -1.0, 0.0], 'color': l['color_linear'],
                                 'intensity': light_intensity('spot' if spot else 'point', rng), 'range': rng})
        it['hidden'] = [o for o in r['objects'] if o in HIDDEN]
        items.append(it)
    return items


def rocks_items(c, rep):
    items = []
    for root, r in rep.items():
        md = c['models'][root]
        items.append({'name': root, 'kind': 'static', 'tris': r['tris'], 'colliders': [], 'sockets': [], 'parents': [], 'lights': [],
                      'hidden': [], 'kit': [], 'poses': [],
                      'meshColliders': [{'object': root, 'convex': bool(md['collider'].get('convex'))}]})
    return items


def trash_items(c, rep):
    items = []
    for kind in c['kinds'].values():
        for state in ('pile', 'clump'):
            s = kind[state]
            root = s['model']
            col = s['collider']
            it = {'name': root, 'kind': 'static', 'tris': rep[root]['tris'], 'meshColliders': [], 'parents': [], 'lights': [],
                  'hidden': [], 'kit': [], 'poses': [],
                  'colliders': [{'name': 'COL_' + root, 'parent': root, 'center': col['center'], 'size': col['size'], 'euler': [0.0, 0.0, 0.0], 'trigger': False}],
                  'sockets': [{'name': n, 'parent': root, 'kind': 'hand', 'pos': p, 'dir': [0.0, 0.0, 0.0], 'euler': [0.0, 0.0, 0.0], 'hasEuler': False}
                              for n, p in s.get('sockets', {}).items()],
                  'massKg': float(kind.get('massKg', 0.0))}
            items.append(it)
    return items


def make(pack):
    src, stem, dest, data = PACKS[pack]
    pkg = os.path.join(SRC, src)
    c = load(os.path.join(pkg, f'{stem}_contract.json'))
    rep = load(os.path.join(OUT, f'{pack}_export.json'))
    lp = os.path.join(pkg, f'{stem}_lights.json')
    lights = load(lp)['lights'] if os.path.exists(lp) else []
    if pack == 'rocks':
        items = rocks_items(c, rep)
    elif pack == 'trash':
        items = trash_items(c, rep)
    else:
        items = generic_items(pack, c, rep, lights)
    for it in items:
        it['fbx'] = f"{dest}/Models/{it['name']}.fbx"
    used = {m for r in rep.values() for m in r['materials']}
    mats, remap, textures = materials(pack, used)
    tex_dir = os.path.join(UNITY, dest, 'Textures')
    os.makedirs(tex_dir, exist_ok=True)
    for t in textures:
        s, d = os.path.join(pkg, 'tex', t['name']), os.path.join(tex_dir, t['name'])
        if not os.path.exists(d) or open(s, 'rb').read() != open(d, 'rb').read():
            shutil.copyfile(s, d)
    attach = []
    # devices / parts: placements in forest_far prefabs; world: the ice in its hosts (switched off)
    for name, places in c.get('placements', {}).items():
        for pl in places:
            attach.append({'prefab': name, 'host': pl['parent'], 'pos': pl['posUnity'], 'rot': pl['rotUnity_xyzw'], 'active': True,
                           'spawn': name.startswith('Item_')})
    for root_name, ice in c.get('ice', {}).items():
        attach.append({'prefab': root_name, 'host': ice['host'], 'pos': [0.0, 0.0, 0.0], 'rot': [0.0, 0.0, 0.0, 1.0], 'active': False, 'spawn': False})
    out = {'version': c['meta']['version'], 'pack': src, 'root': dest,
           'note': f'generated by tools/forest_packs/make_unity_data.py from source/assets/{src}; read by PackPropsBuilder (Tools / Snow / Packs)',
           'materials': mats, 'remap': [{'from': k, 'to': v} for k, v in sorted(remap.items())], 'textures': textures, 'items': items, 'attach': attach}
    path = os.path.join(UNITY, dest, data)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    for a in attach:
        print(f"   attach {a['prefab']} -> {a['host']} at {a['pos']} active={a['active']} spawn={a['spawn']}")
    print(f'{pack}: {len(items)} prefabs, {len(mats)} materials ({len(remap)} lit -> _Off), {len(textures)} textures -> {path}')
    for it in items:
        extra = []
        for k in ('colliders', 'meshColliders', 'sockets', 'lights', 'parents', 'kit', 'poses', 'hidden'):
            if it.get(k):
                extra.append(f'{k} {len(it[k])}')
        print(f"   {it['name']} [{it['kind']}] tris {it['tris']}: " + ', '.join(extra))


if __name__ == '__main__':
    for p in sys.argv[1:] or PACKS:
        make(p)
