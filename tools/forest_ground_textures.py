"""Forest ground textures (Snow, 21.10.2026, research FOREST_RESEARCH_2_2026-10-21.md section 5).

Draws the eight Unity Terrain layers of the Forest as PS1-style pixel art, 64 x 64, seamless, at 32 px/m (one tile = 2 m):
  1. a seamless band-limited noise field per layer (FFT, wraps), shaped by the layer's recipe;
  2. features stamped with wrap-around: needles (short bright strokes), cones, pebbles (lit top-left, dark bottom-right),
     cracks (random walks), lichen and moss spots, puddle hollows in the ruts;
  3. a small palette per layer (5-7 colours) picked with an ordered 4 x 4 Bayer dither - few colours, wide value range
     (luminance sd ~20-30 / 255 instead of 3-8 in the old tiles) and a value hierarchy that reads at night in fog:
     road gravel the lightest strip, trail soil a step darker, forest floor dark with bright needle specks, the floor
     under crowns darker still, ruts dark and wet;
  4. a URP terrain mask per layer: R metallic 0, G occlusion (features shade their surroundings), B height (pebbles and
     stones up, cracks and hollows down - the height blend uses it), A smoothness (wet ruts and puddles shine in a torch).

python tools/forest_ground_textures.py --out Snow/Assets/Locations/Forest/Terrain/Ground [--preview preview.png]
Unity import (set by ForestTerrainBaker): albedo sRGB, Bilinear, aniso 8, mipmaps, no compression; mask linear.
Needs numpy and Pillow."""
import argparse, os
import numpy as np
from PIL import Image

N = 64
BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], float) / 16.0 + 1.0 / 32.0


def noise(rng, lo, hi, power=1.0):
    """Seamless noise, frequencies lo..hi cycles per tile, normalised to 0..1."""
    f = np.fft.fftfreq(N) * N
    fx, fy = np.meshgrid(f, f)
    r = np.sqrt(fx * fx + fy * fy)
    spec = np.fft.fft2(rng.standard_normal((N, N))) * ((r >= lo) & (r <= hi)) / np.maximum(r, 1.0) ** power
    n = np.real(np.fft.ifft2(spec))
    n -= n.min()
    return n / max(n.max(), 1e-9)


def stamp(arr, x, y, value, op='set'):
    x %= N
    y %= N
    if op == 'set':
        arr[y, x] = value
    elif op == 'max':
        arr[y, x] = max(arr[y, x], value)
    elif op == 'min':
        arr[y, x] = min(arr[y, x], value)


def quantize(v, n):
    """Value field 0..1 -> palette index 0..n-1 with the 4 x 4 Bayer threshold."""
    ys, xs = np.mgrid[0:N, 0:N]
    t = BAYER[ys % 4, xs % 4]
    return np.clip(np.floor(v * (n - 1) + t), 0, n - 1).astype(int)


class Layer:
    def __init__(self, name, palette, seed):
        self.name = name
        self.pal = np.array(palette, float)
        self.rng = np.random.default_rng(seed)
        self.idx = np.zeros((N, N), int)          # palette index
        self.h = np.full((N, N), 0.5)             # height 0..1
        self.ao = np.ones((N, N))
        self.smooth = np.full((N, N), 0.04)

    def base(self, v, n=None):
        n = n or len(self.pal)
        self.idx = quantize(np.clip(v, 0, 1), n)

    def needles(self, count, colours, length=(2, 4), height=0.62):
        r = self.rng
        dirs = [(1, 0), (0, 1), (1, 1), (1, -1)]
        for _ in range(count):
            x, y = r.integers(0, N, 2)
            dx, dy = dirs[r.integers(0, 4)]
            c = colours[r.integers(0, len(colours))]
            for k in range(r.integers(length[0], length[1] + 1)):
                stamp(self.idx, x + dx * k, y + dy * k, c)
                stamp(self.h, x + dx * k, y + dy * k, height, 'max')

    def blobs(self, count, size, light, mid, dark, height=0.85, ao_ring=0.75):
        """Pebbles / cones / stones: lit top-left, shadow bottom-right, darker ring of occlusion around."""
        r = self.rng
        for _ in range(count):
            x, y = r.integers(0, N, 2)
            s = r.integers(size[0], size[1] + 1)
            for j in range(s):
                for i in range(s):
                    if s >= 3 and (i, j) in ((0, 0), (s - 1, 0), (0, s - 1), (s - 1, s - 1)):
                        continue
                    c = light if i + j < s - 1 else (mid if i + j == s - 1 else dark)
                    stamp(self.idx, x + i, y + j, c)
                    stamp(self.h, x + i, y + j, height - 0.08 * (i + j) / max(s, 1), 'max')
            for j in range(-1, s + 1):
                for i in range(-1, s + 1):
                    if 0 <= i < s and 0 <= j < s:
                        continue
                    stamp(self.ao, x + i, y + j, ao_ring, 'min')
            stamp(self.idx, x + s, y + s - 1, dark)   # cast shadow

    def cracks(self, count, steps, colour, depth=0.1):
        """Thin cracks: a polyline that wanders slowly (heading jitter), one texel wide, occasionally forking."""
        r = self.rng
        for _ in range(count):
            px, py = r.uniform(0, N, 2)
            ang = r.uniform(0, 2 * np.pi)
            for k in range(int(r.integers(steps[0], steps[1] + 1))):
                x, y = int(round(px)), int(round(py))
                stamp(self.idx, x, y, colour)
                stamp(self.h, x, y, depth, 'min')
                stamp(self.ao, x, y, 0.6, 'min')
                ang += r.normal(0, 0.35)
                px += np.cos(ang)
                py += np.sin(ang)

    def spots(self, count, radius, colour, height=None, prob=0.75):
        r = self.rng
        for _ in range(count):
            cx, cy = r.integers(0, N, 2)
            rad = r.uniform(radius[0], radius[1])
            for j in range(-int(rad) - 1, int(rad) + 2):
                for i in range(-int(rad) - 1, int(rad) + 2):
                    if i * i + j * j <= rad * rad and r.random() < prob:
                        stamp(self.idx, cx + i, cy + j, colour)
                        if height is not None:
                            stamp(self.h, cx + i, cy + j, height)

    def images(self):
        rgb = self.pal[self.idx].clip(0, 255).astype(np.uint8)
        m = np.zeros((N, N, 4), np.uint8)
        m[..., 0] = 0
        m[..., 1] = (np.clip(self.ao, 0, 1) * 255).astype(np.uint8)
        m[..., 2] = (np.clip(self.h, 0, 1) * 255).astype(np.uint8)
        m[..., 3] = (np.clip(self.smooth, 0, 1) * 255).astype(np.uint8)
        return Image.fromarray(rgb, 'RGB'), Image.fromarray(m, 'RGBA')


def build():
    L = {}

    # forest floor: needle litter, moss and grass bits; dark base, bright specks (needles), a few cones
    f = Layer('Floor', [(34, 31, 22), (48, 43, 29), (63, 56, 36), (80, 71, 44), (62, 74, 40), (118, 102, 60), (150, 128, 74)], 11)
    v = noise(f.rng, 2, 12) * 0.75 + noise(f.rng, 10, 24) * 0.25
    f.base(v * 0.95, 4)
    f.spots(5, (2.0, 4.0), 4, prob=0.6)                        # moss patches
    f.needles(70, [5, 5, 6, 3], (2, 4))
    f.blobs(3, (2, 3), 5, 3, 0, height=0.8)                    # cones
    f.h = np.maximum(f.h, v * 0.55)
    L['Floor'] = f

    # floor under crowns: deep needles, darker, sparse highlights
    d = Layer('FloorDark', [(24, 21, 16), (34, 29, 21), (45, 38, 26), (58, 48, 32), (92, 74, 44), (118, 94, 54)], 12)
    v = noise(d.rng, 2, 12) * 0.7 + noise(d.rng, 12, 28) * 0.3
    d.base(v * 0.9, 4)
    d.needles(55, [4, 4, 5, 3], (2, 4), height=0.6)
    d.blobs(4, (2, 3), 4, 2, 0, height=0.78)
    d.cracks(3, (6, 12), 0, depth=0.25)                        # roots' shadows
    d.h = np.maximum(d.h, v * 0.5)
    L['FloorDark'] = d

    # road gravel: the lightest strip - pebbles on compacted fines
    g = Layer('Gravel', [(60, 56, 49), (80, 75, 66), (100, 94, 83), (122, 115, 102), (146, 138, 123), (168, 160, 144), (42, 39, 34)], 13)
    v = noise(g.rng, 3, 16)
    g.base(0.15 + v * 0.55, 6)
    g.blobs(55, (1, 2), 5, 3, 6, height=0.8, ao_ring=0.85)
    g.blobs(14, (2, 3), 4, 2, 6, height=0.9, ao_ring=0.8)
    g.h = np.maximum(g.h, v * 0.45)
    g.smooth[:] = 0.02
    L['Gravel'] = g

    # trail soil: brown path with grass bits and small stones
    s = Layer('Soil', [(46, 37, 27), (62, 50, 35), (80, 65, 45), (100, 82, 57), (124, 102, 70), (64, 76, 40), (40, 32, 24)], 14)
    v = noise(s.rng, 2, 14)
    s.base(0.1 + v * 0.8, 5)
    s.needles(22, [5, 5, 4], (1, 3), height=0.55)              # grass blades
    s.blobs(10, (1, 2), 4, 2, 6, height=0.75)
    s.cracks(2, (5, 10), 6, depth=0.3)
    s.h = np.maximum(s.h, v * 0.5)
    L['Soil'] = s

    # rock: granite with lichen and cracks
    r = Layer('Rock', [(46, 46, 44), (66, 66, 63), (88, 87, 83), (112, 110, 104), (138, 135, 127), (92, 102, 74), (120, 128, 92)], 15)
    v = noise(r.rng, 2, 10) * 0.6 + noise(r.rng, 10, 30) * 0.4
    r.base(0.1 + v * 0.85, 5)
    r.spots(7, (1.5, 3.5), 5, prob=0.7)
    r.spots(4, (1.0, 2.0), 6, prob=0.6)
    r.cracks(5, (14, 30), 0, depth=0.05)
    r.h = 0.35 + v * 0.6
    for y in range(N):
        for x in range(N):
            if r.idx[y, x] == 0:
                r.h[y, x] = 0.05
    r.smooth[:] = 0.12
    L['Rock'] = r

    # ruts: dark compacted, wet, with puddle hollows that shine
    u = Layer('Ruts', [(30, 28, 25), (42, 39, 34), (55, 51, 44), (70, 65, 56), (26, 27, 28), (62, 65, 68)], 16)
    v = noise(u.rng, 2, 10)
    u.base(0.1 + v * 0.75, 4)
    u.blobs(18, (1, 2), 3, 2, 0, height=0.7)
    u.h = 0.3 + v * 0.5
    wet = np.zeros((N, N), bool)
    for _ in range(1):                                         # one puddle per 2 m tile (no-tile offsets scatter it)
        cx, cy = u.rng.integers(0, N, 2)
        rx, ry = u.rng.uniform(4.0, 6.5), u.rng.uniform(2.2, 3.5)
        ys, xs = np.mgrid[0:N, 0:N]
        dx = (xs - cx + N // 2) % N - N // 2
        dy = (ys - cy + N // 2) % N - N // 2
        wet |= (dx / rx) ** 2 + (dy / ry) ** 2 <= 1.0
    u.idx[wet] = 4
    u.h[wet] = 0.05
    u.smooth = np.where(wet, 0.55, 0.3)
    edge = wet & ~np.roll(wet, 1, 0)
    u.idx[edge & (u.rng.random((N, N)) < 0.25)] = 5             # sparse sky glints on the near rim
    L['Ruts'] = u

    # trail track: the worn middle of the trail, darker and smoother
    t = Layer('Track', [(38, 31, 23), (52, 42, 30), (68, 55, 39), (86, 70, 49), (30, 25, 19)], 17)
    v = noise(t.rng, 2, 12)
    t.base(0.02 + v * 0.98, 4)
    t.blobs(16, (1, 2), 3, 1, 4, height=0.7)
    t.cracks(3, (8, 16), 4, depth=0.2)
    t.h = 0.3 + v * 0.45
    t.smooth[:] = 0.18
    L['Track'] = t

    # asphalt: worn grey with cracks and patches
    a = Layer('Asphalt', [(40, 40, 40), (54, 54, 53), (68, 68, 66), (84, 83, 80), (100, 99, 95), (26, 26, 26)], 18)
    v = noise(a.rng, 3, 20)
    a.base(0.1 + v * 0.8, 5)
    a.spots(3, (3.0, 6.0), 1, prob=0.95)                       # patched areas
    a.cracks(6, (16, 34), 5, depth=0.1)
    a.blobs(10, (1, 1), 4, 3, 5, height=0.7)
    a.h = np.minimum(a.h, 0.5) + v * 0.2
    a.smooth[:] = 0.15
    L['Asphalt'] = a
    return L


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--preview')
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    layers = build()
    tiles = []
    for name, lay in layers.items():
        alb, mask = lay.images()
        alb.save(os.path.join(args.out, f'T_GR_{name}_A.png'))
        mask.save(os.path.join(args.out, f'T_GR_{name}_M.png'))
        lum = np.asarray(alb.convert('L'), float)
        print(f'{name:10s} mean L {lum.mean():5.1f}  sd {lum.std():5.1f}  colours {len(np.unique(np.asarray(alb).reshape(-1, 3), axis=0))}')
        tiles.append(alb)
    if args.preview:
        # each layer tiled 3 x 3 and scaled x4 (nearest), in two rows of four
        cell = N * 3 * 4
        sheet = Image.new('RGB', (cell * 4 + 30, cell * 2 + 10), (12, 12, 12))
        for i, alb in enumerate(tiles):
            big = Image.new('RGB', (N * 3, N * 3))
            for j in range(3):
                for k in range(3):
                    big.paste(alb, (k * N, j * N))
            sheet.paste(big.resize((cell, cell), Image.NEAREST), ((i % 4) * (cell + 10), (i // 4) * (cell + 10)))
        sheet.save(args.preview)


if __name__ == '__main__':
    main()
