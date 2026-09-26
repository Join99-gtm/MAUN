#!/usr/bin/env python3
"""Finds the Autumn mod's leaf piles on a screenshot: clusters of its four leaf colours.
Usage: leaves.py shot.png  -> prints one line per pile: cx cy pixels minx miny maxx maxy
"""
import sys
import numpy as np
from PIL import Image

COLORS = [(208, 122, 45), (234, 198, 54), (172, 193, 79), (208, 87, 64)]

img = np.asarray(Image.open(sys.argv[1]).convert("RGB")).astype(int)
mask = np.zeros(img.shape[:2], dtype=bool)
for c in COLORS:
    mask |= (np.abs(img - np.array(c)).sum(axis=2) <= 12)

ys, xs = np.nonzero(mask)
# cluster on a coarse grid: 24 px cells, flood-fill neighbouring cells
cell = 24
cells = {}
for x, y in zip(xs, ys):
    cells.setdefault((x // cell, y // cell), []).append((x, y))
seen = set()
piles = []
for start in cells:
    if start in seen:
        continue
    stack, pts = [start], []
    seen.add(start)
    while stack:
        cx, cy = stack.pop()
        pts.extend(cells[(cx, cy)])
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                n = (cx + dx, cy + dy)
                if n in cells and n not in seen:
                    seen.add(n)
                    stack.append(n)
    if len(pts) >= 40:
        a = np.array(pts)
        piles.append((int(a[:, 0].mean()), int(a[:, 1].mean()), len(pts),
                      int(a[:, 0].min()), int(a[:, 1].min()), int(a[:, 0].max()), int(a[:, 1].max())))
for p in sorted(piles, key=lambda p: -p[2]):
    print(*p)
