"""Pixel-art atlases for the van, drawn the way the Utility Plane's v12/v13 atlases were:
  T_Van_Gauges_BaseColor / _Emission (256 x 256): dials (pixel fonts F35 / F57, hard edges), warning-lamp strip,
      switch plate, CB radio, heater plate, clock; the emission map is the night backlight
  T_Van_Lens_BaseColor / _Emission (128 x 128): 16 lens regions of 32 px (reflectors, prisms, domes) in 8 grey
      steps with an ordered dither, tinted per lamp
REG / LENS give the regions in pixels (x0, y0, x1, y1), origin top-left; uv() converts to UV (origin bottom-left).
python3 van2_tex.py <out_dir>"""
import os, sys, math, json
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import pxfont as F

GN = 256
REG = {
    'speedo': (0, 0, 64, 64), 'tacho': (64, 0, 128, 64), 'fuel': (128, 0, 160, 32), 'temp': (160, 0, 192, 32),
    'oil': (128, 32, 160, 64), 'volt': (160, 32, 192, 64), 'clock': (192, 0, 224, 32), 'compass': (224, 0, 256, 32),
    'annun': (0, 64, 128, 80), 'switch': (0, 80, 192, 104), 'radio': (0, 104, 128, 136), 'heater': (128, 104, 256, 128),
    'placard': (192, 64, 256, 80), 'odo': (192, 32, 240, 40),
}
LN = 128
LENS = {}
for i, k in enumerate(['head_rect', 'spot_round', 'work_rect', 'dome', 'amber', 'marker', 'tail_red', 'reverse',
                       'night_red', 'plate', 'tail_amber', 'side_amber', 'head_round']):
    LENS[k] = ((i % 4) * 32, (i // 4) * 32, (i % 4) * 32 + 32, (i // 4) * 32 + 32)
TINT = {'head_round': (226, 229, 232), 'head_rect': (226, 229, 232), 'spot_round': (222, 226, 228), 'work_rect': (222, 226, 228),
        'dome': (238, 230, 210), 'amber': (232, 138, 30), 'marker': (236, 150, 32), 'tail_red': (192, 26, 20),
        'reverse': (232, 234, 234), 'night_red': (176, 22, 16), 'plate': (222, 224, 224), 'tail_amber': (232, 138, 30),
        'side_amber': (232, 138, 30)}
BAYER = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) + 0.5) / 16.0


def uv(rect, n, inset=0.5):
    """pixel rect -> (u0, v0, u1, v1) in UV space with a half-texel inset"""
    x0, y0, x1, y1 = rect
    return ((x0 + inset) / n, 1 - (y1 - inset) / n, (x1 - inset) / n, 1 - (y0 + inset) / n)


# ------------------------------------------------------------------ gauges
BG, FACE = (10, 10, 10), (16, 16, 16)
WHITE, GREY, DGREY = (236, 236, 230), (150, 150, 146), (90, 90, 88)
RED, GREEN, AMBER, BLUE = (220, 40, 30), (40, 190, 70), (240, 160, 30), (60, 110, 220)
PLATE, PLATE_HI, PLATE_LO = (34, 35, 38), (62, 64, 68), (14, 14, 15)
LCD_BG, LCD_ON = (22, 40, 24), (90, 230, 110)


def dial(img, emi, rect, vmin, vmax, major, minor, labels, a0=-120, a1=120, arcs=(), title='', sub='', big=True):
    """round gauge in the plane's pixel style: hard-edged ticks, F35 numerals, coloured arcs, title below the hub.
    Big dials put their numerals on the scale; small ones put the two end labels under the arc ends."""
    x0, y0, x1, y1 = rect
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    R = (x1 - x0) / 2
    F.disc(img, cx, cy, R - 0.5, (4, 4, 4))
    F.disc(img, cx, cy, R - 2.0, FACE)
    span = a1 - a0
    ang = lambda v: a0 + span * (v - vmin) / (vmax - vmin)
    for (va, vb, col) in arcs:
        F.ring(img, cx, cy, R - 5.5, R - 3.5, col, ang(va), ang(vb))
        F.ring(emi, cx, cy, R - 5.5, R - 3.5, col, ang(va), ang(vb))
    v = vmin
    while v <= vmax + 1e-6:
        is_major = abs((v - vmin) / major - round((v - vmin) / major)) < 1e-6
        r0 = (R - (9 if is_major else 6)) if big else (R - (6 if is_major else 5))
        F.tick(img, cx, cy, r0, R - 3, ang(v), WHITE, 1)
        F.tick(emi, cx, cy, r0, R - 3, ang(v), (200, 200, 190), 1)
        v += minor
    if big:
        for val, txt in labels:
            px, py = F.pol(cx, cy, R - 15, ang(val))
            F.text(img, px, py, txt, F.F35, WHITE)
            F.text(emi, px, py, txt, F.F35, (210, 210, 200))
        if title:
            F.text(img, cx, cy + R * 0.50, title, F.F35, GREY)
            F.text(emi, cx, cy + R * 0.50, title, F.F35, (120, 120, 116))
        if sub:
            F.text(img, cx, cy + R * 0.24, sub, F.F35, DGREY)
    else:
        (va, ta), (vb, tb) = labels[0], labels[-1]
        F.text(img, cx - R * 0.55, cy + 1, ta, F.F35, WHITE)
        F.text(img, cx + R * 0.55, cy + 1, tb, F.F35, WHITE)
        F.text(emi, cx - R * 0.55, cy + 1, ta, F.F35, (200, 200, 190))
        F.text(emi, cx + R * 0.55, cy + 1, tb, F.F35, (200, 200, 190))
        if title:
            F.text(img, cx, cy + R * 0.58, title, F.F35, GREY)
            F.text(emi, cx, cy + R * 0.58, title, F.F35, (120, 120, 116))
    F.disc(img, cx, cy, 2.0 if big else 1.5, (60, 60, 60))


def gauges(out, switch_labels='alt'):
    img = np.zeros((GN, GN, 3), np.uint8)
    img[:] = BG
    emi = np.zeros_like(img)
    dial(img, emi, REG['speedo'], 0, 120, 20, 10, [(0, '0'), (40, '40'), (80, '80'), (120, '120')], title='KM/H')
    dial(img, emi, REG['tacho'], 0, 5, 1, 0.5, [(i, str(i)) for i in range(6)],
         arcs=((0.7, 3.5, GREEN), (3.5, 4.0, AMBER), (4.0, 5, RED)), title='RPM', sub='x1000')
    dial(img, emi, REG['fuel'], 0, 4, 2, 1, [(0, 'E'), (4, 'F')], a0=-60, a1=60, arcs=((0, 0.6, RED),), title='FUEL',
         big=False)
    dial(img, emi, REG['temp'], 40, 120, 40, 20, [(40, 'C'), (120, 'H')], a0=-60, a1=60,
         arcs=((70, 100, GREEN), (105, 120, RED)), title='TEMP', big=False)
    dial(img, emi, REG['oil'], 0, 8, 4, 2, [(0, '0'), (8, '8')], a0=-60, a1=60, arcs=((0, 1, RED), (2, 6, GREEN)),
         title='OIL', big=False)
    dial(img, emi, REG['volt'], 10, 16, 3, 1, [(10, '10'), (16, '16')], a0=-60, a1=60,
         arcs=((10, 11.5, RED), (12.5, 14.5, GREEN), (14.8, 16, RED)), title='VOLT', big=False)
    # clock: hands stuck at 3:12 (painted, the clock is dead)
    x0, y0, x1, y1 = REG['clock']
    cx, cy, R = (x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) / 2
    F.disc(img, cx, cy, R - 0.5, (4, 4, 4))
    F.disc(img, cx, cy, R - 2.0, (210, 206, 190))
    for h in range(12):
        F.tick(img, cx, cy, R - (6 if h % 3 == 0 else 4), R - 3, h * 30, (20, 20, 20))
    F.tick(img, cx, cy, 0, R - 9, 3 * 30 + 6, (20, 20, 20), 1)
    F.tick(img, cx, cy, 0, R - 5, 72, (20, 20, 20), 1)
    F.disc(img, cx, cy, 1.5, (180, 30, 20))
    # compass ball window: a strip of the card behind a lubber line
    x0, y0, x1, y1 = REG['compass']
    F.rect(img, x0 + 1, y0 + 8, x1 - 2, y1 - 9, (4, 4, 4))
    F.rect(img, x0 + 3, y0 + 10, x1 - 4, y1 - 11, (200, 196, 180))
    for i, t in enumerate(['W', 'N', 'E']):
        F.text(img, x0 + 7 + i * 9, (y0 + y1) / 2, t, F.F35, (20, 20, 20))
    for i in range(4):
        F.rect(img, x0 + 11 + i * 9, (y0 + y1) / 2 - 1, x0 + 11 + i * 9, (y0 + y1) / 2 + 1, (60, 60, 60))
    F.rect(img, (x0 + x1) / 2, y0 + 9, (x0 + x1) / 2, y1 - 10, (200, 30, 20))
    # odometer drum
    x0, y0, x1, y1 = REG['odo']
    F.rect(img, x0, y0, x1 - 1, y1 - 1, (4, 4, 4))
    for i, d in enumerate('0471236'):
        F.rect(img, x0 + 2 + i * 6, y0 + 1, x0 + 6 + i * 6, y1 - 2, (24, 24, 24) if i < 6 else (200, 200, 196))
        F.text(img, x0 + 4.5 + i * 6, (y0 + y1) / 2, d, F.F35, WHITE if i < 6 else (20, 20, 20))
    # warning lamps: 8 cells
    x0, y0, x1, y1 = REG['annun']
    lamps = [('OIL', RED, 0), ('BAT', RED, 1), ('BRK', RED, 0), ('TMP', RED, 0), ('HI', BLUE, 1), ('<>', GREEN, 0),
             ('GLW', AMBER, 1), ('DIF', AMBER, 0)]
    for i, (t, c, lit) in enumerate(lamps):
        cx0 = x0 + i * 16
        F.rect(img, cx0, y0, cx0 + 15, y1 - 1, (6, 6, 6))
        dark = tuple(int(v * 0.32) for v in c)
        F.rect(img, cx0 + 1, y0 + 1, cx0 + 14, y1 - 2, dark)
        F.text(img, cx0 + 8, (y0 + y1) / 2, t, F.F35, tuple(int(v * 0.75) for v in c))
        if lit:
            F.rect(emi, cx0 + 1, y0 + 1, cx0 + 14, y1 - 2, tuple(int(v * 0.55) for v in c))
            F.text(emi, cx0 + 8, (y0 + y1) / 2, t, F.F35, c)
    # switch plate: 8 toggles, labels above, ON / OFF
    x0, y0, x1, y1 = REG['switch']
    F.rect(img, x0, y0, x1 - 1, y1 - 1, PLATE)
    F.frame(img, x0, y0, x1 - 1, y1 - 1, PLATE_HI)
    F.rect(img, x0 + 1, y1 - 2, x1 - 2, y1 - 2, PLATE_LO)
    names = ['HEAD', 'FOG', 'SPOT', 'ROOF', 'WORK', 'CABIN', 'STOVE', 'WIPER']
    for i, t in enumerate(names):
        cx = x0 + 12 + i * 24
        F.text(img, cx, y0 + 5, t, F.F35, WHITE)
        F.text(emi, cx, y0 + 5, t, F.F35, (150, 150, 140))
        F.disc(img, cx, y0 + 14, 3.5, (12, 12, 12))
        F.disc(img, cx, y0 + 14, 2.0, (90, 90, 90))
        F.text(img, cx, y1 - 5, ('ON' if i % 2 == 0 else 'OFF') if switch_labels == 'alt' else 'OFF', F.F35, DGREY)
    # CB radio
    x0, y0, x1, y1 = REG['radio']
    F.rect(img, x0, y0, x1 - 1, y1 - 1, (26, 26, 28))
    F.frame(img, x0, y0, x1 - 1, y1 - 1, (58, 58, 62))
    F.rect(img, x0 + 34, y0 + 6, x0 + 92, y0 + 22, LCD_BG)
    F.text(img, x0 + 63, y0 + 14, '27.135', F.F57, LCD_ON)
    F.rect(emi, x0 + 34, y0 + 6, x0 + 92, y0 + 22, (10, 40, 14))
    F.text(emi, x0 + 63, y0 + 14, '27.135', F.F57, LCD_ON)
    F.text(img, x0 + 16, y0 + 8, 'CB-27', F.F35, GREY)
    F.text(img, x0 + 16, y0 + 18, 'CH 09', F.F35, LCD_ON)
    F.text(emi, x0 + 16, y0 + 18, 'CH 09', F.F35, (60, 160, 70))
    for i in range(2):
        F.disc(img, x0 + 104 + i * 14, y0 + 15, 5.5, (8, 8, 8))
        F.disc(img, x0 + 104 + i * 14, y0 + 15, 3.5, (70, 70, 72))
        F.text(img, x0 + 104 + i * 14, y0 + 26, ['VOL', 'SQ'][i], F.F35, DGREY)
    for i in range(5):
        F.rect(img, x0 + 36 + i * 11, y0 + 25, x0 + 44 + i * 11, y0 + 29, (52, 52, 54))
    # heater plate
    x0, y0, x1, y1 = REG['heater']
    F.rect(img, x0, y0, x1 - 1, y1 - 1, PLATE)
    F.frame(img, x0, y0, x1 - 1, y1 - 1, PLATE_HI)
    F.text(img, x0 + 18, y0 + 6, 'HEAT', F.F35, WHITE)
    for i in range(40):
        t = i / 39
        c = (int(40 + 200 * t), int(80 + 20 * (1 - abs(t - 0.5) * 2)), int(220 - 200 * t))
        F.rect(img, x0 + 4 + i, y0 + 13, x0 + 4 + i, y0 + 16, c)
    F.text(img, x0 + 82, y0 + 6, 'FAN', F.F35, WHITE)
    for i, t in enumerate('0123'):
        F.text(img, x0 + 70 + i * 9, y0 + 15, t, F.F35, GREY)
    F.text(img, x0 + 110, y0 + 12, 'AIR', F.F35, DGREY)
    # placard
    x0, y0, x1, y1 = REG['placard']
    F.rect(img, x0, y0, x1 - 1, y1 - 1, (196, 190, 168))
    F.frame(img, x0, y0, x1 - 1, y1 - 1, (40, 40, 40))
    F.text(img, (x0 + x1) / 2, y0 + 5, 'MAX 8 PERS', F.F35, (30, 30, 30))
    F.text(img, (x0 + x1) / 2, y0 + 11, 'NO SMOKING', F.F35, (150, 30, 20))
    Image.fromarray(img).save(os.path.join(out, 'T_Van_Gauges_BaseColor.png'))
    Image.fromarray(emi).save(os.path.join(out, 'T_Van_Gauges_Emission.png'))


# ------------------------------------------------------------------ lenses
def quant(x, levels):
    h, w = x.shape
    t = np.tile(BAYER, (h // 4 + 1, w // 4 + 1))[:h, :w]
    return np.clip(np.floor(np.clip(x, 0, 1) * (levels - 1) + t) / (levels - 1), 0, 1)


H = 32
yy, xx = np.mgrid[0:H, 0:H].astype(np.float32)
U, V = (xx + 0.5) / H, 1.0 - (yy + 0.5) / H


def _g(u0, v0, su, sv):
    return np.exp(-(((U - u0) / su) ** 2 + ((V - v0) / sv) ** 2))


def pat(kind):
    """grey patterns in the plane's lens language (lens_tex_v13): what the glass and the reflector behind it do to the
    light. col = daylight look (multiplied by the tint), emi = shape of the glow when lit"""
    r = np.hypot(U - 0.5, V - 0.5) * 2
    glint = 0.16 * _g(0.24, 0.78, 0.07, 0.06)                     # window reflection, top left
    if kind == 'head_rect':
        # halogen headlamp: nine vertical Fresnel flutes, three soft bands, an elliptic parabolic reflector with
        # rings, the H4 bulb under its cap (dark centre, bright ring)
        fl = 0.5 + 0.5 * np.cos(2 * np.pi * U * 9)
        hb = 0.5 + 0.5 * np.cos(2 * np.pi * V * 3)
        rr = np.hypot((U - 0.5) * 1.9, V - 0.5) * 2
        rings = (0.5 + 0.5 * np.cos(2 * np.pi * rr * 2.5)) * (rr < 1.2)
        ring_b = np.exp(-((rr - 0.24) / 0.07) ** 2)
        cap = np.exp(-(rr / 0.10) ** 2)
        col = 0.46 + 0.14 * fl + 0.05 * hb + 0.14 * rings + 0.30 * ring_b - 0.22 * cap + glint
        emi = 0.30 + 0.10 * fl + 0.16 * rings + 0.70 * ring_b + 0.30 * np.exp(-(rr / 0.55) ** 2) - 0.25 * cap
    elif kind == 'head_round':
        # round sealed-beam headlamp: Fresnel rings round the edge, a centre field of vertical flutes and prisms,
        # the H4 bulb under its cap (dark centre, bright ring)
        rings = 0.5 + 0.5 * np.cos(2 * np.pi * r * 4.5)
        flute = 0.5 + 0.5 * np.cos(2 * np.pi * ((U * 7) % 1))
        prism = 1 - np.abs(((V * 5) % 1) - 0.5) * 2
        inner = r < 0.62
        ring_b = np.exp(-((r - 0.24) / 0.07) ** 2)
        cap = np.exp(-(r / 0.11) ** 2)
        col = np.where(r < 1, 0.46 + np.where(inner, 0.14 * flute + 0.06 * prism, 0.16 * rings) + 0.28 * ring_b
                       - 0.22 * cap, 0.40) + glint
        emi = np.where(r < 1, 0.30 + np.where(inner, 0.12 * flute + 0.05 * prism, 0.14 * rings) + 0.72 * ring_b
                       + 0.30 * np.exp(-(r / 0.55) ** 2) - 0.25 * cap, 0.15)
    elif kind == 'work_rect':
        # work lamp: a 5 x 3 grid of square prisms, each with a bright face, over a reflector with the bulb
        cu, cv = (U * 5) % 1, (V * 3) % 1
        cell = 1 - np.maximum(np.abs(cu - 0.5), np.abs(cv - 0.5)) * 2
        face = np.exp(-(((cu - 0.35) / 0.25) ** 2 + ((cv - 0.62) / 0.3) ** 2))
        rr = np.hypot((U - 0.5) * 1.5, V - 0.5) * 2
        ring_b = np.exp(-((rr - 0.22) / 0.08) ** 2)
        col = 0.44 + 0.14 * cell + 0.16 * face + 0.22 * ring_b - 0.15 * np.exp(-(rr / 0.09) ** 2) + glint
        emi = 0.32 + 0.16 * cell + 0.22 * face + 0.6 * ring_b + 0.25 * np.exp(-(rr / 0.6) ** 2)
    elif kind == 'spot_round':
        # driving lamp: four Fresnel rings, eight faint radial ribs, the bulb cap in the middle with a bright ring
        rings = 0.5 + 0.5 * np.cos(2 * np.pi * r * 4.0)
        ang = np.arctan2(V - 0.5, U - 0.5)
        spokes = 0.5 + 0.5 * np.cos(ang * 8)
        ring_b = np.exp(-((r - 0.26) / 0.07) ** 2)
        cap = np.exp(-(r / 0.13) ** 2)
        col = np.where(r < 1, 0.48 + 0.18 * rings + 0.05 * spokes + 0.30 * ring_b - 0.24 * cap, 0.40) + glint
        emi = np.where(r < 1, 0.30 + 0.16 * rings + 0.75 * ring_b + 0.30 * np.exp(-(r / 0.5) ** 2) - 0.25 * cap, 0.15)
    elif kind in ('dome', 'night_red'):
        # frosted dome with moulded rings, brighter in the middle (corners keep the rim value: no black at mips)
        rings = 0.5 + 0.5 * np.cos(2 * np.pi * r * (3.0 if kind == 'dome' else 2.0))
        col = np.where(r < 1, 0.66 + 0.14 * rings + 0.18 * np.exp(-(r / 0.35) ** 2), 0.62) + 0.8 * glint
        emi = np.where(r < 1, 0.55 + 0.18 * rings + 0.45 * np.exp(-(r / 0.40) ** 2), 0.5)
    elif kind in ('amber', 'tail_amber', 'side_amber'):
        # amber prism glass: horizontal ribs, a faint vertical cross-rib, the bulb glow
        ribs = 0.5 + 0.5 * np.cos(2 * np.pi * V * 6)
        cross = 0.5 + 0.5 * np.cos(2 * np.pi * U * 3)
        bulb = _g(0.5, 0.5, 0.22, 0.30)
        col = 0.50 + 0.20 * ribs + 0.06 * cross + 0.20 * bulb + glint
        emi = 0.35 + 0.18 * ribs + 0.05 * cross + 0.60 * bulb
    elif kind == 'marker':
        # capsule marker: lengthwise ribs, bulb in the middle
        ribs = 0.5 + 0.5 * np.cos(2 * np.pi * V * 4)
        bulb = _g(0.5, 0.5, 0.28, 0.4)
        col = 0.52 + 0.18 * ribs + 0.24 * bulb + glint
        emi = 0.38 + 0.15 * ribs + 0.60 * bulb
    elif kind == 'tail_red':
        # red tail / stop glass: a grid of cube-corner diamonds (reflex prisms), the bulb behind
        d = np.abs(((U * 5) % 1) - 0.5) + np.abs(((V * 5) % 1) - 0.5)
        facet = ((U * 5) % 1 < 0.5) ^ ((V * 5) % 1 < 0.5)
        bulb = _g(0.5, 0.5, 0.30, 0.30)
        col = 0.46 + 0.22 * (1 - d) + 0.08 * facet + 0.18 * bulb + glint
        emi = 0.32 + 0.22 * (1 - d) + 0.06 * facet + 0.60 * bulb
    elif kind == 'reverse':
        # clear reversing glass: vertical ribs over a small reflector with rings
        ribs = 0.5 + 0.5 * np.cos(2 * np.pi * U * 5)
        rr = np.hypot((U - 0.5) * 1.2, V - 0.5) * 2
        rings = (0.5 + 0.5 * np.cos(2 * np.pi * rr * 2.0)) * (rr < 1.1)
        bulb = np.exp(-(rr / 0.30) ** 2)
        col = 0.52 + 0.16 * ribs + 0.12 * rings + 0.18 * bulb + glint
        emi = 0.32 + 0.14 * ribs + 0.16 * rings + 0.60 * bulb
    else:                                                       # number-plate lamp: frosted, horizontal lines
        col = 0.66 + 0.10 * (0.5 + 0.5 * np.cos(2 * np.pi * V * 4)) + 0.12 * _g(0.5, 0.5, 0.4, 0.4)
        emi = 0.60 + 0.25 * _g(0.5, 0.5, 0.4, 0.4)
    return np.clip(col, 0, 1), np.clip(emi, 0, 1)


def lenses(out):
    C = np.zeros((LN, LN, 3), np.float32)
    E = np.zeros((LN, LN, 3), np.float32)
    for k, (x0, y0, x1, y1) in LENS.items():
        c, e = pat(k)
        c, e = quant(c, 8), quant(e, 6)
        t = np.array(TINT[k], np.float32) / 255
        C[y0:y1, x0:x1] = c[..., None] * t
        E[y0:y1, x0:x1] = e[..., None] * t
    Image.fromarray((C * 255 + 0.5).astype(np.uint8)).save(os.path.join(out, 'T_Van_Lens_BaseColor.png'))
    Image.fromarray((E * 255 + 0.5).astype(np.uint8)).save(os.path.join(out, 'T_Van_Lens_Emission.png'))


if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'tex', 'van2')
    os.makedirs(out, exist_ok=True)
    gauges(out)
    lenses(out)
    json.dump(dict(gauges=REG, lens=LENS, gauges_size=GN, lens_size=LN), open(os.path.join(out, 'van2_atlas_regions.json'), 'w'), indent=1)
    print('ok', out)
