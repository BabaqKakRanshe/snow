"""Pixel fonts and pixel-art primitives for the v12 PSX text atlas (T_Gauges_*, T_Displays_Emission).
Two hand-made bitmap fonts (original glyphs, no third-party font data):
  F35 - 3 x 5 px, advance 4: the smallest legible pixel font (capitals, digits, signs)
  F57 - 5 x 7 px, advance 6: the main font
Everything draws into a numpy uint8 RGB image (H, W, 3) with hard edges: no anti-aliasing anywhere."""
import math
import numpy as np

BOXES = None          # set to a list to record text boxes (x0, y0, x1, y1, txt) for layout checks

_F35 = {
    'A': '.#.|#.#|###|#.#|#.#', 'B': '##.|#.#|##.|#.#|##.', 'C': '.##|#..|#..|#..|.##', 'D': '##.|#.#|#.#|#.#|##.',
    'E': '###|#..|##.|#..|###', 'F': '###|#..|##.|#..|#..', 'G': '.##|#..|#.#|#.#|.##', 'H': '#.#|#.#|###|#.#|#.#',
    'I': '###|.#.|.#.|.#.|###', 'J': '..#|..#|..#|#.#|.#.', 'K': '#.#|#.#|##.|#.#|#.#', 'L': '#..|#..|#..|#..|###',
    'M': '#...#|##.##|#.#.#|#...#|#...#', 'N': '#..#|##.#|#.##|#..#|#..#', 'O': '.#.|#.#|#.#|#.#|.#.', 'P': '##.|#.#|##.|#..|#..',
    'Q': '.#.|#.#|#.#|##.|.##', 'R': '##.|#.#|##.|#.#|#.#', 'S': '.##|#..|.#.|..#|##.', 'T': '###|.#.|.#.|.#.|.#.',
    'U': '#.#|#.#|#.#|#.#|###', 'V': '#.#|#.#|#.#|#.#|.#.', 'W': '#...#|#...#|#.#.#|##.##|#...#', 'X': '#.#|#.#|.#.|#.#|#.#',
    'Y': '#.#|#.#|.#.|.#.|.#.', 'Z': '###|..#|.#.|#..|###',
    '0': '###|#.#|#.#|#.#|###', '1': '.#.|##.|.#.|.#.|###', '2': '##.|..#|.#.|#..|###', '3': '##.|..#|.#.|..#|##.',
    '4': '#.#|#.#|###|..#|..#', '5': '###|#..|##.|..#|##.', '6': '.##|#..|###|#.#|###', '7': '###|..#|.#.|.#.|.#.',
    '8': '###|#.#|###|#.#|###', '9': '###|#.#|###|..#|##.',
    ' ': '...|...|...|...|...', '.': '...|...|...|...|.#.', ',': '...|...|...|.#.|#..', ':': '...|.#.|...|.#.|...',
    '-': '...|...|###|...|...', '+': '...|.#.|###|.#.|...', '/': '..#|..#|.#.|#..|#..', '%': '#.#|..#|.#.|#..|#.#',
    '°': '.#.|#.#|.#.|...|...', "'": '.#.|.#.|...|...|...', '(': '..#|.#.|.#.|.#.|..#', ')': '#..|.#.|.#.|.#.|#..',
    '<': '..#|.#.|#..|.#.|..#', '>': '#..|.#.|..#|.#.|#..', '=': '...|###|...|###|...', '•': '...|...|.#.|...|...',
    'x': '...|#.#|.#.|#.#|...', '?': '##.|..#|.#.|...|.#.', '!': '.#.|.#.|.#.|...|.#.', '_': '...|...|...|...|###',
    '→': '...|.#.|###|.#.|...', '⇆': '.#.|###|...|###|.#.',
}

_F57 = {
    'A': '.###.|#...#|#...#|#####|#...#|#...#|#...#', 'B': '####.|#...#|#...#|####.|#...#|#...#|####.',
    'C': '.###.|#...#|#....|#....|#....|#...#|.###.', 'D': '####.|#...#|#...#|#...#|#...#|#...#|####.',
    'E': '#####|#....|#....|####.|#....|#....|#####', 'F': '#####|#....|#....|####.|#....|#....|#....',
    'G': '.###.|#...#|#....|#.###|#...#|#...#|.####', 'H': '#...#|#...#|#...#|#####|#...#|#...#|#...#',
    'I': '.###.|..#..|..#..|..#..|..#..|..#..|.###.', 'J': '..###|...#.|...#.|...#.|...#.|#..#.|.##..',
    'K': '#...#|#..#.|#.#..|##...|#.#..|#..#.|#...#', 'L': '#....|#....|#....|#....|#....|#....|#####',
    'M': '#...#|##.##|#.#.#|#.#.#|#...#|#...#|#...#', 'N': '#...#|#...#|##..#|#.#.#|#..##|#...#|#...#',
    'O': '.###.|#...#|#...#|#...#|#...#|#...#|.###.', 'P': '####.|#...#|#...#|####.|#....|#....|#....',
    'Q': '.###.|#...#|#...#|#...#|#.#.#|#..#.|.##.#', 'R': '####.|#...#|#...#|####.|#.#..|#..#.|#...#',
    'S': '.####|#....|#....|.###.|....#|....#|####.', 'T': '#####|..#..|..#..|..#..|..#..|..#..|..#..',
    'U': '#...#|#...#|#...#|#...#|#...#|#...#|.###.', 'V': '#...#|#...#|#...#|#...#|#...#|.#.#.|..#..',
    'W': '#...#|#...#|#...#|#.#.#|#.#.#|#.#.#|.#.#.', 'X': '#...#|#...#|.#.#.|..#..|.#.#.|#...#|#...#',
    'Y': '#...#|#...#|.#.#.|..#..|..#..|..#..|..#..', 'Z': '#####|....#|...#.|..#..|.#...|#....|#####',
    '0': '.###.|#...#|#..##|#.#.#|##..#|#...#|.###.', '1': '..#..|.##..|..#..|..#..|..#..|..#..|.###.',
    '2': '.###.|#...#|....#|...#.|..#..|.#...|#####', '3': '#####|...#.|..#..|...#.|....#|#...#|.###.',
    '4': '...#.|..##.|.#.#.|#..#.|#####|...#.|...#.', '5': '#####|#....|####.|....#|....#|#...#|.###.',
    '6': '..##.|.#...|#....|####.|#...#|#...#|.###.', '7': '#####|....#|...#.|..#..|.#...|.#...|.#...',
    '8': '.###.|#...#|#...#|.###.|#...#|#...#|.###.', '9': '.###.|#...#|#...#|.####|....#|...#.|.##..',
    ' ': '.....|.....|.....|.....|.....|.....|.....', '.': '.....|.....|.....|.....|.....|.##..|.##..',
    ',': '.....|.....|.....|.....|.##..|..#..|.#...', ':': '.....|.##..|.##..|.....|.##..|.##..|.....',
    '-': '.....|.....|.....|#####|.....|.....|.....', '+': '.....|..#..|..#..|#####|..#..|..#..|.....',
    '/': '....#|...#.|...#.|..#..|.#...|.#...|#....', '%': '##..#|##..#|...#.|..#..|.#...|#..##|#..##',
    '°': '.##..|#..#.|#..#.|.##..|.....|.....|.....', "'": '..#..|..#..|.#...|.....|.....|.....|.....',
    '(': '...#.|..#..|.#...|.#...|.#...|..#..|...#.', ')': '.#...|..#..|...#.|...#.|...#.|..#..|.#...',
    '<': '...#.|..#..|.#...|#....|.#...|..#..|...#.', '>': '.#...|..#..|...#.|....#|...#.|..#..|.#...',
    '=': '.....|.....|#####|.....|#####|.....|.....', '•': '.....|.....|.###.|.###.|.###.|.....|.....',
    'x': '.....|.....|#...#|.#.#.|..#..|.#.#.|#...#', '?': '.###.|#...#|....#|...#.|..#..|.....|..#..',
    '!': '..#..|..#..|..#..|..#..|..#..|.....|..#..', '_': '.....|.....|.....|.....|.....|.....|#####',
    '→': '.....|..#..|...#.|#####|...#.|..#..|.....', '⇆': '..#..|.#...|#####|.....|#####|...#.|..#..',
}


def _compile(src):
    out = {}
    for ch, s in src.items():
        rows = s.split('|')
        out[ch] = np.array([[c == '#' for c in r] for r in rows], bool)
    return out


F35 = dict(glyphs=_compile(_F35), w=3, h=5, adv=4, name='F35')
F57 = dict(glyphs=_compile(_F57), w=5, h=7, adv=6, name='F57')
for _f in (F35, F57):
    for _ch, _g in _f['glyphs'].items():
        assert _g.shape[0] == _f['h'] and _g.shape[1] >= 1, (_f['name'], _ch, _g.shape)


def text_mask(txt, font, scale=1, spacing=1):
    """bool mask (h*scale, width*scale) of a string; glyphs may differ in width (F35: M N W), 1 px apart"""
    g = font['glyphs']
    txt = txt.upper().replace('X100', 'x100')
    gl = [g.get(ch, g.get(ch.upper(), g['?'])) for ch in txt]
    W = max(1, sum(m.shape[1] for m in gl) + spacing * (len(gl) - 1))
    m = np.zeros((font['h'], W), bool)
    x = 0
    for gg in gl:
        m[:, x:x + gg.shape[1]] |= gg
        x += gg.shape[1] + spacing
    if scale > 1:
        m = np.repeat(np.repeat(m, scale, 0), scale, 1)
    return m


def text_size(txt, font, scale=1, spacing=1):
    m = text_mask(txt, font, scale, spacing)
    return m.shape[1], m.shape[0]


def blit_mask(img, m, x, y, col):
    """paint mask m with its top-left at integer (x, y); clipped"""
    H, W = img.shape[:2]
    h, w = m.shape
    x0, y0 = max(0, x), max(0, y)
    x1, y1 = min(W, x + w), min(H, y + h)
    if x1 <= x0 or y1 <= y0:
        return
    sub = m[y0 - y:y1 - y, x0 - x:x1 - x]
    img[y0:y1, x0:x1][sub] = col


def text(img, cx, cy, txt, font, col, scale=1, align='c', valign='c', spacing=1):
    """draw a string; (cx, cy) = anchor in pixels (float ok): centre / left / right, vertical centre or top"""
    m = text_mask(txt, font, scale, spacing)
    h, w = m.shape
    x = {'c': cx - w / 2.0, 'l': cx, 'r': cx - w}[align]
    y = cy - h / 2.0 if valign == 'c' else cy
    X, Y = int(math.floor(x + 0.5)), int(math.floor(y + 0.5))
    blit_mask(img, m, X, Y, col)
    if BOXES is not None:
        BOXES.append((X, Y, X + w - 1, Y + h - 1, txt))
    return w, h


def rect(img, x0, y0, x1, y1, col):
    """filled rectangle, inclusive integer bounds"""
    H, W = img.shape[:2]
    x0, y0, x1, y1 = int(round(x0)), int(round(y0)), int(round(x1)), int(round(y1))
    x0, x1 = max(0, min(x0, x1)), min(W - 1, max(x0, x1))
    y0, y1 = max(0, min(y0, y1)), min(H - 1, max(y0, y1))
    if x1 < x0 or y1 < y0:
        return
    img[y0:y1 + 1, x0:x1 + 1] = col


def frame(img, x0, y0, x1, y1, col, t=1):
    rect(img, x0, y0, x1, y0 + t - 1, col)
    rect(img, x0, y1 - t + 1, x1, y1, col)
    rect(img, x0, y0, x0 + t - 1, y1, col)
    rect(img, x1 - t + 1, y0, x1, y1, col)


def line(img, x0, y0, x1, y1, col, w=1):
    """continuous end points (pixel i covers [i, i + 1)): one pixel per step along the major axis, taken where the
    true line crosses that pixel column / row centre; w = 2 adds the neighbouring pixel across the line"""
    H, W = img.shape[:2]
    dx, dy = x1 - x0, y1 - y0
    pts = []
    if abs(dx) >= abs(dy):
        a, b = sorted((x0, x1))
        for i in range(int(math.floor(a)), int(math.floor(b)) + 1):
            xc = min(max(i + 0.5, a), b)
            y = y0 + (xc - x0) * dy / dx if dx else y0
            pts.append((i, int(math.floor(y)), 0, 1))
    else:
        a, b = sorted((y0, y1))
        for j in range(int(math.floor(a)), int(math.floor(b)) + 1):
            yc = min(max(j + 0.5, a), b)
            x = x0 + (yc - y0) * dx / dy
            pts.append((int(math.floor(x)), j, 1, 0))
    for (px, py, ox, oy) in pts:
        for k in range(w):
            xx, yy = px + ox * k, py + oy * k
            if 0 <= xx < W and 0 <= yy < H:
                img[yy, xx] = col


def disc(img, cx, cy, r, col):
    """pixels whose centres lie inside the circle (cx, cy in pixel units, pixel i covers [i, i+1))"""
    H, W = img.shape[:2]
    yy, xx = np.mgrid[0:H, 0:W]
    m = (xx + 0.5 - cx) ** 2 + (yy + 0.5 - cy) ** 2 <= r * r
    img[m] = col


def ring(img, cx, cy, r0, r1, col, a0=None, a1=None):
    """annulus r0 <= r <= r1 (pixel centres); optional sector a0..a1 in degrees clockwise from 12 o'clock"""
    H, W = img.shape[:2]
    yy, xx = np.mgrid[0:H, 0:W]
    dx, dy = xx + 0.5 - cx, yy + 0.5 - cy
    rr = np.sqrt(dx * dx + dy * dy)
    m = (rr >= r0) & (rr <= r1)
    if a0 is not None:
        a = np.degrees(np.arctan2(dx, -dy)) % 360.0
        lo, hi = a0 % 360.0, a1 % 360.0
        span = (a1 - a0) % 360.0
        if span == 0 and a1 != a0:
            span = 360.0
        m &= ((a - lo) % 360.0) <= span
    img[m] = col


def pol(cx, cy, r, a):
    """point at radius r, angle a (degrees clockwise from 12 o'clock)"""
    return cx + r * math.sin(math.radians(a)), cy - r * math.cos(math.radians(a))


def tick(img, cx, cy, r0, r1, a, col, w=1):
    x0, y0 = pol(cx, cy, r0, a)
    x1, y1 = pol(cx, cy, r1, a)
    line(img, x0, y0, x1, y1, col, w)


def rot_text(img, cx, cy, txt, font, col, angle, scale=1, ss=8, thr=0.45):
    """string rotated clockwise by `angle` degrees about its centre, re-rasterised to whole pixels (coverage >= thr)"""
    m = text_mask(txt, font, scale).astype(np.float32)
    h, w = m.shape
    R = int(math.ceil(math.hypot(w, h) / 2)) + 2
    H, W = img.shape[:2]
    a = math.radians(angle)
    ca, sa = math.cos(a), math.sin(a)
    x0, y0 = int(math.floor(cx)) - R, int(math.floor(cy)) - R
    n = 2 * R + 1
    acc = np.zeros((n, n), np.float32)
    o = (np.arange(ss) + 0.5) / ss
    for oy in o:
        for ox in o:
            yy, xx = np.mgrid[0:n, 0:n]
            px = x0 + xx + ox - cx
            py = y0 + yy + oy - cy
            # inverse rotation (screen y down, clockwise positive)
            u = ca * px + sa * py + w / 2.0
            v = -sa * px + ca * py + h / 2.0
            iu, iv = np.floor(u).astype(int), np.floor(v).astype(int)
            ok = (iu >= 0) & (iu < w) & (iv >= 0) & (iv < h)
            val = np.zeros_like(acc)
            val[ok] = m[iv[ok], iu[ok]]
            acc += val
    acc /= ss * ss
    sel = acc >= thr
    for (yy, xx) in zip(*np.nonzero(sel)):
        X, Y = x0 + xx, y0 + yy
        if 0 <= X < W and 0 <= Y < H:
            img[Y, X] = col
