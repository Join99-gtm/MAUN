#!/usr/bin/env python3
"""Finds the goose on a screenshot of an otherwise plain grey desktop: the biggest cluster of pure white.
Prints: cx cy pixels"""
import sys
import numpy as np
from PIL import Image

img = np.asarray(Image.open(sys.argv[1]).convert("RGB")).astype(int)
white = (img[:, :, 0] > 245) & (img[:, :, 1] > 245) & (img[:, :, 2] > 245)
white[:60, :200] = False  # Wine's tray window
ys, xs = np.nonzero(white)
if len(xs) == 0:
    sys.exit(1)
cell = 16
cells = {}
for x, y in zip(xs, ys):
    cells.setdefault((x // cell, y // cell), []).append((x, y))
seen, best = set(), []
for start in cells:
    if start in seen:
        continue
    stack, pts = [start], []
    seen.add(start)
    while stack:
        c = stack.pop()
        pts.extend(cells[c])
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                n = (c[0] + dx, c[1] + dy)
                if n in cells and n not in seen:
                    seen.add(n)
                    stack.append(n)
    if len(pts) > len(best):
        best = pts
a = np.array(best)
print(int(a[:, 0].mean()), int(a[:, 1].mean()), len(best))
