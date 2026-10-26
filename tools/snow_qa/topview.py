"""Snow QA: a top-down orthographic picture of the open scene (edit mode), with a metre grid drawn over it.

    python tools/snow_qa/topview.py <x> <z> <size_m> <out.png> [--px 1600] [--grid 10] [--y 400]

The render raises the lighting for itself only (flat ambient, no fog, volumes off) and restores it (topview.cs).
"""
import argparse, math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ue

ap = argparse.ArgumentParser()
ap.add_argument('x', type=float); ap.add_argument('z', type=float); ap.add_argument('size', type=float); ap.add_argument('out')
ap.add_argument('--px', type=int, default=1600); ap.add_argument('--grid', type=float, default=10.0); ap.add_argument('--y', type=float, default=400.0)
a = ap.parse_args()
out = os.path.abspath(a.out)
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'topview.cs'), encoding='utf-8').read()
args = f'x={a.x};z={a.z};size={a.size};px={a.px};y={a.y};out={out}'
print(ue.run(src.replace('ARGS', args.replace('\\', '\\\\')), timeout=300))
if a.grid > 0 and os.path.exists(out):
    from PIL import Image, ImageDraw, ImageFont
    img = Image.open(out).convert('RGB')
    d = ImageDraw.Draw(img)
    k = a.px / a.size
    try:
        f = ImageFont.truetype('arial.ttf', 13)
    except OSError:
        f = ImageFont.load_default()
    x0, z1 = a.x - a.size / 2, a.z + a.size / 2
    g = a.grid
    for gx in range(int(math.ceil(x0 / g)), int((x0 + a.size) / g) + 1):
        X = (gx * g - x0) * k
        d.line([X, 0, X, a.px], fill=(255, 255, 0), width=1)
        d.text((X + 2, 2), f'{gx * g:g}', fill=(255, 255, 0), font=f)
    for gz in range(int(math.ceil((z1 - a.size) / g)), int(z1 / g) + 1):
        Y = (z1 - gz * g) * k
        d.line([0, Y, a.px, Y], fill=(255, 255, 0), width=1)
        d.text((2, Y + 2), f'{gz * g:g}', fill=(255, 255, 0), font=f)
    img.save(out)
