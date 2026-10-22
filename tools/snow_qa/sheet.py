"""Snow QA: contact sheet of screenshots (file names as labels) - one image to look at instead of many.

    python sheet.py <out.jpg> <columns> <tile_width_px> img1 img2 ...
"""
import os, sys
from PIL import Image, ImageDraw


def sheet(out, cols, w, files):
    ims = []
    for f in files:
        im = Image.open(f).convert('RGB')
        im = im.resize((w, int(im.height * w / im.width)))
        d = ImageDraw.Draw(im)
        d.rectangle([0, 0, w, 16], fill=(0, 0, 0))
        d.text((3, 2), os.path.splitext(os.path.basename(f))[0], fill=(255, 255, 0))
        ims.append(im)
    h = ims[0].height
    s = Image.new('RGB', (cols * w, ((len(ims) + cols - 1) // cols) * h), (30, 30, 30))
    for i, im in enumerate(ims):
        s.paste(im, ((i % cols) * w, (i // cols) * h))
    s.save(out, quality=85)
    return out


if __name__ == '__main__':
    print(sheet(sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4:]))
