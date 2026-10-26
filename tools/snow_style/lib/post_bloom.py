"""EXR (linear) -> PNG with the same post as the URP previews (URP-like bloom threshold 1, knee 0.5, intensity 0.25,
scatter 0.7) and sRGB. python3 post.py in.exr out.png [bloom]"""
import sys, numpy as np, OpenEXR
from PIL import Image
from scipy.ndimage import gaussian_filter
f = OpenEXR.File(sys.argv[1])
ch = f.parts[0].channels
col = np.asarray(ch['RGBA'].pixels if 'RGBA' in ch else ch['RGB'].pixels, np.float64)[..., :3]
bloom = float(sys.argv[3]) if len(sys.argv) > 3 else 0.25
exposure = float(sys.argv[4]) if len(sys.argv) > 4 else 0.0
col = col * 2.0 ** exposure
H, W = col.shape[:2]
if bloom > 0:
    br = np.maximum(col.max(-1), 1e-6); knee = 0.5
    soft = np.clip(br - 1 + knee, 0, 2 * knee) ** 2 / (4 * knee + 1e-5)
    pre = col * (np.maximum(soft, br - 1) / br)[..., None]
    pre = np.minimum(pre, 40.0)                       # URP bloom 'Clamp': bright cores stay bounded
    acc = np.zeros_like(col); wsum = 0
    for k in range(1, 8):
        s = 2.0 ** k * (2 * H / 1080.0)          # same pixel radii as the twin (it blooms at 2x resolution)
        w = 0.7 ** (k - 1)
        acc += np.stack([gaussian_filter(pre[..., c], s / 2, mode='nearest') for c in range(3)], -1) * w
        wsum += w
    col = col + bloom * acc / wsum
img = np.clip(col, 0, 1)
srgb = np.where(img <= 0.0031308, 12.92 * img, 1.055 * np.power(img, 1 / 2.4) - 0.055)
Image.fromarray(np.round(srgb * 255).astype(np.uint8)).save(sys.argv[2])
print('post', sys.argv[2], 'max lin %.2f' % col.max())
