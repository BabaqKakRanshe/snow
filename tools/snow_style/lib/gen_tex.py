"""Pixel-art panel atlas for the generator shed (the van's gauge language):
  T_Gen_Panel_BaseColor / _Emission (512 x 512): genset dials (oil, temp, battery, AC volts / amps / Hz), hour meter,
  kWh counter, the genset control panel face and the switchboard face plate (labels printed where the 3D controls
  sit), circuit card, signs, drum and tank stencils, sight-glass scale, indicator lamp lenses, bulkhead lamp lenses.
REG (x0, y0, x1, y1) in pixels, origin top-left; uv() gives (u0, v0, u1, v1).  GPANEL / BOARD give the panel-local
positions (u across, v up, metres) that gen_kit uses for the 3D controls, so texture and geometry agree.
python3 gen_tex.py [out_dir]"""
import os, sys, json, math
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import pxfont as F
import van2_tex as VT

N = 512
REG = {
    'oil': (0, 0, 48, 48), 'temp': (48, 0, 96, 48), 'batt': (96, 0, 144, 48),
    'acv': (0, 48, 64, 112), 'aca': (64, 48, 128, 112), 'hz': (128, 48, 192, 112),
    'hours': (144, 0, 208, 16), 'kwh': (144, 16, 224, 32), 'nameplate': (192, 48, 256, 80),
    'gpanel': (256, 0, 448, 160), 'board': (0, 160, 160, 360), 'card': (160, 160, 240, 260),
    'sign_danger': (240, 160, 304, 224), 'sign_flame': (304, 160, 368, 208), 'sign_gen': (368, 160, 464, 184),
    'sign_louvre': (368, 184, 464, 208), 'sign_start': (368, 208, 464, 240), 'sign_diesel': (464, 160, 512, 176),
    'lvl': (492, 300, 512, 476), 'st_dw': (240, 256, 336, 288), 'st_ds': (336, 256, 432, 288),
    'st_tank': (432, 256, 512, 288), 'st_petrol': (240, 288, 336, 320),
    'l_amber': (448, 0, 464, 16), 'l_red': (464, 0, 480, 16), 'l_green': (480, 0, 496, 16), 'l_white': (496, 0, 512, 16),
    'l_bulk': (448, 16, 480, 48), 'l_ext': (480, 16, 512, 48), 'keyplate': (192, 80, 224, 112),
    'estop': (224, 80, 256, 112), 'logpage': (0, 360, 112, 512),
}
# genset control panel face: 0.60 x 0.50 m, u = -0.30..0.30 (left to right seen from the front), v = 0..0.50 up
GP_W, GP_H = 0.60, 0.50
GPANEL = {
    'oil': (-0.20, 0.405, 0.062), 'temp': (-0.07, 0.405, 0.062), 'batt': (0.06, 0.405, 0.062),
    'hours': (0.205, 0.405, 0.10, 0.032),
    'acv': (-0.18, 0.268, 0.09), 'aca': (0.0, 0.268, 0.09), 'hz': (0.18, 0.268, 0.09),
    'lamps': [('GLOW', 'amber', -0.24), ('CHG', 'red', -0.16), ('OIL', 'red', -0.08), ('TEMP', 'red', 0.0),
              ('RUN', 'green', 0.08)], 'lamp_v': 0.17, 'lamp_d': 0.022,
    'vadj': (0.21, 0.165, 0.03),
    'key': (-0.20, 0.06, 0.05), 'glow': (-0.02, 0.06, 0.03), 'stop': (0.08, 0.06, 0.03), 'estop': (0.19, 0.06, 0.08),
}
# switchboard face plate: 0.80 x 1.00 m, u = -0.40..0.40, v = 0..1.00
BD_W, BD_H = 0.80, 1.00
BOARD = {
    'acv': (-0.24, 0.82, 0.12), 'aca': (0.0, 0.82, 0.12), 'hz': (0.24, 0.82, 0.12), 'kwh': (0.0, 0.71, 0.20, 0.05),
    'lamps': [('LINE', 'white', -0.24), ('GEN', 'green', 0.0), ('OVERLOAD', 'red', 0.24)], 'lamp_v': 0.635,
    'lamp_d': 0.035,
    'changeover': (-0.20, 0.40, 0.14), 'brk_gen': (0.10, 0.40), 'brk_line': (0.28, 0.40),
    'mcb_v': 0.20, 'mcb_u0': -0.315, 'mcb_du': 0.09,
    'circuits': ['HALL', 'OUTD', 'HEAT', 'RAD', 'SERV', 'SHOP', 'PUMP', 'SHED'],
}
CIRCUITS = ['HALL LIGHTS', 'OUTDOOR LIGHTS', 'HEATING', 'RADIO / MAST', 'SERVER ROOM', 'WORKSHOP', 'WATER PUMP', 'SHED']

INK, DIM, WHITE = (24, 24, 22), (90, 88, 80), (236, 236, 230)
CREAM, CREAM_D, CREAM_L = (196, 188, 158), (150, 142, 116), (220, 214, 190)
GREYP, GREYP_D, GREYP_L = (128, 132, 128), (88, 92, 88), (160, 164, 160)
RED, AMBER, GREEN, YEL = (200, 40, 28), (236, 150, 30), (60, 180, 70), (228, 186, 40)
LAMP = {'amber': (236, 150, 30), 'red': (210, 36, 24), 'green': (60, 200, 80), 'white': (236, 232, 210)}


def uv(name, n=N):
    return VT.uv(REG[name], n)


def _plate(img, r, col, hi, lo):
    x0, y0, x1, y1 = r
    F.rect(img, x0, y0, x1 - 1, y1 - 1, col)
    F.frame(img, x0, y0, x1 - 1, y1 - 1, hi)
    F.rect(img, x0 + 1, y1 - 1, x1 - 1, y1 - 1, lo)
    F.rect(img, x1 - 1, y0 + 1, x1 - 1, y1 - 1, lo)


def _p(r, W, H, u, v):
    """panel-local (u, v) metres -> pixel (x, y) inside region r of a W x H panel"""
    x0, y0, x1, y1 = r
    return x0 + (u + W / 2) / W * (x1 - x0), y0 + (H - v) / H * (y1 - y0)


def build(out):
    os.makedirs(out, exist_ok=True)
    img = np.zeros((N, N, 3), np.uint8)
    img[:] = (16, 16, 16)
    emi = np.zeros_like(img)
    dial = VT.dial
    dial(img, emi, REG['oil'], 0, 8, 4, 1, [(0, '0'), (8, '8')], a0=-60, a1=60, arcs=((0, 1, VT.RED), (2, 6, VT.GREEN)),
         title='OIL', big=False)
    dial(img, emi, REG['temp'], 40, 120, 40, 20, [(40, 'C'), (120, 'H')], a0=-60, a1=60,
         arcs=((70, 95, VT.GREEN), (105, 120, VT.RED)), title='TEMP', big=False)
    dial(img, emi, REG['batt'], 8, 16, 4, 1, [(8, '8'), (16, '16')], a0=-60, a1=60,
         arcs=((8, 11.5, VT.RED), (12.5, 14.5, VT.GREEN), (15, 16, VT.RED)), title='BATT', big=False)
    dial(img, emi, REG['acv'], 0, 500, 100, 50, [(0, '0'), (200, '200'), (400, '400')],
         arcs=((380, 420, VT.GREEN), (450, 500, VT.RED)), title='VOLTS')
    dial(img, emi, REG['aca'], 0, 100, 20, 10, [(0, '0'), (50, '50'), (100, '100')],
         arcs=((63, 100, VT.RED),), title='AMPS')                          # the breakers trip at 63 A
    dial(img, emi, REG['hz'], 45, 55, 5, 1, [(45, '45'), (50, '50'), (55, '55')],
         arcs=((49.5, 50.5, VT.GREEN), (45, 47, VT.RED), (53, 55, VT.RED)), title='HZ')
    # hour meter and kWh counter: drum digits, the last one white on black (tenths)
    for key, digits, unit in (('hours', '04127', 'H'), ('kwh', '018422', 'KWH')):
        x0, y0, x1, y1 = REG[key]
        F.rect(img, x0, y0, x1 - 1, y1 - 1, (4, 4, 4))
        for i, d in enumerate(digits + '3'):
            last = i == len(digits)
            cx0 = x0 + 2 + i * 6
            F.rect(img, cx0, y0 + 2, cx0 + 4, y1 - 3, (200, 200, 196) if last else (26, 26, 26))
            F.text(img, cx0 + 2.5, (y0 + y1) / 2, d, F.F35, (20, 20, 20) if last else WHITE)
        F.text(img, x1 - 3, (y0 + y1) / 2, unit, F.F35, (150, 150, 146), align='r')
    # alternator nameplate (engraved brass)
    r = REG['nameplate']
    _plate(img, r, (176, 150, 92), (204, 180, 120), (120, 98, 56))
    for i, t in enumerate(['GENERATOR SET', '40 KVA 400/230V', '50 HZ 1500 RPM', 'NO 7716-3']):
        F.text(img, (r[0] + r[2]) / 2, r[1] + 5 + i * 7, t, F.F35, (60, 44, 20))
    # genset control panel face: cream, labels where the controls are
    r = REG['gpanel']
    _plate(img, r, CREAM, CREAM_L, CREAM_D)
    F.text(img, (r[0] + r[2]) / 2, r[1] + 6, 'GENERATOR CONTROL', F.F35, INK)
    for k in ('oil', 'temp', 'batt'):
        u, v, d = GPANEL[k]
        x, y = _p(r, GP_W, GP_H, u, v)
        F.disc(img, x, y, d * 320 / 2 + 2, CREAM_D)
    u, v, w, h = GPANEL['hours']
    x, y = _p(r, GP_W, GP_H, u, v)
    F.rect(img, x - w * 160 - 2, y - h * 160 - 2, x + w * 160 + 1, y + h * 160 + 1, CREAM_D)
    F.text(img, x, y - h * 160 - 6, 'HOURS', F.F35, INK)
    for k in ('acv', 'aca', 'hz'):
        u, v, d = GPANEL[k]
        x, y = _p(r, GP_W, GP_H, u, v)
        F.disc(img, x, y, d * 320 / 2 + 2, CREAM_D)
    for t, c, u in GPANEL['lamps']:
        x, y = _p(r, GP_W, GP_H, u, GPANEL['lamp_v'])
        F.disc(img, x, y, GPANEL['lamp_d'] * 160 + 1.5, (40, 40, 40))
        F.text(img, x, y + 7, t, F.F35, INK)
    u, v, d = GPANEL['vadj']
    x, y = _p(r, GP_W, GP_H, u, v)
    F.text(img, x, y - 8, 'V ADJ', F.F35, INK)
    F.text(img, x - 7, y + 7, '-', F.F35, INK)
    F.text(img, x + 7, y + 7, '+', F.F35, INK)
    u, v, d = GPANEL['key']
    x, y = _p(r, GP_W, GP_H, u, v)
    F.disc(img, x, y, d * 160 + 2, (40, 40, 40))
    for a, t, dx in ((0, 'OFF', 0), (52, 'ON', 2), (95, 'START', 10)):
        px, py = F.pol(x, y, d * 160 + 8, a)
        F.text(img, px + dx, py, t, F.F35, RED if a == 95 else INK)
    for k, t in (('glow', 'GLOW'), ('stop', 'STOP')):
        u, v, d = GPANEL[k]
        x, y = _p(r, GP_W, GP_H, u, v)
        F.text(img, x, y - d * 160 - 5, t, F.F35, RED if k == 'stop' else INK)
    u, v, d = GPANEL['estop']
    x, y = _p(r, GP_W, GP_H, u, v)
    F.rect(img, x - d * 160, y - d * 160, x + d * 160 - 1, y + d * 160 - 1, YEL)
    F.text(img, x, y - d * 160 + 3, 'EMERG', F.F35, INK)
    # switchboard face plate: grey steel, meters, lamps, changeover, breakers, eight circuits
    r = REG['board']
    _plate(img, r, GREYP, GREYP_L, GREYP_D)
    F.text(img, (r[0] + r[2]) / 2, r[1] + 7, 'MAIN SWITCHBOARD', F.F35, INK)
    for k in ('acv', 'aca', 'hz'):
        u, v, d = BOARD[k]
        x, y = _p(r, BD_W, BD_H, u, v)
        F.disc(img, x, y, d * 100 + 2, GREYP_D)
    u, v, w, h = BOARD['kwh']
    x, y = _p(r, BD_W, BD_H, u, v)
    F.rect(img, x - w * 100 - 2, y - h * 100 - 2, x + w * 100 + 1, y + h * 100 + 1, GREYP_D)
    for t, c, u in BOARD['lamps']:
        x, y = _p(r, BD_W, BD_H, u, BOARD['lamp_v'])
        F.disc(img, x, y, BOARD['lamp_d'] * 100 + 2, (40, 40, 40))
        F.text(img, x, y + 8, t, F.F35, INK)
    u, v, d = BOARD['changeover']
    x, y = _p(r, BD_W, BD_H, u, v)
    F.disc(img, x, y, d * 100 + 1, (52, 54, 52))
    for a, t, dx in ((-60, 'LINE', -6), (0, 'OFF', 0), (60, 'GEN', 5)):
        px, py = F.pol(x, y, d * 100 + 8, a)
        F.text(img, px + dx, py, t, F.F35, INK)
    for k, t in (('brk_gen', 'GEN 63A'), ('brk_line', 'LINE 63A')):
        u, v = BOARD[k]
        x, y = _p(r, BD_W, BD_H, u, v)
        F.text(img, x, y - 18, t, F.F35, INK)
    y_r = _p(r, BD_W, BD_H, 0, BOARD['mcb_v'])[1]
    F.rect(img, r[0] + 6, y_r - 8, r[2] - 7, y_r + 8, (60, 62, 60))
    for i, t in enumerate(BOARD['circuits']):                          # numbers above, names below in two rows
        x, y = _p(r, BD_W, BD_H, BOARD['mcb_u0'] + i * BOARD['mcb_du'], BOARD['mcb_v'])
        F.text(img, x, y - 14, str(i + 1), F.F35, INK)
        F.text(img, x, y + (16 if i % 2 == 0 else 23), t, F.F35, INK)
    F.text(img, (r[0] + r[2]) / 2, r[3] - 8, 'DANGER 400 V', F.F35, RED)
    # circuit card inside the cabinet door
    r = REG['card']
    _plate(img, r, (226, 220, 196), (240, 236, 220), (160, 154, 130))
    F.text(img, (r[0] + r[2]) / 2, r[1] + 5, 'CIRCUITS', F.F35, INK)
    for i, t in enumerate(CIRCUITS):
        F.text(img, r[0] + 4, r[1] + 13 + i * 8, f'{i + 1} {t}', F.F35, INK, align='l', valign='t')
    for i, t in enumerate(['START THE SET,', 'THEN GEN, THEN', 'ONE BY ONE']):
        F.text(img, r[0] + 4, r[1] + 82 + i * 6, t, F.F35, RED, align='l', valign='t')
    # signs
    r = REG['sign_danger']
    F.rect(img, r[0], r[1], r[2] - 1, r[3] - 1, (236, 232, 220))
    cx = (r[0] + r[2]) / 2
    for k in range(34):
        w = k * 0.95
        F.rect(img, cx - w, r[1] + 6 + k, cx + w - 1, r[1] + 6 + k, (20, 20, 20) if k > 31 else YEL)
    for k in range(4, 30):
        w = (k - 4) * 0.8
        F.rect(img, cx - w, r[1] + 6 + k, cx + w - 1, r[1] + 6 + k, YEL)
    F.line(img, cx + 3, r[1] + 14, cx - 3, r[1] + 26, (20, 20, 20), 2)
    F.line(img, cx - 3, r[1] + 26, cx + 3, r[1] + 26, (20, 20, 20), 2)
    F.line(img, cx + 3, r[1] + 26, cx - 2, r[1] + 36, (20, 20, 20), 2)
    F.text(img, cx, r[1] + 48, 'DANGER', F.F35, RED)
    F.text(img, cx, r[1] + 56, '400 VOLTS', F.F35, INK)
    r = REG['sign_flame']
    F.rect(img, r[0], r[1], r[2] - 1, r[3] - 1, (236, 232, 220))
    F.frame(img, r[0], r[1], r[2] - 1, r[3] - 1, RED)
    cx = (r[0] + r[2]) / 2
    F.ring(img, cx, r[1] + 16, 9, 12, RED)
    F.disc(img, cx, r[1] + 17, 4.5, AMBER)
    F.disc(img, cx, r[1] + 13, 2.5, AMBER)
    F.line(img, cx - 8, r[1] + 8, cx + 8, r[1] + 24, RED, 2)
    F.text(img, cx, r[1] + 34, 'NO NAKED', F.F35, INK)
    F.text(img, cx, r[1] + 41, 'FLAMES', F.F35, INK)
    r = REG['sign_gen']
    _plate(img, r, (32, 34, 34), (70, 72, 72), (10, 10, 10))
    F.text(img, (r[0] + r[2]) / 2, (r[1] + r[3]) / 2, 'GENERATOR', F.F57, (220, 214, 190))
    r = REG['sign_louvre']
    _plate(img, r, YEL, (246, 214, 90), (150, 120, 20))
    F.text(img, (r[0] + r[2]) / 2, r[1] + 7, 'OPEN LOUVRE', F.F35, INK)
    F.text(img, (r[0] + r[2]) / 2, r[1] + 15, 'BEFORE START', F.F35, INK)
    r = REG['sign_start']
    _plate(img, r, (226, 220, 196), (240, 236, 220), (160, 154, 130))
    for i, t in enumerate(['1 FUEL VALVE OPEN', '2 BLEED IF RAN DRY', '3 GLOW 20 S', '4 START 10 S MAX']):
        F.text(img, r[0] + 4, r[1] + 3 + i * 7, t, F.F35, INK, align='l', valign='t')
    r = REG['sign_diesel']
    F.rect(img, r[0], r[1], r[2] - 1, r[3] - 1, YEL)
    F.text(img, (r[0] + r[2]) / 2, (r[1] + r[3]) / 2, 'DIESEL ONLY', F.F35, INK)
    # sight glass scale: F 3/4 1/2 1/4 E from the top
    r = REG['lvl']
    F.rect(img, r[0], r[1], r[2] - 1, r[3] - 1, (226, 220, 196))
    for i, t in enumerate(['F', '3/4', '1/2', '1/4', 'E']):
        y = r[1] + 3 + i * (r[3] - r[1] - 7) / 4
        F.rect(img, r[0] + 1, y, r[0] + 4, y, INK)
        F.text(img, r[0] + 6, y, t, F.F35, INK, align='l')
    # stencils: white / yellow paint on a dark drum (the colour around them is transparent-ish: drum paint shows as
    # the base, so the region is filled with the drum colours by the decal alpha-free approach: dark backing)
    for key, lines, col, bg in (('st_dw', ['DIESEL', 'WINTER'], (236, 228, 200), (38, 54, 74)),
                                ('st_ds', ['DIESEL', 'SUMMER'], (236, 228, 200), (38, 54, 74)),
                                ('st_petrol', ['PETROL', 'FLAMMABLE'], (240, 236, 220), (150, 34, 26)),
                                ('st_tank', ['DIESEL', 'DAY TANK 100 L'], (24, 24, 22), (106, 112, 102))):
        r = REG[key]
        F.rect(img, r[0], r[1], r[2] - 1, r[3] - 1, bg)
        F.text(img, (r[0] + r[2]) / 2, r[1] + 10, lines[0], F.F57, col)
        F.text(img, (r[0] + r[2]) / 2, r[1] + 23, lines[1], F.F35, col)
    # indicator lenses: dark when off; the emission map holds the lit colour
    for key, c in (('l_amber', 'amber'), ('l_red', 'red'), ('l_green', 'green'), ('l_white', 'white')):
        x0, y0, x1, y1 = REG[key]
        col = LAMP[c]
        F.rect(img, x0, y0, x1 - 1, y1 - 1, tuple(int(v * 0.38) for v in col))
        F.disc(img, (x0 + x1) / 2 - 2, (y0 + y1) / 2 - 2, 2.5, tuple(int(v * 0.6) for v in col))
        F.rect(emi, x0, y0, x1 - 1, y1 - 1, col)
        F.disc(emi, (x0 + x1) / 2 - 2, (y0 + y1) / 2 - 2, 2.5, (255, 255, 240))
    for key, tint in (('l_bulk', (230, 224, 200)), ('l_ext', (236, 226, 190))):
        x0, y0, x1, y1 = REG[key]
        yy, xx = np.mgrid[y0:y1, x0:x1]
        rr = np.hypot(xx + 0.5 - (x0 + x1) / 2, yy + 0.5 - (y0 + y1) / 2) / ((x1 - x0) / 2)
        b = np.clip(1.0 - 0.55 * rr, 0, 1)
        t = VT.BAYER[np.mod(yy, 4), np.mod(xx, 4)]
        q = np.floor(b * 7 + t) / 7
        tile = (q[..., None] * np.array(tint)[None, None]).astype(np.uint8)
        img[y0:y1, x0:x1] = (tile * 0.55).astype(np.uint8)
        emi[y0:y1, x0:x1] = tile
        if key == 'l_bulk':                                    # the wire cage in front of the glass
            for k in range(x0 + 7, x1, 8):
                img[y0:y1, k] = (40, 40, 40)
                emi[y0:y1, k] = (60, 58, 50)
    # generator log page on the clipboard (handwriting in pencil grey, the last line in red)
    r = REG['logpage']
    F.rect(img, r[0], r[1], r[2] - 1, r[3] - 1, (226, 222, 204))
    for k in range(r[1] + 22, r[3] - 4, 9):
        F.rect(img, r[0] + 2, k, r[2] - 3, k, (176, 190, 200))
    F.rect(img, r[0] + 26, r[1] + 14, r[0] + 26, r[3] - 3, (210, 120, 110))
    F.text(img, (r[0] + r[2]) / 2, r[1] + 6, 'GENERATOR LOG', F.F35, INK)
    rows = [('DATE', 'HRS', 'NOTE'), ('02/11', '4096', 'OIL +0.5L'), ('09/11', '4104', 'FUEL FILTER'),
            ('16/11', '4113', 'BELT TIGHT'), ('23/11', '4120', 'BLED PUMP'), ('30/11', '4127', 'RAN 6 H'),
            ('', '', 'LOUVRE STUCK'), ('', '', 'WITH ICE'), ('03/12', '----', 'NO START'), ('', '', 'NOISES ON'),
            ('', '', 'THE ROOF')]
    for i, (a, b, c) in enumerate(rows):
        y = r[1] + 17 + i * 9
        col = INK if i < 8 else (150, 30, 24)
        F.text(img, r[0] + 3, y, a, F.F35, col, align='l', valign='t')
        F.text(img, r[0] + 29, y, b, F.F35, col, align='l', valign='t')
        F.text(img, r[0] + 49, y, c, F.F35, col, align='l', valign='t')
    # key plate and e-stop mushroom top
    r = REG['keyplate']
    F.rect(img, r[0], r[1], r[2] - 1, r[3] - 1, (30, 30, 30))
    F.disc(img, (r[0] + r[2]) / 2, (r[1] + r[3]) / 2, 13, (150, 152, 150))
    F.disc(img, (r[0] + r[2]) / 2, (r[1] + r[3]) / 2, 6, (40, 40, 40))
    F.rect(img, (r[0] + r[2]) / 2 - 1, r[1] + 11, (r[0] + r[2]) / 2, r[1] + 20, (10, 10, 10))
    r = REG['estop']
    F.rect(img, r[0], r[1], r[2] - 1, r[3] - 1, (40, 10, 8))
    F.disc(img, (r[0] + r[2]) / 2, (r[1] + r[3]) / 2, 15, RED)
    F.disc(img, (r[0] + r[2]) / 2 - 4, (r[1] + r[3]) / 2 - 4, 4, (236, 90, 70))
    Image.fromarray(img).save(os.path.join(out, 'T_Gen_Panel_BaseColor.png'))
    Image.fromarray(emi).save(os.path.join(out, 'T_Gen_Panel_Emission.png'))
    json.dump(dict(size=N, regions=REG, gpanel=GPANEL, board=BOARD), open(os.path.join(out, 'gen_panel_regions.json'), 'w'),
              indent=1)
    return os.path.join(out, 'T_Gen_Panel_BaseColor.png'), os.path.join(out, 'T_Gen_Panel_Emission.png')


if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'tex', 'gen')
    print(build(out))
