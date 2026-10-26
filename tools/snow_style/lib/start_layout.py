"""layout_suggestion.json: where the forest_start prefabs could stand at the SPERRGEBIET gate, in Unity world coordinates, from the
ground of context/start_gate (start_ctx_grid.py -> grid_H.npy, grid_meta.json: the terrain under a 0.5 m grid in the gate's frame).
Gate frame: u = the gate's x (across the road, + = SW), v = the gate's y (along the road, + = SE: the van's side; the gate is behind
the van). python3 start_layout.py [grid_dir] -> out/layout_suggestion.json"""
import os, sys, json, math, re
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
GD = sys.argv[1] if len(sys.argv) > 1 else '/home/claude/start'
M = json.load(open(os.path.join(GD, 'grid_meta.json')))
H = np.load(os.path.join(GD, 'grid_H.npy'))                     # H[iv, iu] = ground z - gate z
US, VS = np.array(M['us']), np.array(M['vs'])
G0, AX, AY = np.array(M['g0']), np.array(M['ax'][:2]), np.array(M['ay'][:2])
GATE_Y = G0[2]


def dc(x, nd=2):
    return f'{x:.{nd}f}'.replace('.', ',')                    # Russian notes: decimal comma


def ground(u, v):
    fu, fv = (u - US[0]) / 0.5, (v - VS[0]) / 0.5
    i, j = min(int(fu), len(US) - 2), min(int(fv), len(VS) - 2)
    a, b = fu - i, fv - j
    return float(H[j, i] * (1 - a) * (1 - b) + H[j, i + 1] * a * (1 - b) + H[j + 1, i] * (1 - a) * b + H[j + 1, i + 1] * a * b)


def road(v):
    return float(np.mean([ground(u, v) for u in np.arange(-1.0, 3.01, 0.5)]))


def unity(u, v, dz):
    b = G0[:2] + AX * u + AY * v
    return [round(-b[0], 3), round(GATE_Y + dz, 3), round(-b[1], 3)]


def yaw(du, dv, extra=0.0):
    """Unity yaw of a prefab whose front (+Z, the Blender root's -y) looks along (du, dv) of the gate frame"""
    b = AX * du + AY * dv
    return round((math.degrees(math.atan2(-b[0], -b[1])) + extra) % 360, 2)


def local_to_uv(c, front, lx, ly):
    """a point of a root's own frame (Blender x, y) -> gate frame, the root at c with its front (-y) along front"""
    f = np.array(front, float) / np.linalg.norm(front)
    xdir = np.array([-f[1], f[0]])                              # the root's +x: the front turned 90 deg anticlockwise (from above)
    return np.array(c) + xdir * lx - f * ly


def footprint(c, front, x0, x1, y0, y1, step=0.25):
    hs = [ground(*local_to_uv(c, front, lx, ly)) for lx in np.arange(x0, x1 + 1e-6, step) for ly in np.arange(y0, y1 + 1e-6, step)]
    return round(min(hs), 2), round(max(hs), 2)


objs, areas = [], []


def place(prefab, c, front, dz, note, yaw_extra=0.0, ground_box=None):
    e = dict(prefab=prefab, position=unity(c[0], c[1], dz), rotation=[0.0, yaw(*front, extra=yaw_extra), 0.0],
             gateUV=[round(c[0], 2), round(c[1], 2)], groundUnder=None, note=note)
    if ground_box:
        lo, hi = footprint(c, front, *ground_box)
        e['groundUnder'] = [round(GATE_Y + lo, 2), round(GATE_Y + hi, 2)]
    objs.append(e)
    return e


# the booth: just past the gate's NE end on the van's side, the window (front) to the road, the door (+x) to the SE (the van)
BOOTH_C, TO_ROAD = (-4.7, 2.9), (1.0, 0.0)
booth_dz = -0.50
place('StartBooth', BOOTH_C, TO_ROAD, booth_dz,
      'Будка у северо-восточного края ворот, со стороны фургона: окно и табличка — к дороге, дверь — на юго-восток (к фургону). '
      'Склон: земля под площадкой 2,5 × 2,5 м падает от дороги на северо-восток; выровнять пятно 3,5 × 3,5 м до y = ' +
      f'{dc(GATE_Y + booth_dz - 0.05)} (подсыпать до ~1 м у дальнего угла); площадка уходит в землю не больше чем на 0,30 м.',
      ground_box=(-1.25, 1.25, -1.25, 1.25))
tyres_c = tuple(local_to_uv(BOOTH_C, TO_ROAD, -1.95, -0.75))
place('StartTyres', tyres_c, TO_ROAD, ground(*tyres_c) - 0.03, 'Покрышки между будкой и воротами, у обочины.', yaw_extra=20.0,
      ground_box=(-0.35, 0.75, -0.35, 0.35))
trash_c = tuple(local_to_uv(BOOTH_C, TO_ROAD, 2.30, -0.90))
place('StartTrash', trash_c, TO_ROAD, ground(*trash_c) - 0.03,
      'Куча мусора у юго-восточного угла будки, со стороны дороги; не мешает двери (дверь открывается на юго-восток, к северо-востоку '
      'от этого места).', yaw_extra=-30.0, ground_box=(-0.7, 0.7, -0.55, 0.55))
# the work light: by the gate's SW post on the van's side, the head turned across the road to the booth and the gate's NE half
LAMP_C, LAMP_AIM = (3.45, 1.15), (-1.0, 0.02)
feet = [local_to_uv(LAMP_C, LAMP_AIM, 0.62 * math.cos(math.radians(a)), 0.62 * math.sin(math.radians(a))) for a in (90, 210, 330)]
fh = [ground(*p) for p in feet]
place('StartLamp', LAMP_C, LAMP_AIM, float(np.mean(fh)),
      'Фонарь на треноге у юго-западного столба ворот, со стороны фургона; прожектор светит через дорогу на будку и ворота. '
      f'Земля под ногами треноги: {dc(GATE_Y + min(fh))}…{dc(GATE_Y + max(fh))} (склон к дороге) — опустить ноги по месту или '
      'подровнять. Ящик аккумулятора — у ворот.')
# the parking: SW of the road where the ground is at the road's level, three slots across the road, noses to the hill
PAD = dict(u0=4.5, u1=10.5, v0=17.5, v1=25.0)
pad_dz = round(road((PAD['v0'] + PAD['v1']) / 2), 2)
cf = [ground(u, v) - pad_dz for u in np.arange(PAD['u0'], PAD['u1'] + 1e-6, 0.5) for v in np.arange(PAD['v0'], PAD['v1'] + 1e-6, 0.5)]
slots = [(7.6, PAD['v0'] + 1.25 + 2.5 * i) for i in range(3)]
place('StartCar', slots[0], TO_ROAD, pad_dz,
      'Брошенная машина на первом месте площадки (ближе к воротам), носом к склону; водительская дверь открывается на юго-восток, '
      'к соседнему месту. Высота — уровень площадки.', ground_box=(-0.9, 0.9, -2.3, 2.3))
pc = ((PAD['u0'] + PAD['u1']) / 2, (PAD['v0'] + PAD['v1']) / 2)
areas.append(dict(
    name='Parking_SW', centre=unity(pc[0], pc[1], pad_dz), size=[PAD['u1'] - PAD['u0'], PAD['v1'] - PAD['v0']],
    sizeNote='поперёк дороги × вдоль дороги, м', rotation=[0.0, yaw(*TO_ROAD), 0.0], level=round(GATE_Y + pad_dz, 2),
    corners=[unity(u, v, pad_dz) for u, v in ((PAD['u0'], PAD['v0']), (PAD['u1'], PAD['v0']), (PAD['u1'], PAD['v1']),
                                              (PAD['u0'], PAD['v1']))],
    slots=[dict(position=unity(u, v, pad_dz), rotation=[0.0, yaw(*TO_ROAD), 0.0]) for u, v in slots],
    cutFill=[round(min(cf), 2), round(max(cf), 2)],
    gateUV=dict(u=[PAD['u0'], PAD['u1']], v=[PAD['v0'], PAD['v1']]),
    note=f'Площадка под 3 машины (места по 2,5 м, носом к склону) на юго-западной обочине, 17–25 м за воротами, сразу за '
         f'указателем Sign_Observatory: земля здесь почти на уровне дороги — выровнять до y = {dc(GATE_Y + pad_dz)} '
         f'(срезать до {dc(max(cf), 1)} м у склона, подсыпать до {dc(-min(cf), 1)} м у дороги); деревьев нет, ель на краю '
         '(u 13,9; v 24,3) остаётся. Столбик EuroPost у дороги (u 3,6; v 23,5) — у въезда на площадку.'))
NE_NOTE = ('Почему не поляна к северо-востоку от дороги: у ворот она падает от дороги на 2–4 м (уклон ~23°) — площадка у дороги '
           'потребует насыпи 2–3 м; ниже (u −15…−9, v 16…24) склон положе (~8°), но это 2,5–3 м под дорогой (нужен съезд), и по её северному '
           'краю идёт старый забор OldFence (линия по z ≈ 554,7 от x −501 на восток). Если площадка всё же там — '
           'машину ставить носом вниз по склону с наклоном по земле.')
out = dict(
    note='Предложение раскладки пакета forest_start у ворот SPERRGEBIET (Forest, старт). Координаты — мира Unity, м; rotation — '
         'Euler Unity, градусы (только поворот вокруг Y); position.y — земля под началом префаба (у будки и площадки — уровень '
         'выравнивания). groundUnder — нынешняя земля под габаритом (min…max y). gateUV — те же места в рамке ворот: u — '
         'поперёк дороги (+ на юго-запад), v — вдоль дороги (+ на юго-восток, к фургону; ворота за фургоном). Расставляет проект.',
    gate=dict(prefab='ZoneGate', position=[round(-G0[0], 3), round(GATE_Y, 3), round(-G0[1], 3)], rotation=[0.0, 317.66, 0.0]),
    objects=objs, areas=areas, northEastGlade=NE_NOTE,
    source='context/start_gate (выгрузка 2026-10-25 16:50): земля Terrain, деревья и забор TerrainTrees; src/start_layout.py')
os.makedirs(os.path.join(HERE, 'out'), exist_ok=True)
txt = json.dumps(out, ensure_ascii=False, indent=1)
txt = re.sub(r'\[\s+([-0-9.,\s]+?)\s+\]', lambda mo: '[' + ' '.join(mo.group(1).split()) + ']', txt)   # number lists on one line
open(os.path.join(HERE, 'out', 'layout_suggestion.json'), 'w', encoding='utf-8').write(txt + '\n')
for e in objs:
    print(e['prefab'], e['position'], e['rotation'][1], e['gateUV'], e['groundUnder'])
print('pad', areas[0]['level'], areas[0]['cutFill'], areas[0]['centre'])
