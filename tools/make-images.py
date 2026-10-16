"""Builds the README screenshot and the GitHub social preview.

Usage: python tools/make-images.py <render.png> <window-shot.png>
render.png: the client area at 200 % from tools/ShotHost (shot-render.png), dark theme
(tests/vm/screenshot-setup.ps1). window-shot.png: a screen shot of the same window at 200 %,
used only for the real title bar above the client area.
Writes docs/screenshot.png (window with rounded corners, transparent outside)
and docs/social-preview.png (1280x640, window centred on a dark background).
"""
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageStat

ROOT = Path(__file__).resolve().parent.parent
RADIUS = 16  # Windows 11 window corner (8 px) at 200 %
PREVIEW = (1280, 640)
PREVIEW_WINDOW_HEIGHT = 540


def rounded(img, radius):
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, img.width - 1, img.height - 1), radius, fill=255)
    out = img.convert("RGBA")
    out.putalpha(mask)
    return out


def background(size):
    w, h = size
    bg = Image.new("RGB", size, (13, 17, 23))
    glow = Image.new("L", size, 0)
    ImageDraw.Draw(glow).ellipse((w * 0.2, h * -0.1, w * 0.8, h * 1.1), fill=110)
    glow = glow.filter(ImageFilter.GaussianBlur(160))
    tint = Image.new("RGB", size, (0, 84, 160))
    return Image.composite(tint, bg, glow)


def shadow(size, window, offset, radius):
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    x, y = offset
    ImageDraw.Draw(layer).rounded_rectangle(
        (x, y + 10, x + window[0], y + window[1] + 10), radius, fill=(0, 0, 0, 170))
    return layer.filter(ImageFilter.GaussianBlur(24))


def title_bar(render, shot):
    """Finds where the client area sits in the screen shot and returns the strip above it."""
    probe = render.crop((0, 0, render.width, 30))
    best = None
    for y in range(20, 120):
        for x in range(0, 40):
            if x + render.width > shot.width:
                break
            d = sum(ImageStat.Stat(ImageChops.difference(shot.crop((x, y, x + render.width, y + 30)), probe)).mean)
            if best is None or d < best[0]:
                best = (d, x, y)
    d, x, y = best
    if d > 2:
        sys.exit("client area not found in the window shot (difference %.1f)" % d)
    # The shot can start a few rows above the window (desktop showing through): begin at the
    # first row that matches the title bar background just below it.
    def row(r):
        return ImageStat.Stat(shot.crop((x, r, x + render.width, r + 1))).median
    top = 0
    while top < 8 and sum(abs(a - b) for a, b in zip(row(top), row(top + 4))) > 6:
        top += 1
    return shot.crop((x, top, x + render.width, y))


def main(render_path, shot_path):
    render = Image.open(render_path).convert("RGB")
    bar = title_bar(render, Image.open(shot_path).convert("RGB"))
    full = Image.new("RGB", (render.width, bar.height + render.height))
    full.paste(bar, (0, 0))
    full.paste(render, (0, bar.height))
    window = rounded(full, RADIUS)
    window.save(ROOT / "docs" / "screenshot.png", optimize=True)

    scale = PREVIEW_WINDOW_HEIGHT / window.height
    small = window.resize((round(window.width * scale), PREVIEW_WINDOW_HEIGHT), Image.LANCZOS)
    pos = ((PREVIEW[0] - small.width) // 2, (PREVIEW[1] - small.height) // 2)
    canvas = background(PREVIEW).convert("RGBA")
    canvas.alpha_composite(shadow(PREVIEW, small.size, pos, round(RADIUS * scale)))
    canvas.alpha_composite(small, pos)
    canvas.convert("RGB").save(ROOT / "docs" / "social-preview.png", optimize=True)
    print("docs/screenshot.png", window.size, "docs/social-preview.png", PREVIEW)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
