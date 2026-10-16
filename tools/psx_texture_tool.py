"""PS1 / PSX texture reduction used for the Utility Plane exterior (v9) and its survival kit.

Takes a detailed source texture (best 4-8x the target size: 4096 -> 512) and makes a PSX-style one:
  1. shading baked into the colour (PS1 textures carried their own shading): linear colour x (0.25 + 0.75 x AO);
  2. block average to the target size in linear space;
  3. thin grooves survive as 1-texel dark lines: darken by (block mean AO - 12th percentile AO) x 2.6, at most 0.55;
  4. palette: k-means 64 colours in sRGB on the covered texels;
  5. ordered 4x4 Bayer dither (amplitude 1/24 ~ one level of the PS1 5-bit-per-channel frame buffer) before the
     nearest-palette pick; --dither-mode two (v12, the plane's interior): Bayer dither between the two nearest palette
     colours instead - a flat area stays flat, gradients mix (for surfaces seen up close: the default mode turns a flat
     dark panel into a checkerboard);
  6. mask: metal 0/1, smoothness in 4 steps x 0.55 (duller), occlusion = 1 (already in the colour); no normal map;
  7. padding: texels outside the UV islands take the nearest covered colour (no mip bleeding).
Optional, for surfaces seen up close (the plane's interior, v12 edition 2, used both with --dither-mode two, 32 colours):
  --contrast A: local contrast before the palette - log luminance deviations from their blur (sigma 1.2 texels,
     inside the covered texels) are multiplied by 1 + A (interior: 0.6);
  --grain G: every texel its own brightness, x exp(G x N(0, 1)), clipped at 2.2 sigma (interior: 0.09) - plain dark
     paint then still shows its texels as blocks. Both 0 by default (the exterior recipe, unchanged).

python3 psx_texture_tool.py --basecolor T_X_BaseColor.png [--mask T_X_MaskMap.png | --ao T_X_AO.png]
                            [--coverage mask.png] [--size 512] [--colors 64] [--dither 1] [--dither-mode naive|two]
                            [--contrast 0.6] [--grain 0.09] --out OUTDIR [--name T_X]
--mask: Unity-style packed map (R metallic, G occlusion, A smoothness). --ao: a grey AO map instead.
--coverage: white = inside UV islands (optional; default: every texel).
Needs numpy, scipy, Pillow. The plane used variant b: --size 512 --colors 64 --dither 1 (small props: --size 256).
Unity import of the result: Filter Point, Compression None, mipmaps on, aniso 0, sRGB on (MaskMap: sRGB off)."""
import argparse, os, zlib
import numpy as np
from PIL import Image
from scipy import ndimage

Image.MAX_IMAGE_PIXELS = None
BAYER4 = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], np.float32) + 0.5) / 16.0


def s2l(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def l2s(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def blocks(a, f):
    """(H, W[, C]) -> (H/f, W/f, f*f[, C]) block view"""
    H, W = a.shape[:2]
    rest = a.shape[2:]
    b = a.reshape(H // f, f, W // f, f, *rest).swapaxes(1, 2)
    return b.reshape(H // f, W // f, f * f, *rest)


def kmeans(x, k, iters=18, seed=0):
    rng = np.random.default_rng(seed)
    c = x[rng.choice(len(x), k, replace=False)].copy()
    for _ in range(iters):
        d = ((x[:, None, :] - c[None, :, :]) ** 2).sum(-1)
        lab = d.argmin(1)
        for j in range(k):
            m = lab == j
            c[j] = x[m].mean(0) if m.any() else x[rng.integers(len(x))]
    return c


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--basecolor', required=True)
    ap.add_argument('--mask')
    ap.add_argument('--ao')
    ap.add_argument('--coverage')
    ap.add_argument('--size', type=int, default=512)
    ap.add_argument('--colors', type=int, default=64)
    ap.add_argument('--dither', type=float, default=1.0)
    ap.add_argument('--dither-mode', choices=('naive', 'two'), default='naive')
    ap.add_argument('--contrast', type=float, default=0.0)
    ap.add_argument('--grain', type=float, default=0.0)
    ap.add_argument('--out', required=True)
    ap.add_argument('--name')
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    name = a.name or os.path.basename(a.basecolor).replace('_BaseColor', '').rsplit('.', 1)[0]
    bc = np.asarray(Image.open(a.basecolor).convert('RGB'), np.float32) / 255
    H, W = bc.shape[:2]
    assert H == W and H % a.size == 0, 'square source, a multiple of the target size'
    f = H // a.size
    if a.mask:
        mk = np.asarray(Image.open(a.mask).convert('RGBA'), np.float32) / 255
    else:
        mk = np.zeros((H, W, 4), np.float32)
        mk[..., 1] = 1.0
        mk[..., 3] = 0.3
    if a.ao:
        mk[..., 1] = np.asarray(Image.open(a.ao).convert('L'), np.float32) / 255
    cov = None
    if a.coverage:
        cov = np.asarray(Image.open(a.coverage).convert('L'), np.float32) / 255 > 0.5
    lin = s2l(bc)
    ao = mk[..., 1]
    shaded = lin * (0.25 + 0.75 * ao)[..., None]                   # 1. shading baked in
    col = blocks(shaded, f).mean(2)                                 # 2. block mean (linear)
    gb = blocks(ao, f)                                              # 3. grooves -> 1-texel lines
    line = np.clip((gb.mean(2) - np.percentile(gb, 12, axis=2)) * 2.6, 0, 0.55)
    col = col * (1 - line)[..., None]
    metal = (blocks(mk[..., 0], f).mean(2) > 0.5).astype(np.float32)
    smooth = np.round(blocks(mk[..., 3], f).mean(2) * 0.55 * 3) / 3
    covered = np.ones(col.shape[:2], bool) if cov is None else blocks(cov, f).mean(2) > 0.25
    if a.contrast:                                                  # optional: local contrast (close-up surfaces)
        L = np.log(np.maximum((col * np.array([0.2126, 0.7152, 0.0722], np.float32)).sum(-1), 1e-5))
        w = covered.astype(np.float32)
        Lb = ndimage.gaussian_filter(L * w, 1.2) / np.maximum(ndimage.gaussian_filter(w, 1.2), 1e-4)
        col = col * np.exp(np.clip(a.contrast * (L - Lb), -0.9, 0.9))[..., None]
    if a.grain:                                                     # optional: per-texel grain
        jit = np.clip(np.random.default_rng(zlib.crc32(name.encode())).normal(0, 1, col.shape[:2]), -2.2, 2.2)
        col = col * np.exp(a.grain * jit)[..., None]
    s = l2s(col).reshape(-1, 3)                                     # 4. palette in sRGB
    samp = s[covered.reshape(-1)]
    samp = samp[np.random.default_rng(1).choice(len(samp), min(len(samp), 60000), replace=False)]
    pal = kmeans(samp, a.colors)
    n = col.shape[0]
    if a.dither and a.dither_mode == 'two':                         # 5. ordered dither between 2 nearest colours
        bay = np.tile(BAYER4, (n // 4, n // 4)).reshape(-1)
        idx = np.empty(len(s), np.int32)
        for i in range(0, len(s), 65536):
            c = s[i:i + 65536]
            o = np.argsort(((c[:, None, :] - pal[None]) ** 2).sum(-1), axis=1)[:, :2]
            p1, p2 = pal[o[:, 0]], pal[o[:, 1]]
            seg = p2 - p1
            t = np.clip(((c - p1) * seg).sum(1) / np.maximum((seg * seg).sum(1), 1e-9), 0, 1)
            idx[i:i + 65536] = np.where(t > bay[i:i + 65536], o[:, 1], o[:, 0])
    else:
        if a.dither:                                                # 5. ordered dither (exterior, v9)
            bay = np.tile(BAYER4, (n // 4, n // 4)).reshape(-1, 1)
            s_d = np.clip(s + (bay - 0.5) * (1.0 / 24.0) * a.dither, 0, 1)
        else:
            s_d = s
        idx = np.empty(len(s_d), np.int32)
        for i in range(0, len(s_d), 65536):
            idx[i:i + 65536] = ((s_d[i:i + 65536, None, :] - pal[None]) ** 2).sum(-1).argmin(1)
    q = pal[idx].reshape(col.shape)
    if not covered.all():                                           # 7. padding
        ii = ndimage.distance_transform_edt(~covered, return_distances=False, return_indices=True)
        q, metal, smooth = q[ii[0], ii[1]], metal[ii[0], ii[1]], smooth[ii[0], ii[1]]
    Image.fromarray((q * 255 + 0.5).astype(np.uint8), 'RGB').save(os.path.join(a.out, f'{name}_BaseColor.png'))
    mask = np.dstack([metal, np.ones_like(metal), np.ones_like(metal), smooth])    # 6. mask
    Image.fromarray((mask * 255 + 0.5).astype(np.uint8), 'RGBA').save(os.path.join(a.out, f'{name}_MaskMap.png'))
    print(name, a.size, 'px,', len(np.unique(idx)), 'colours, dither', a.dither, a.dither_mode,
          'contrast', a.contrast, 'grain', a.grain)


if __name__ == '__main__':
    main()
