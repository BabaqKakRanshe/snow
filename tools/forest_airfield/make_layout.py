"""The airfield on the east road (user 27.10: «полоса = дорога», like a German Autobahn emergency strip): the road is
straightened for 700 m and becomes the runway, 18 m of concrete modules on a smoothed profile of the ground along it (slope
up to 4 %, the modules tilt with it); the hangar is dug into the higher ground east of it, gate to the runway, the old road
bend in front of it is the taxiway. Reads the Forest ground as it is now (the World Sync export, 27.10 15:31: with the relief) and the
road from the export's paint; writes Snow/Assets/Snow/Levels/Forest/ForestAirfield.json for ForestAirfieldStep (items in
world x, z and Unity yaw; ForestGroundEdit rectangles for the strip, the clearing, the apron, the earth over the hall and the
holes at the gate) and a top view docs/gdd/locations/airfield/frames/site_road_layout.png.

python tools/forest_airfield/make_layout.py
"""
import json, math, os
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
MAP = os.path.join(ROOT, 'source', 'world_sync', 'forest', 'map')
EDITS = [os.path.join(ROOT, 'source', 'world_sync', 'forest', 'edits', 'streams', 'ground_delta.f32')]   # export 15:31 (relief) + streams / east ravine fill
OUT = os.path.join(ROOT, 'Snow', 'Assets', 'Snow', 'Levels', 'Forest', 'ForestAirfield.json')
VIEW = os.path.join(ROOT, 'docs', 'gdd', 'locations', 'airfield', 'frames', 'site_road_layout.png')
AF = 'Assets/Locations/Forest/Airfield/Prefabs/'
SKY = 'Assets/SkyIsland/Prefabs/'
L, MOD, HALF = 700.0, 10.0, 9.0
FLAT, FADE = 14.0, 22.0                    # the strip: flat to 14 m from the axis, back to the ground over 22 m
SLOPE_MAX, SMOOTH = 0.04, 80.0             # profile: slope cap, moving-average half window (m)

# ------------------------------------------------------------------ the ground now
m = json.load(open(os.path.join(MAP, 'map.json'), encoding='utf-8'))
g = m['ground']
n, step, gx0, gz0 = g['nx'], g['step'], g['x0'], g['z0']
H = np.fromfile(os.path.join(MAP, g['file']), dtype='<u2').reshape(n, n).astype(np.float32)
H = g['hmin'] + H / 65535.0 * (g['hmax'] - g['hmin'])
for e in EDITS:
    H += np.fromfile(e, dtype='<f4').reshape(n, n)


def h(x, z):
    c = np.clip((np.asarray(x) - gx0) / step, 0, n - 1.001)
    r = np.clip((np.asarray(z) - gz0) / step, 0, n - 1.001)
    c0, r0 = c.astype(int), r.astype(int)
    fc, fr = c - c0, r - r0
    return H[r0, c0] * (1 - fc) * (1 - fr) + H[r0, c0 + 1] * fc * (1 - fr) + H[r0 + 1, c0] * (1 - fc) * fr + H[r0 + 1, c0 + 1] * fc * fr


# ------------------------------------------------------------------ the east road's centre line (from the paint)
from PIL import Image, ImageDraw
pa = np.asarray(Image.open(os.path.join(MAP, 'paint.png')).convert('RGB')).astype(np.float32)   # 1 px/m, north up
road = ((pa.max(-1) - pa.min(-1)) < 25) & (pa.mean(-1) > 95)
rows, cols = np.nonzero(road)
RX, RZ = cols - 600.0, 600.0 - rows
k = (RX > 850) & (RX < 1150) & (RZ > -900) & (RZ < 250)
RX, RZ = RX[k], RZ[k]
cz = np.arange(-880.0, 241.0, 10.0)
cx = np.array([np.median(RX[np.abs(RZ - z) < 12]) if (np.abs(RZ - z) < 12).sum() > 20 else np.nan for z in cz])
ok = ~np.isnan(cx)
cz, cx = cz[ok], cx[ok]

# ------------------------------------------------------------------ the axis and the profile
S0, S1, PS = -L / 2 - 30.0, L / 2 + 30.0, 10.0
ss = np.arange(S0, S1 + 0.1, PS)
w = int(SMOOTH / PS)


def frame(a, b, zs):
    d = np.array([b, 1.0]) / math.hypot(b, 1.0)            # south -> north
    A = np.array([a + b * zs, zs])
    return d, A, A + d * (L / 2), np.array([d[1], -d[0]])  # across: + to the east


def profile_of(C, d, E):
    """the ground along the axis, smoothed, slope-capped"""
    raw = np.array([np.mean([h(*(C + d * s + E * o)) for o in (-6.0, 0.0, 6.0)]) for s in ss])
    prof = np.convolve(np.pad(raw, w, mode='edge'), np.ones(2 * w + 1) / (2 * w + 1), 'same')[w:-w]
    for _ in range(4):
        for i in range(1, len(prof)):
            prof[i] = np.clip(prof[i], prof[i - 1] - SLOPE_MAX * PS, prof[i - 1] + SLOPE_MAX * PS)
        for i in range(len(prof) - 2, -1, -1):
            prof[i] = np.clip(prof[i], prof[i + 1] - SLOPE_MAX * PS, prof[i + 1] + SLOPE_MAX * PS)
    return prof


# a least-squares line through the road over each 700 m window; the window with the least earthwork (and road off the axis) wins
best = None
for zs in np.arange(-860.0, -150.0, 10.0):
    kk = (cz >= zs) & (cz <= zs + 690.0)
    if kk.sum() < 40:
        continue
    b, a = np.polyfit(cz[kk], cx[kk], 1)
    off = float(np.abs(cx[kk] - (a + b * cz[kk])).max())
    d, A, C, E = frame(a, b, zs)
    prof = profile_of(C, d, E)
    dv = max(abs(float(h(*(C + d * s + E * o))) - float(np.interp(s, ss, prof))) for s in ss[::2] for o in (-FLAT, 0.0, FLAT))
    score = dv + 0.3 * off
    if best is None or score < best[0]:
        best = (score, off, a, b, zs, prof)
_, off_max, a, b, zs, prof = best
d, A, C, E = frame(a, b, zs)
YAW_N = math.degrees(math.atan2(d[0], d[1]))
YAW_S = YAW_N + 180.0


def W(s, o=0.0):
    """s along from the centre (+ north), o across (+ east) -> world x, z"""
    p = C + d * s + E * o
    return float(p[0]), float(p[1])


P = lambda s: float(np.interp(s, ss, prof))
dev = max(abs(h(*W(s, o)) - P(s)) for s in ss for o in (-FLAT, 0.0, FLAT))

items = []


def add(prefab, s, o, yaw, mode='ground', note=''):
    x, z = W(s, o)
    items.append(dict(prefab=prefab, x=round(x, 2), z=round(z, 2), yaw=round(yaw, 2), mode=mode, s=round(s, 1), o=round(o, 1), note=note))


# ---- the runway: 70 modules (the frames: FOREST_AIRFIELD_CONTRACT.md; a threshold's paving end is its prefab +Z)
nm = int(L / MOD)
for i in range(nm):
    s = -L / 2 + MOD / 2 + MOD * i                         # i = 0: the south end
    if i == 0:
        add(AF + 'RWY_Threshold.prefab', s, 0, YAW_S, 'runway', 'south end')
    elif i == 1:
        add(AF + 'RWY_Num35.prefab', s, 0, YAW_S, 'runway', 'read from the south (landing 35)')
    elif i == nm - 2:
        add(AF + 'RWY_Num17.prefab', s, 0, YAW_N, 'runway', 'read from the north (landing 17)')
    elif i == nm - 1:
        add(AF + 'RWY_Threshold.prefab', s, 0, YAW_N, 'runway', 'north end')
    else:
        worn = (i * 7) % 5 in (1, 3)
        add(AF + ('RWY_SlabWorn' if worn else 'RWY_Slab') + '.prefab', s, 0, YAW_N if (i * 3) % 4 < 2 else YAW_S, 'runway')
# ---- the hangar: east of the axis where the ground is highest above the profile (dug into the bank)
cands = []
for s in np.arange(-200.0, 200.1, 10.0):
    bank = np.mean([h(*W(s + du, o)) for du in (-15.0, 0.0, 15.0) for o in (50.0, 58.0, 66.0)]) - P(s)
    cands.append((bank, s))
HB, HS = max(cands)
HO = HALF + 30.0 + 6.0                                     # gate 45 m east of the axis: 30 m of slabs + the hangar's 6 m apron
HY = P(HS)                                                  # the hall floor at the runway's height there
YAW_H = math.degrees(math.atan2(-E[0], -E[1]))              # the gate (+Z) to the west, to the runway
hx, hz = W(HS, HO)
hangar = dict(x=round(hx, 2), y=round(HY, 2), z=round(hz, 2), yaw=round(YAW_H, 2))
for row in range(3):                                        # the apron: 2 x 3 slabs from the hangar's apron to the runway edge
    for du in (-9.0, 9.0):
        add(AF + 'RWY_Slab.prefab', HS + du, HO - 6.0 - 5.0 - 10.0 * row, YAW_H, 'apron', 'apron')
# ---- lights and markers
for i in range(12):
    s = -L / 2 + 5.0 + 60.0 * i
    for side in (-1, 1):
        add(AF + 'AF_EdgeLight.prefab', s, side * (HALF + 1.5), YAW_N, 'strip')
for end, sgn in ((-L / 2, -1), (L / 2, 1)):
    for x in range(-8, 9, 2):
        add(AF + 'AF_ThresholdLight.prefab', end + sgn * 1.2, float(x), YAW_N, 'strip')
for s in range(-325, 326, 50):
    for side in (-1, 1):
        if side > 0 and abs(s - HS) < 35:
            continue                                        # the apron
        add(AF + 'AF_EdgeMarker.prefab', float(s), side * 24.0, YAW_N, 'ground')
# ---- the road ends: barriers, the pointer and the hut at the south end (from the base), a barrier at the north end
add(SKY + 'Barrier_Boom.prefab', -L / 2 - 40.0, 0.0, YAW_N + 90.0, 'ground', 'south: the road onto the runway')
add(AF + 'AF_Sign.prefab', -L / 2 - 46.0, -9.0, YAW_S + 90.0, 'ground', 'faces the road from the south; arrow north onto the runway')
add(SKY + 'Guard_Hut.prefab', -L / 2 - 52.0, -20.0, YAW_N + 90.0, 'ground', 'by the south barrier')
add(SKY + 'Barrier_Boom.prefab', L / 2 + 40.0, 0.0, YAW_N + 90.0, 'ground', 'north end')
# ---- runway furniture
add(SKY + 'Windsock.prefab', L / 2 - 60.0, -26.0, 0.0, 'ground', 'west of the north end')
for j, (pre, o) in enumerate((('PAPI_Red', -16.0), ('PAPI_Red', -20.0), ('PAPI_White', -24.0), ('PAPI_White', -28.0))):
    add(SKY + pre + '.prefab', L / 2 - 120.0, o, YAW_N, 'ground', 'PAPI for landing 17, west side' if j == 0 else '')
add(SKY + 'Flood_Mast.prefab', HS + 22.0, HO - 4.0, YAW_H, 'ground', 'apron corner')
add(SKY + 'Electrical_Cabinet.prefab', HS - 21.0, HO - 2.0, YAW_H, 'ground', "the airfield's power")

# ------------------------------------------------------------------ ground rectangles (ForestGroundEdit, frames: u across +X, v along +Z at yaw)
cxs, czs = W(0.0)
rv0, rv1 = S0, S1
strip = dict(name='runway strip (profile)', x=round(cxs, 2), z=round(czs, 2), yaw=round(YAW_N, 3), y=-0.05, fade=FADE,
             rect=[-FLAT, rv0, FLAT, rv1], mode='set', heights=True, hole=False, clearTrees=True, clearGrass=True,
             profile=[round(float(v), 3) for v in prof], profileStep=PS)
clearing = dict(name='the clearing (trees)', x=round(cxs, 2), z=round(czs, 2), yaw=round(YAW_N, 3), y=0.0, fade=0.0,
                rect=[-30.0, rv0 - 10.0, 30.0, rv1 + 10.0], mode='cut', heights=False, hole=False, clearTrees=True, clearGrass=False)
fr = lambda name, rect, mode, y, fade, heights=True, hole=False, trees=True, grass=True: dict(
    name=name, x=hangar['x'], z=hangar['z'], yaw=hangar['yaw'], y=round(y, 3), fade=fade, rect=rect, mode=mode, heights=heights,
    hole=hole, clearTrees=trees, clearGrass=grass)
ground = [strip, clearing,
          # in the hangar's frame (as the bunker's): v + to the gate side (west), the hall at v < 0
          fr('apron pad', [-19.0, 0.2, 19.0, HO - HALF - 0.5], 'set', HY - 0.3, 6.0),
          fr('holes over the hall at the facade (the gate)', [-9.6, -4.0, 9.6, 0.4], 'cut', 0.0, 0.0, heights=False, hole=True, grass=False),
          fr('holes over the side door corridor', [9.0, -3.0, 11.6, -1.2], 'cut', 0.0, 0.0, heights=False, hole=True, grass=False),
          fr('earth over the hall roof', [-10.2, -16.8, 10.2, -3.6], 'raise', HY + 7.65, 3.0, trees=False, grass=False),
          # behind the hall only: its 12 m fade stops 4 m behind the facade (a fade to the front buried the gate, 27.10)
          fr('berm round the hall', [-18.0, -24.0, 18.0, -16.0], 'raise', HY + 6.5, 12.0, trees=False, grass=False),
          fr('trees off the apron and the facade', [-24.0, -6.0, 24.0, HO], 'cut', 0.0, 0.0, heights=False, grass=False)]

data = dict(version='1.0', note="The airfield on the east road (docs/gdd/locations/airfield, FOREST_AIRFIELD_CONTRACT.md): the road "
            "straightened for 700 m is the runway (user 27.10); generated by tools/forest_airfield/make_layout.py from the ground of "
            "the World Sync export + the relief edit. Items: world x, z, Unity yaw; mode runway = on the strip, tilted with the "
            "ground under it; apron / strip / ground = on the ground, upright.",
            runway=dict(south=[round(v, 2) for v in A], north=[round(v, 2) for v in A + d * L], heading=round(YAW_S % 360, 2),
                        roadOffsetMax=round(float(off_max), 1), cutFillMax=round(float(dev), 1)),
            hangar=hangar, plane='Assets/UtilityPlane/Prefabs/UtilityPlane.prefab', kitInstalled=True,
            plug=AF + 'AF_HangarPlug.prefab', hangarPrefab='Assets/Locations/Bunker/Prefabs/BunkerHangar.prefab',
            items=items, ground=ground)
with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(data, f, ensure_ascii=False, indent=1)
cnt = {}
for it in items:
    k_ = os.path.basename(it['prefab']).replace('.prefab', '')
    cnt[k_] = cnt.get(k_, 0) + 1
print(OUT)
print('runway', data['runway'], 'hangar', hangar, 'bank', round(float(HB), 1), 'at s', HS)
print(len(items), cnt)

# ------------------------------------------------------------------ top view on the relief
Hs = H[::3, ::3][::-1]
gy, gxx = np.gradient(Hs, 1.758)
shade = np.clip(0.6 + 0.6 * (-gxx * 0.7 - gy * 0.7) / np.sqrt(1 + gxx ** 2 + gy ** 2), 0, 1)
hn = (Hs - Hs.min()) / (Hs.max() - Hs.min())
rgb = np.stack([0.35 + 0.5 * hn, 0.45 + 0.35 * hn, 0.30 + 0.2 * hn], -1) * shade[..., None]
im = Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8))
pr = np.asarray(Image.fromarray(road.astype(np.uint8) * 255).resize(im.size)) > 127
arr = np.asarray(im).copy()
arr[pr] = (215, 215, 215)
im = Image.fromarray(arr)
dr = ImageDraw.Draw(im)
M = lambda x, z: ((x + 600) / 1.7578, (600 - z) / 1.7578)
dr.polygon([M(*W(-L / 2, -HALF)), M(*W(L / 2, -HALF)), M(*W(L / 2, HALF)), M(*W(-L / 2, HALF))], fill=(245, 245, 255))
col = {'AF_EdgeLight': (255, 200, 60), 'AF_ThresholdLight': (60, 255, 90), 'AF_EdgeMarker': (255, 60, 60)}
for it in items:
    nm_ = os.path.basename(it['prefab'])[:-7]
    if nm_.startswith('RWY') and it['mode'] == 'runway':
        continue
    c = col.get(nm_, (0, 220, 255) if not nm_.startswith('RWY') else (200, 200, 210))
    x, y = M(it['x'], it['z'])
    dr.rectangle([x - 1.5, y - 1.5, x + 1.5, y + 1.5], fill=c)
x, y = M(hangar['x'], hangar['z'])
dr.rectangle([x - 6, y - 6, x + 6, y + 6], fill=(255, 140, 0))
im.crop((700, 300, 1000, 900)).resize((450, 900)).save(VIEW)
print(VIEW)
