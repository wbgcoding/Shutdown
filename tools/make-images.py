"""Builds the README screenshot and the GitHub social preview.

Usage: python tools/make-images.py <render.png>
render.png: the whole window at 200 % from tools/ShotHost (shot-render.png), dark theme
(tests/vm/screenshot-setup.ps1). The app draws its own title bar, so the render already shows it.
Writes docs/screenshot.png (window with rounded corners, transparent outside)
and docs/social-preview.png (1280x640: app icon, name and a short description beside the window,
on a dark background).
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
RADIUS = 16  # Windows 11 window corner (8 px) at 200 %
PREVIEW = (1280, 640)
PREVIEW_WINDOW_HEIGHT = 540
ICON = ROOT / "src" / "Shutdown" / "Assets" / "Shutdown.ico"
FONTS = Path("C:/Windows/Fonts")
NAME = "Shutdown"
DESCRIPTION = ["Shut down, restart, hibernate or sleep", "after a countdown or at a set time."]
FOOTNOTE = "For Windows 10 and 11"
GAP = 88  # between the text column and the window


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


def app_icon(size):
    with Image.open(ICON) as ico:
        ico.size = max(ico.info["sizes"])
        return ico.convert("RGBA").resize((size, size), Image.LANCZOS)


def text_column():
    """Icon, name and description, left-aligned; returns the drawing steps and the column size."""
    name_font = ImageFont.truetype(str(FONTS / "seguisb.ttf"), 68)
    body_font = ImageFont.truetype(str(FONTS / "segoeui.ttf"), 30)
    note_font = ImageFont.truetype(str(FONTS / "segoeui.ttf"), 24)
    icon = app_icon(112)
    steps, y = [("image", icon, 0)], icon.height + 30
    for text, font, colour, space in [(NAME, name_font, (240, 244, 248), 18)] + \
            [(line, body_font, (200, 208, 218), 8) for line in DESCRIPTION] + [(FOOTNOTE, note_font, (128, 140, 154), 0)]:
        if text == FOOTNOTE:
            y += 20
        top, bottom = font.getbbox("Hg")[1], font.getbbox("Hg")[3]
        steps.append(("text", (text, font, colour), y - top))
        y += bottom - top + space
    width = max([icon.width] + [round(f.getlength(t)) for kind, (t, f, _), _ in
                                 [s for s in steps if s[0] == "text"]])
    return steps, (width, y)


def main(render_path):
    window = rounded(Image.open(render_path).convert("RGB"), RADIUS)
    window.save(ROOT / "docs" / "screenshot.png", optimize=True)

    scale = PREVIEW_WINDOW_HEIGHT / window.height
    small = window.resize((round(window.width * scale), PREVIEW_WINDOW_HEIGHT), Image.LANCZOS)
    steps, column = text_column()
    left = (PREVIEW[0] - column[0] - GAP - small.width) // 2
    pos = (left + column[0] + GAP, (PREVIEW[1] - small.height) // 2)
    canvas = background(PREVIEW).convert("RGBA")
    canvas.alpha_composite(shadow(PREVIEW, small.size, pos, round(RADIUS * scale)))
    canvas.alpha_composite(small, pos)

    draw = ImageDraw.Draw(canvas)
    top = (PREVIEW[1] - column[1]) // 2
    for kind, item, y in steps:
        if kind == "image":
            canvas.alpha_composite(item, (left, top + y))
        else:
            text, font, colour = item
            draw.text((left, top + y), text, font=font, fill=colour)
    canvas.convert("RGB").save(ROOT / "docs" / "social-preview.png", optimize=True)
    print("docs/screenshot.png", window.size, "docs/social-preview.png", PREVIEW)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
