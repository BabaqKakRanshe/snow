"""Snow QA: physics and state checks in Play (host unless proj is given).

    python checks.py reach <config.json> van|walker [out.png]   flood fill over real colliders from a start point
    python checks.py line x0 z0 x1 z1 [ext]                     van box swept along a line (points with no hit = gaps)
    python checks.py touch <structure> [seconds]                penetration of a structure into the Rocks group while it moves
    python checks.py probe [player] --parts A B --items X Y      state of parts / items on the host or a client (late join); items by object
                                                           name prefix: ids on the host (Leitstand_RadioSet_1), models on clients (Item_RadioSet)

reach config: {"region": [x0, z0, x1, z1], "step": 0.5, "start": [x, z], "goals": {"name": [x, z]}}.
Bodies: van = 0.35..2.0 m above ground in any heading (rectangles inscribed in a 1.94 m disc: a 1.37 m square and 1.80 x 0.73 m
strips at 0/45/90/135 deg; a sphere slips between tank-trap pyramids - do not use one); walker = capsule r 0.35, 0.35..1.8 m.
Ignored: TerrainCollider (ground, Terrain trees), triggers, layer Interact, players, loose items.
"""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ue

IGN = ('if (c is UnityEngine.TerrainCollider || c.GetComponentInParent<Snow.Player.PlayerNet>() != null || '
       'c.GetComponentInParent<Snow.Cargo.CargoItem>() != null) continue;')
COMMON = ('var ci = System.Globalization.CultureInfo.InvariantCulture; var sb = new System.Text.StringBuilder(); var hits = new UnityEngine.Collider[32];\n'
          'int mask = ~(1 << UnityEngine.LayerMask.NameToLayer("Interact")); var terrains = UnityEngine.Terrain.activeTerrains;\n'
          'System.Func<float, float, float> th = (x, z) => { foreach (var te in terrains) { var o = te.GetPosition(); var s = te.terrainData.size; '
          'if (x >= o.x && x < o.x + s.x && z >= o.z && z < o.z + s.z) return te.SampleHeight(new UnityEngine.Vector3(x, 0, z)) + o.y; } return 0f; };\n'
          'var vb = new (UnityEngine.Vector3 half, float yaw)[] { (new UnityEngine.Vector3(0.685f, 0.825f, 0.685f), 0f), (new UnityEngine.Vector3(0.9f, 0.825f, 0.365f), 0f), '
          '(new UnityEngine.Vector3(0.9f, 0.825f, 0.365f), 45f), (new UnityEngine.Vector3(0.9f, 0.825f, 0.365f), 90f), (new UnityEngine.Vector3(0.9f, 0.825f, 0.365f), 135f) };\n'
          'System.Func<float, float, bool> Van = (x, z) => { float g = th(x, z); foreach (var b in vb) { int k = UnityEngine.Physics.OverlapBoxNonAlloc(new UnityEngine.Vector3(x, g + 1.175f, z), b.half, hits, '
          'UnityEngine.Quaternion.Euler(0, b.yaw, 0), mask, UnityEngine.QueryTriggerInteraction.Ignore); for (int h = 0; h < k; h++) { var c = hits[h]; ' + IGN + ' return false; } } return true; };\n'
          'System.Func<float, float, bool> Walker = (x, z) => { float g = th(x, z); int k = UnityEngine.Physics.OverlapCapsuleNonAlloc(new UnityEngine.Vector3(x, g + 0.7f, z), '
          'new UnityEngine.Vector3(x, g + 1.45f, z), 0.35f, hits, mask, UnityEngine.QueryTriggerInteraction.Ignore); for (int h = 0; h < k; h++) { var c = hits[h]; ' + IGN + ' return false; } return true; };\n')


def reach(cfg, body, png=None):
    x0, z0, x1, z1 = cfg['region']
    step = cfg.get('step', 0.5)
    goals = ''.join(f'G("{k}", {v[0]}f, {v[1]}f);\n' for k, v in cfg['goals'].items())
    png_code = ''
    if png:
        png_code = (f'var tex = new UnityEngine.Texture2D(nx, nz, UnityEngine.TextureFormat.RGB24, false); for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) '
                    'tex.SetPixel(i, j, reach[i, j] ? new UnityEngine.Color(0.3f, 0.75f, 0.3f) : free[i, j] ? new UnityEngine.Color(0.85f, 0.85f, 0.85f) : new UnityEngine.Color(0.15f, 0.15f, 0.2f)); '
                    f'tex.Apply(); System.IO.File.WriteAllBytes(@"{png}", tex.EncodeToPNG());\n')
    code = COMMON + f'''float x0 = {x0}f, z0 = {z0}f, step = {step}f; int nx = (int)(({x1}f - x0) / step), nz = (int)(({z1}f - z0) / step);
var fit = {"Van" if body == "van" else "Walker"}; var free = new bool[nx, nz]; int nFree = 0;
for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) {{ free[i, j] = fit(x0 + (i + 0.5f) * step, z0 + (j + 0.5f) * step); if (free[i, j]) nFree++; }}
var reach = new bool[nx, nz]; var parent = new int[nx * nz]; var q = new System.Collections.Generic.Queue<int>(); int[] DA = {{ -1, -1, -1, 0, 0, 1, 1, 1 }}, DB = {{ -1, 0, 1, -1, 1, -1, 0, 1 }};
System.Func<float, float, int> cell = (x, z) => ((int)((x - x0) / step)) * nz + (int)((z - z0) / step);
int s0 = cell({cfg["start"][0]}f, {cfg["start"][1]}f), best = -1; float bd = 1e9f;
for (int di = -10; di <= 10; di++) for (int dj = -10; dj <= 10; dj++) {{ int a = s0 / nz + di, b = s0 % nz + dj; if (a < 0 || b < 0 || a >= nx || b >= nz || !free[a, b]) continue; float d = di * di + dj * dj; if (d < bd) {{ bd = d; best = a * nz + b; }} }}
if (best >= 0) {{ reach[best / nz, best % nz] = true; q.Enqueue(best); }}
while (q.Count > 0) {{ int c = q.Dequeue(); int a = c / nz, b = c % nz;
  for (int d = 0; d < 8; d++) {{ int na = a + DA[d], nb = b + DB[d]; if (na < 0 || nb < 0 || na >= nx || nb >= nz || reach[na, nb] || !free[na, nb]) continue;
    if (DA[d] != 0 && DB[d] != 0 && (!free[a + DA[d], b] || !free[a, b + DB[d]])) continue; reach[na, nb] = true; parent[na * nz + nb] = c; q.Enqueue(na * nz + nb); }} }}
sb.Append("{body}: " + nx + " x " + nz + " cells of " + step.ToString(ci) + " m, free " + nFree + "\\n");
System.Action<string, float, float> G = (n, x, z) => {{ int c = cell(x, z); int a = c / nz, b = c % nz; bool any = false, anyFree = false; int hit = -1;
  for (int di = -2; di <= 2; di++) for (int dj = -2; dj <= 2; dj++) {{ int p = a + di, r = b + dj; if (p < 0 || r < 0 || p >= nx || r >= nz) continue; if (reach[p, r]) {{ any = true; hit = p * nz + r; }} anyFree |= free[p, r]; }}
  sb.Append(n + string.Format(ci, " ({{0:F1}}; {{1:F1}}): ", x, z) + (any ? "reachable" : anyFree ? "closed off" : "no free cell") + "\\n");
  if (any) {{ var path = new System.Collections.Generic.List<int>(); int k = hit, guard = 0; while (k != best && guard++ < 400000) {{ path.Add(k); k = parent[k]; }} path.Reverse();
    sb.Append("  path:"); for (int i = 0; i < path.Count; i += System.Math.Max(1, path.Count / 25)) sb.Append(string.Format(ci, " ({{0:F1}}; {{1:F1}})", x0 + (path[i] / nz + 0.5f) * step, z0 + (path[i] % nz + 0.5f) * step)); sb.Append("\\n"); }} }};
{goals}{png_code}return sb.ToString();'''
    return ue.run_long(code)


def line(x0, z0, x1, z1, ext=0.0, step=0.25):
    code = COMMON + f'''float ax = {x0}f, az = {z0}f, bx = {x1}f, bz = {z1}f, ext = {ext}f; float L = UnityEngine.Mathf.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)); float ux = (bx - ax) / L, uz = (bz - az) / L;
var rot = UnityEngine.Quaternion.LookRotation(new UnityEngine.Vector3(-uz, 0f, ux)); int n = 0, freeN = 0;
for (float s = -ext; s <= L + ext + 1e-4f; s += {step}f) {{ float x = ax + ux * s, z = az + uz * s, g = th(x, z); n++;
  int k = UnityEngine.Physics.OverlapBoxNonAlloc(new UnityEngine.Vector3(x, g + 1.175f, z), new UnityEngine.Vector3(0.99f, 0.825f, 0.5f), hits, rot, ~0, UnityEngine.QueryTriggerInteraction.Ignore);
  bool hit = false; for (int h = 0; h < k; h++) {{ var c = hits[h]; {IGN} hit = true; break; }}
  if (!hit) {{ freeN++; sb.Append(string.Format(ci, "FREE ({{0:F2}}; {{1:F2}}; {{2:F2}}) at {{3:F2}} m\\n", x, g, z, s)); }} }}
sb.Insert(0, n + " points, " + freeN + " free\\n"); return sb.ToString();'''
    return ue.run_long(code)


def touch(structure):
    return ue.run(f'''UnityEngine.Transform pt = null; foreach (var t in UnityEngine.Object.FindObjectsByType<UnityEngine.Transform>(UnityEngine.FindObjectsSortMode.None)) if (t.name == "{structure}") {{ pt = t; break; }}
if (pt == null) return "no {structure}"; UnityEngine.Physics.SyncTransforms(); float deepest = 0f; string what = ""; var hits = new UnityEngine.Collider[64];
foreach (var a in pt.GetComponentsInChildren<UnityEngine.Collider>()) {{ if (a.isTrigger) continue;
  int k = UnityEngine.Physics.OverlapBoxNonAlloc(a.bounds.center, a.bounds.extents + UnityEngine.Vector3.one * 0.02f, hits, UnityEngine.Quaternion.identity, ~0, UnityEngine.QueryTriggerInteraction.Ignore);
  for (int i = 0; i < k; i++) {{ var b = hits[i]; if (b.transform.IsChildOf(pt) || b is UnityEngine.TerrainCollider) continue; bool rock = false; for (var t = b.transform; t != null; t = t.parent) if (t.name == "Rocks") {{ rock = true; break; }} if (!rock) continue;
    if (UnityEngine.Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out UnityEngine.Vector3 dir, out float dist) && dist > 0.001f) {{
      if (dist > deepest) deepest = dist; what += b.transform.name + "@" + b.transform.position.ToString("F1") + " " + dist.ToString("F3") + "; "; }} }} }}
return "{structure}: deepest " + deepest.ToString("F3") + " m " + what;''')


def probe(player=None, parts=(), items=()):
    proj = ue.clone(player) if player else ue.PROJ
    pl = ', '.join(f'"{p}"' for p in parts) or '""'
    il = ', '.join(f'"{i}"' for i in items) or '""'
    return ue.run(f'''var nm = Unity.Netcode.NetworkManager.Singleton; var sb = new System.Text.StringBuilder((nm.IsServer ? "host" : "client") + " id " + nm.LocalClientId + "; ");
var all = UnityEngine.Object.FindObjectsByType<UnityEngine.Transform>(UnityEngine.FindObjectsSortMode.None);
foreach (var n in new[] {{ {pl} }}) foreach (var t in all) if (n != "" && t.name == n) {{ sb.Append(n + " " + t.localEulerAngles.ToString("F0") + " @" + t.position.ToString("F2") + "; "); break; }}
foreach (var n in new[] {{ {il} }}) foreach (var c in Snow.Cargo.CargoItem.All) {{ if (n != "" && c.name.StartsWith(n)) {{ sb.Append(n + " @" + c.transform.position.ToString("F2") + " " + c.Describe() + "; "); break; }} }}
int items = 0; foreach (var c in Snow.Cargo.CargoItem.All) if (c.IsSpawned) items++;
var grid = UnityEngine.Object.FindFirstObjectByType<SkyIsland.SkyIslandPowerGrid>(); var dn = Snow.Levels.Forest.ForestDayNight.Instance;
sb.Append("items " + items + ", grid " + (grid != null && grid.Powered) + (dn != null ? ", hour " + dn.Hour.ToString("F2") : "")); return sb.ToString();''', proj=proj)


if __name__ == '__main__':
    a = sys.argv[1:]
    if a[0] == 'reach':
        print(reach(json.load(open(a[1], encoding='utf-8')), a[2], a[3] if len(a) > 3 else None))
    elif a[0] == 'line':
        print(line(*[float(v) for v in a[1:]]))
    elif a[0] == 'touch':
        import time
        t0 = time.time()
        while True:
            print(f'{time.time() - t0:4.1f}s', touch(a[1]))
            if time.time() - t0 > (float(a[2]) if len(a) > 2 else 0):
                break
            time.sleep(0.5)
    elif a[0] == 'probe':
        player = int(a[1]) if len(a) > 1 and a[1].isdigit() else None
        parts = a[a.index('--parts') + 1:] if '--parts' in a else []
        items = []
        if '--items' in a:
            items = a[a.index('--items') + 1:]
            parts = [p for p in parts if p not in items and p != '--items']
        print(probe(player, parts, items))
