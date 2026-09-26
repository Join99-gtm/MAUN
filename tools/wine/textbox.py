#!/usr/bin/env python3
"""Bounding boxes of text-coloured pixels inside rough regions of a meme.
Usage: textbox.py image x0 y0 x1 y1 mode [thr]
  mode: white  -> pixels with all channels > thr (default 225)
        dark   -> pixels with max channel < thr (default 90)
        blue   -> blue-ish (b > r+60 and b > g+20)
Prints the tight bbox and the connected components (x0 y0 x1 y1 pixels) above 20 px."""
import sys
import numpy as np
from PIL import Image

img = np.asarray(Image.open(sys.argv[1]).convert("RGB")).astype(int)
x0, y0, x1, y1 = map(int, sys.argv[2:6])
mode = sys.argv[6]
thr = int(sys.argv[7]) if len(sys.argv) > 7 else None
reg = img[y0:y1, x0:x1]
r, g, b = reg[:, :, 0], reg[:, :, 1], reg[:, :, 2]
if mode == "white":
    m = (r > (thr or 225)) & (g > (thr or 225)) & (b > (thr or 225))
elif mode == "dark":
    m = np.maximum(np.maximum(r, g), b) < (thr or 90)
elif mode == "blue":
    m = (b > r + 60) & (b > g + 20)
else:
    raise SystemExit("mode?")
ys, xs = np.nonzero(m)
if len(xs) == 0:
    print("nothing")
    raise SystemExit
print("bbox", x0 + xs.min(), y0 + ys.min(), x0 + xs.max(), y0 + ys.max(), "px", len(xs))
# row profile: which rows have text (to split lines)
rows = m.sum(axis=1)
runs, start = [], None
for i, v in enumerate(rows):
    if v > 0 and start is None:
        start = i
    if v == 0 and start is not None:
        runs.append((start, i - 1)); start = None
if start is not None:
    runs.append((start, len(rows) - 1))
for a, bb in runs:
    cols = np.nonzero(m[a:bb + 1].sum(axis=0))[0]
    print("  line y", y0 + a, "-", y0 + bb, " x", x0 + cols.min(), "-", x0 + cols.max())
