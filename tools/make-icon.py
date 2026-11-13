"""Draws the app icon (src/Shutdown/Assets/Shutdown.ico): a white power symbol on a blue rounded tile.

The symbol matches the in-app power glyph: a ring open at the top with round ends, and a short stem in the
gap that stays clear of the ring. Small frames get a heavier, pixel-aligned stroke so they stay crisp in
the title bar. Usage: python tools/make-icon.py [out.ico]
"""
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image

SS = 16  # supersampling factor
TOP = np.array([0x1A, 0x94, 0xFF], float)
BOTTOM = np.array([0x00, 0x5F, 0xB8], float)
GAP_DEG = 42  # ring opening either side of the top, measured to the centre of the round end

# size: (ring outer radius, stroke) in px; small frames are tuned so the stem lands on whole pixels.
SMALL = {16: (5.5, 2.0), 20: (7.0, 2.0), 24: (8.0, 2.0), 32: (11.0, 3.0)}


def frame(s):
    ro, w = SMALL.get(s, (s * 0.34, s * 0.095))
    radius = max(3.0, s * 0.2)
    cx = s / 2
    # The symbol spans from the stem tip (1.2 ro above the ring centre) to the ring bottom; centre that span.
    cy = s / 2 + 0.1 * ro
    rm = ro - w / 2

    n = s * SS
    c = (np.arange(n) + 0.5) / SS
    x, y = np.meshgrid(c, c)

    # Rounded tile.
    qx = np.maximum(np.maximum(radius - x, x - (s - radius)), 0)
    qy = np.maximum(np.maximum(radius - y, y - (s - radius)), 0)
    tile = qx * qx + qy * qy <= radius * radius

    # Ring with an opening at the top and round ends.
    ang = np.degrees(np.arctan2(x - cx, cy - y))  # 0 at the top, +-180 at the bottom
    ring = (np.abs(np.hypot(x - cx, y - cy) - rm) <= w / 2) & (np.abs(ang) >= GAP_DEG)
    for side in (-1, 1):
        a = math.radians(GAP_DEG) * side
        ex, ey = cx + rm * math.sin(a), cy - rm * math.cos(a)
        ring |= np.hypot(x - ex, y - ey) <= w / 2

    # Stem with round ends, tip to tip from 1.2 ro above the centre to 0.3 ro above it.
    top, bottom = cy - 1.2 * ro + w / 2, cy - 0.3 * ro - w / 2
    stem = (np.abs(x - cx) <= w / 2) & (y >= top) & (y <= bottom)
    for ey in (top, bottom):
        stem |= np.hypot(x - cx, y - ey) <= w / 2
    glyph = (ring | stem) & tile

    t = (y / s)[..., None]
    rgb = TOP * (1 - t) + BOTTOM * t
    rgb = np.where(glyph[..., None], 255.0, rgb)
    alpha = tile.astype(float) * 255
    img = np.dstack([rgb * (alpha[..., None] / 255), alpha])
    small = img.reshape(s, SS, s, SS, 4).mean(axis=(1, 3))
    # Un-premultiply the averaged colour.
    out = small.copy()
    nz = small[..., 3] > 0
    out[nz, :3] = np.clip(small[nz, :3] * 255 / small[nz, 3:4], 0, 255)
    return Image.fromarray(out.round().astype(np.uint8), 'RGBA')


def clearance(s):
    """Smallest distance in px between the stem and a ring end (must stay visible)."""
    ro, w = SMALL.get(s, (s * 0.34, s * 0.095))
    rm = ro - w / 2
    return rm * math.sin(math.radians(GAP_DEG)) - w


def main():
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent / 'src/Shutdown/Assets/Shutdown.ico'
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    for s in sizes:
        if clearance(s) < 1.0:
            sys.exit(f'{s} px: stem and ring end only {clearance(s):.2f} px apart')
    frames = [frame(s) for s in sizes]
    frames[-1].save(out, format='ICO', sizes=[(s, s) for s in sizes], append_images=frames[:-1])
    print(out)


if __name__ == '__main__':
    main()
