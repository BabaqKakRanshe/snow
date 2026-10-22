"""Snow QA: location update packages (.unitypackage made by the asset pipeline).

    python pkg.py pre  <manifest.json>                 before the import: base_md5 of overwritten files, GUIDs of existing paths
    python pkg.py install <package> <manifest.json>    extract into the project (GUIDs checked first), refresh, fix importers
    python pkg.py post <manifest.json>                 after: md5 + GUID of every file, no "X 1.fbx" copies

Why extraction: AssetDatabase.ImportPackage and -importPackage silently do nothing with these packages (BASEV2, Forest K,
U6, U7). Existing .meta with the same GUID are kept (importer settings stay), new paths get the package .meta (GUID only ->
legacy defaults: textures Cube, models External materials -> fixed to 2D / InPrefab here). A GUID mismatch aborts before
anything is written. Exit code 0 = PASS.
"""
import hashlib, json, os, re, sys, tarfile
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ue

PROJ = ue.PROJ


def md5(p):
    h = hashlib.md5()
    with open(p, 'rb') as f:
        for b in iter(lambda: f.read(1 << 20), b''):
            h.update(b)
    return h.hexdigest()


def meta_guid(p):
    mp = p + '.meta'
    if not os.path.exists(mp):
        return None
    m = re.search(r'^guid:\s*([0-9a-f]{32})', open(mp, encoding='utf-8', errors='replace').read(), re.M)
    return m.group(1) if m else '?'


def pre(man):
    bad, new = 0, []
    for path, want in man['base_md5'].items():
        full = os.path.join(PROJ, path)
        got = md5(full) if os.path.exists(full) else 'MISSING'
        ok = got == want
        bad += not ok
        print(f"base_md5 {'OK ' if ok else 'BAD'} {path} {got}" + ('' if ok else f' (want {want})'))
    existing = 0
    for f in man['files']:
        full = os.path.join(PROJ, f['path'])
        if os.path.exists(full):
            existing += 1
            if meta_guid(full) != f['guid']:
                bad += 1
                print(f"GUID BAD {f['path']} project {meta_guid(full)} package {f['guid']}")
        else:
            new.append(f['path'])
    print(f'existing paths, GUID checked: {existing}; new paths: {len(new)}')
    for n in new:
        print('  new', n)
    return bad


def post(man):
    bad, n = 0, 0
    for f in man['files']:
        full = os.path.join(PROJ, f['path'])
        ok = os.path.isdir(full) if f['md5'] is None else (os.path.exists(full) and md5(full) == f['md5'])
        n += f['md5'] is not None
        if not ok or meta_guid(full) != f['guid']:
            bad += 1
            print('BAD', f['path'])
    for d in sorted({os.path.dirname(f['path']) for f in man['files'] if f['md5'] is not None}):
        for name in os.listdir(os.path.join(PROJ, d)):
            if re.search(r' \d+\.(fbx|png|jpg|json|mat)$', name):
                bad += 1
                print('COPY', f'{d}/{name}')
    print(f'files with md5: {n}, entries {len(man["files"])}')
    return bad


FIX = '''string root = "ROOT"; int t2 = 0, m2 = 0;
foreach (var p in new[] { PATHS }) {
  if (UnityEditor.AssetImporter.GetAtPath(p) is UnityEditor.TextureImporter ti && ti.textureShape != UnityEditor.TextureImporterShape.Texture2D) { ti.textureShape = UnityEditor.TextureImporterShape.Texture2D; ti.SaveAndReimport(); t2++; }
  if (UnityEditor.AssetImporter.GetAtPath(p) is UnityEditor.ModelImporter mi && mi.materialLocation != UnityEditor.ModelImporterMaterialLocation.InPrefab) { mi.materialLocation = UnityEditor.ModelImporterMaterialLocation.InPrefab; mi.SaveAndReimport(); m2++; } }
return "new files: textures -> 2D " + t2 + ", models -> InPrefab " + m2;'''


def install(package, man):
    t = tarfile.open(package, 'r:gz')
    ents = {}
    for m in t.getmembers():
        if m.isfile():
            g, _, leaf = m.name.strip('./').partition('/')
            ents.setdefault(g, {})[leaf] = m
    plan = []
    for g, e in ents.items():
        path = t.extractfile(e['pathname']).read().decode('utf-8').splitlines()[0].strip()
        if not path.startswith('Assets/') or '..' in path:
            sys.exit(f'bad path {path}')
        full = os.path.join(PROJ, path)
        keep = os.path.exists(full + '.meta')
        if keep and meta_guid(full) != g:
            sys.exit(f'GUID MISMATCH {path}: project {meta_guid(full)} package {g} - nothing written')
        plan.append((path, full, e, keep))
    new = []
    for path, full, e, keep in plan:
        if 'asset' in e:
            os.makedirs(os.path.dirname(full), exist_ok=True)
            open(full, 'wb').write(t.extractfile(e['asset']).read())
        else:
            os.makedirs(full, exist_ok=True)
        if not keep and 'asset.meta' in e:
            open(full + '.meta', 'wb').write(t.extractfile(e['asset.meta']).read())
            if 'asset' in e:
                new.append(path)
    print(f'{os.path.basename(package)}: {len(plan)} entries written, {len(new)} new paths')
    ue.run('UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport); return "refreshed";', 900)
    if new:
        print(ue.run(FIX.replace('PATHS', ', '.join(f'"{p}"' for p in new)), 900))


if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'install':
        install(sys.argv[2], json.load(open(sys.argv[3], encoding='utf-8')))
        sys.exit(post(json.load(open(sys.argv[3], encoding='utf-8'))) > 0)
    man = json.load(open(sys.argv[2], encoding='utf-8'))
    bad = pre(man) if cmd == 'pre' else post(man)
    print('RESULT', 'PASS' if bad == 0 else f'FAIL ({bad})')
    sys.exit(bad > 0)
