"""Plan of layout_suggestion.json over the ground of context/start_gate, Unity x (east) / z (north), north up: contours every 0.5 m
(heights from the gate), the road, the gate, the van, the old fence, trees; the suggested booth, light, tyres, rubbish, car and parking.
python3 start_plan.py [grid_dir] -> rend/start/layout_plan.png"""
import os, sys, json, math
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.patches import Polygon, Circle

HERE = os.path.dirname(os.path.abspath(__file__))
GD = sys.argv[1] if len(sys.argv) > 1 else '/home/claude/start'
sys.argv = sys.argv[:1] + [GD]
import start_layout as L

M, H = L.M, L.H
LAY = json.load(open(os.path.join(HERE, 'out', 'layout_suggestion.json'), encoding='utf-8'))


def xz(u, v):
    bx = L.G0[0] + L.AX[0] * np.asarray(u) + L.AY[0] * np.asarray(v)
    by = L.G0[1] + L.AX[1] * np.asarray(u) + L.AY[1] * np.asarray(v)
    return -bx, -by


def rect(c, front, x0, x1, y0, y1):
    return [xz(*L.local_to_uv(c, front, lx, ly)) for lx, ly in ((x0, y0), (x1, y0), (x1, y1), (x0, y1))]


fig, ax = plt.subplots(figsize=(10, 10), dpi=110)
UU, VV = np.meshgrid(L.US, L.VS)
X, Z = xz(UU, VV)
cs = ax.contour(X, Z, H, levels=np.arange(-8, 10.1, 0.5), colors='#8a8a80', linewidths=0.5)
ax.clabel(cs, levels=np.arange(-8, 10.1, 1.0), fontsize=6, fmt='%.0f')
road = [xz(u, v) for u, v in ((-3.5, -25), (4.5, -25), (4.5, 25), (-3.5, 25))]
ax.add_patch(Polygon(road, closed=True, fc='#d8d0bc', ec='none', alpha=0.7, zorder=1))
ax.plot(*zip(xz(-4.52, 0), xz(4.52, 0)), color='#c03020', lw=3, zorder=5)
ax.text(*xz(0.0, -1.6), 'ZoneGate', ha='center', va='center', fontsize=8, color='#c03020')
van = rect((1.79, 4.56), (0.13, 0.99), -1.0, 1.0, -2.2, 2.2)
ax.add_patch(Polygon(van, closed=True, fc='#6080a0', ec='k', lw=0.6, zorder=4))
ax.text(*xz(1.79, 4.56), 'фургон', ha='center', va='center', fontsize=7, color='w', zorder=6)
for a, b in (((7.3, -1.0), (28.5, -19.3)), ((-7.9, 12.1), (-29.1, 30.4))):
    ax.plot(*zip(xz(*a), xz(*b)), color='#7a5030', lw=1.4, ls='--', zorder=3)
ax.text(*xz(-13.0, 18.4), 'OldFence', fontsize=7, color='#7a5030', rotation=-41)
for tu, tv, bush in M['trees']:
    if -25 < tu < 25 and -25 < tv < 25:
        ax.add_patch(Circle(xz(tu, tv), 0.8 if bush else 1.4, fc='#4a7a40' if not bush else '#7aa060', ec='none', alpha=0.7, zorder=2))
ax.plot(*xz(4.2, 14.5), 's', color='#305080', ms=5, zorder=5)
ax.text(*xz(5.6, 14.5), 'Sign_Observatory', fontsize=7, color='#305080', va='center')
for pu in (-3.6, 3.6):
    ax.plot(*xz(pu, 23.5), 'o', color='#305080', ms=3, zorder=5)
pad = LAY['areas'][0]
P = pad['gateUV']
ax.add_patch(Polygon([xz(u, v) for u, v in ((P['u'][0], P['v'][0]), (P['u'][1], P['v'][0]), (P['u'][1], P['v'][1]),
                                            (P['u'][0], P['v'][1]))], closed=True, fc='#f0e0a0', ec='#a08020', lw=1.0, alpha=0.8, zorder=2))
for i in range(1, 3):
    v = P['v'][0] + 2.5 * i
    ax.plot(*zip(xz(P['u'][0], v), xz(P['u'][1], v)), color='#a08020', lw=0.6, zorder=3)
ax.text(*xz(9.6, 23.0), 'Parking_SW\nуровень %.2f' % pad['level'], fontsize=7, color='#806010', ha='center')
COL = {'StartBooth': '#d04040', 'StartCar': '#a02828', 'StartLamp': '#e0a000', 'StartTyres': '#303030', 'StartTrash': '#705040'}
for o in LAY['objects']:
    c = tuple(o['gateUV'])
    yaw = math.radians(o['rotation'][1])
    f = None
    # the prefab's front in the gate frame from its yaw (inverse of L.yaw)
    bdir = np.array([-math.sin(yaw), -math.cos(yaw)])
    f = (float(bdir @ L.AX), float(bdir @ L.AY))
    n = o['prefab']
    if n == 'StartBooth':
        poly = rect(c, f, -1.25, 1.25, -1.25, 1.25)
        door = rect(c, f, 1.0, 1.12, -0.32, 0.52)
        ax.add_patch(Polygon(door, closed=True, fc='k', ec='none', zorder=7))
        win = rect(c, f, -0.80, 0.22, -1.05, -0.95)
        ax.add_patch(Polygon(win, closed=True, fc='#80c0f0', ec='none', zorder=7))
    elif n == 'StartCar':
        poly = rect(c, f, -0.9, 0.9, -2.27, 2.25)
    elif n == 'StartLamp':
        poly = rect(c, f, -0.55, 0.55, -0.35, 0.62)
        ax.annotate('', xy=xz(*(np.array(c) + np.array(f) * 4.0)), xytext=xz(*c),
                    arrowprops=dict(arrowstyle='->', color='#e0a000', lw=1.2), zorder=7)
    elif n == 'StartTyres':
        poly = rect(c, f, -0.32, 0.72, -0.32, 0.32)
    else:
        poly = rect(c, f, -0.7, 0.7, -0.55, 0.55)
    ax.add_patch(Polygon(poly, closed=True, fc=COL[n], ec='k', lw=0.6, alpha=0.9, zorder=6))
    if n != 'StartTyres':
        ax.plot(*zip(xz(*c), xz(*(np.array(c) + np.array(f) * (2.6 if n == 'StartCar' else 1.4)))), color='k', lw=0.6, zorder=8)
    off = {'StartBooth': (-2.6, 0.0), 'StartCar': (0.0, 1.6), 'StartLamp': (1.2, -1.6), 'StartTyres': (-1.4, -1.2), 'StartTrash': (-1.6, 1.6)}[n]
    ax.text(*xz(c[0] + off[0], c[1] + off[1]), n, fontsize=7, ha='center', va='center', zorder=9,
            bbox=dict(fc='w', ec='none', alpha=0.7, pad=0.5))
cx, cz = xz(1.0, 10.0)
ax.set_xlim(cx - 24, cx + 24)
ax.set_ylim(cz - 24, cz + 24)
ax.set_aspect('equal')
ax.set_xlabel('Unity x, м (восток →)')
ax.set_ylabel('Unity z, м (север ↑)')
ax.set_title('forest_start — предложение раскладки (layout_suggestion.json); изолинии — высота от ворот, м', fontsize=9)
ax.grid(color='#dddddd', lw=0.4)
out = os.path.join(HERE, 'rend', 'start', 'layout_plan.png')
fig.tight_layout()
fig.savefig(out)
print(out)
