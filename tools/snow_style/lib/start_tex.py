"""Panel atlas of the start post (256 x 256, pixel art, point-filtered) on the explicit-UV objects:
  sign        ZUFAHRT SPERRGEBIET / RESTRICTED AREA ACCESS - the plate on the booth's front wall (0.52 x 0.34 m): white enamel,
              red border, German first, English under it; rust at the screws and the edges
  l_bulk      the booth's bulkhead dome (warm, the shed's), the cage in front;  l_flood  the work light's glass: prismatic
              ribs over the reflector, the halogen tube (warm white when lit)
  glow_warm / glow_white  halo tiles for the glow cards (emission only)
python3 start_tex.py [out_dir] -> T_Start_Panel_BaseColor.png, T_Start_Panel_Emission.png, start_panel_regions.json"""
import os, sys, json, math
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pxfont as F
import van2_tex as VT

HERE = os.path.dirname(os.path.abspath(__file__))
N = 256
REG = {'sign': (0, 0, 160, 104), 'l_bulk': (160, 0, 192, 32), 'l_flood': (192, 0, 248, 40), 'glow_warm': (160, 64, 224, 128),
       'glow_white': (0, 128, 64, 192)}
INK, WHITE = (26, 24, 22), (236, 232, 220)
RED, RED_D = (190, 34, 26), (128, 26, 20)


def uv(name, n=N):
    return VT.uv(REG[name], n)


def _age(img, r, seed, spots=18, amount=0.18, col=(120, 100, 70)):
    x0, y0, x1, y1 = r
    rng = np.random.default_rng(seed)
    for _ in range(spots):
        fx, fy = rng.integers(x0 + 1, x1 - 1), rng.integers(y0 + 1, y1 - 1)
        img[fy, fx] = (img[fy, fx] * (1 - amount) + np.array(col) * amount).astype(np.uint8)


def draw_sign(img):
    x0, y0, x1, y1 = REG['sign']
    F.rect(img, x0, y0, x1 - 1, y1 - 1, WHITE)
    F.frame(img, x0 + 2, y0 + 2, x1 - 3, y1 - 3, RED, 3)
    cx = (x0 + x1) / 2
    F.text(img, cx, y0 + 15, 'ZUFAHRT', F.F57, INK, scale=2)
    F.text(img, cx, y0 + 34, 'SPERRGEBIET', F.F57, RED, scale=2)
    F.rect(img, x0 + 14, y0 + 48, x1 - 15, y0 + 48, (150, 146, 138))
    F.text(img, cx, y0 + 62, 'RESTRICTED AREA', F.F57, INK)
    F.text(img, cx, y0 + 74, 'ACCESS', F.F57, INK)
    F.text(img, cx, y0 + 89, 'UNBEFUGTEN ZUTRITT VERBOTEN', F.F35, RED_D)
    for sx, sy in ((x0 + 7, y0 + 7), (x1 - 8, y0 + 7), (x0 + 7, y1 - 8), (x1 - 8, y1 - 8)):      # the screws, rust round them
        F.rect(img, sx - 1, sy - 1, sx + 1, sy + 1, (110, 106, 100))
        img[sy, sx] = (60, 58, 54)
        for dx, dy in ((2, 1), (1, 3), (-1, 2), (0, 4)):
            yy, xx = sy + dy, sx + dx
            if y0 <= yy < y1 and x0 <= xx < x1:
                img[yy, xx] = (img[yy, xx] * 0.55 + np.array((120, 64, 30)) * 0.45).astype(np.uint8)
    rng = np.random.default_rng(71)
    for _ in range(260):                                        # chipped enamel along the edges, a streak below
        side = rng.integers(0, 4)
        t = rng.random()
        if side == 0:
            xx, yy = int(x0 + t * (x1 - x0 - 1)), int(y0 + rng.integers(0, 3))
        elif side == 1:
            xx, yy = int(x0 + t * (x1 - x0 - 1)), int(y1 - 1 - rng.integers(0, 3))
        elif side == 2:
            xx, yy = int(x0 + rng.integers(0, 3)), int(y0 + t * (y1 - y0 - 1))
        else:
            xx, yy = int(x1 - 1 - rng.integers(0, 3)), int(y0 + t * (y1 - y0 - 1))
        img[yy, xx] = (img[yy, xx] * 0.4 + np.array((86, 62, 44)) * 0.6).astype(np.uint8)
    _age(img, REG['sign'], 72, 70, 0.25, (110, 80, 50))


def draw_lenses(img, emi):
    x0, y0, x1, y1 = REG['l_bulk']                              # the shed's bulkhead dome (warm), the cage in front
    yy, xx = np.mgrid[y0:y1, x0:x1]
    rr = np.hypot(xx + 0.5 - (x0 + x1) / 2, yy + 0.5 - (y0 + y1) / 2) / ((x1 - x0) / 2)
    b = np.clip(1.0 - 0.55 * rr, 0, 1)
    t = VT.BAYER[np.mod(yy, 4), np.mod(xx, 4)]
    q = np.floor(b * 7 + t) / 7
    tile = (q[..., None] * np.array((230, 224, 200))[None, None]).astype(np.uint8)
    img[y0:y1, x0:x1] = (tile * 0.50).astype(np.uint8)
    emi[y0:y1, x0:x1] = tile
    for k in range(x0 + 7, x1, 8):
        img[y0:y1, k] = (40, 40, 40)
        emi[y0:y1, k] = (60, 58, 50)
    x0, y0, x1, y1 = REG['l_flood']                             # prismatic glass over the reflector, the tube across the middle
    yy, xx = np.mgrid[y0:y1, x0:x1]
    u = (xx + 0.5 - x0) / (x1 - x0)
    v = (yy + 0.5 - y0) / (y1 - y0)
    rib = 0.75 + 0.25 * (np.mod(xx - x0, 4) < 2)
    refl = np.clip(1.0 - 1.6 * np.abs(v - 0.5), 0, 1) * (0.6 + 0.4 * np.sin(u * math.pi))
    tube = np.exp(-((v - 0.5) / 0.06) ** 2) * (np.abs(u - 0.5) < 0.40)
    t = VT.BAYER[np.mod(yy, 4), np.mod(xx, 4)]
    base = np.floor((0.30 + 0.35 * refl) * rib * 8 + t) / 8
    img[y0:y1, x0:x1] = (base[..., None] * np.array((206, 200, 184))[None, None]).astype(np.uint8)
    e = np.clip(0.35 + 0.65 * refl * rib + 1.2 * tube, 0, 1)
    e = np.floor(e * 9 + t) / 9
    emi[y0:y1, x0:x1] = (e[..., None] * np.array((255, 236, 196))[None, None]).astype(np.uint8)
    edge = (xx == x0) | (xx == x1 - 1) | (yy == y0) | (yy == y1 - 1)
    img[y0:y1, x0:x1][edge] = (30, 30, 30)
    emi[y0:y1, x0:x1][edge] = (0, 0, 0)
    for key, col in (('glow_warm', (255, 190, 110)), ('glow_white', (255, 236, 200))):
        x0, y0, x1, y1 = REG[key]
        yy, xx = np.mgrid[y0:y1, x0:x1]
        rr = np.hypot(xx + 0.5 - (x0 + x1) / 2, yy + 0.5 - (y0 + y1) / 2) / ((x1 - x0) / 2)
        g = np.clip(1 - rr, 0, 1) ** 1.6
        t = VT.BAYER[np.mod(yy, 4), np.mod(xx, 4)]
        g = np.floor(g * 10 + t) / 10
        g[rr > 0.97] = 0
        emi[y0:y1, x0:x1] = (g[..., None] * np.array(col)[None, None]).astype(np.uint8)
        img[y0:y1, x0:x1] = 0


def build(out):
    os.makedirs(out, exist_ok=True)
    img = np.zeros((N, N, 3), np.uint8)
    img[:] = (16, 16, 16)
    emi = np.zeros_like(img)
    draw_sign(img)
    draw_lenses(img, emi)
    pb, pe = os.path.join(out, 'T_Start_Panel_BaseColor.png'), os.path.join(out, 'T_Start_Panel_Emission.png')
    Image.fromarray(img).save(pb)
    Image.fromarray(emi).save(pe)
    json.dump(dict(size=N, regions=REG, sign_m=[0.52, 0.34]), open(os.path.join(out, 'start_panel_regions.json'), 'w'), indent=1)
    return pb, pe


if __name__ == '__main__':
    print(build(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'tex', 'start')))
