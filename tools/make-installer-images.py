"""Draws the setup wizard images (installer/WizardImage-*.png, installer/WizardSmallImage-*.png).

The large image fills the left side of the welcome and finish pages: the app icon on white, matching the
standard light wizard. The small image is the app icon itself, shown at the top of the other pages. One file per display
scale, so setup never has to stretch a bitmap. Usage: python tools/make-installer-images.py
"""
import importlib.util
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('make_icon', ROOT / 'tools' / 'make-icon.py')
icon = importlib.util.module_from_spec(spec)
spec.loader.exec_module(icon)

SCALES = [100, 125, 150, 175, 200, 225, 250]
# Wizard image area at 100 % display scale with the default 120 % wizard size.
PANEL = (197, 377)
SMALL = 66


def panel(width, height):
    # White like the rest of the wizard, with the app icon in the upper half.
    img = Image.new('RGB', (width, height), (255, 255, 255))
    size = round(width * 0.5)
    tile = icon.frame(size)
    img.paste(tile, ((width - size) // 2, round(height * 0.38) - size // 2), tile)
    return img


def main():
    out = ROOT / 'installer'
    for scale in SCALES:
        width, height = (round(v * scale / 100) for v in PANEL)
        panel(width, height).save(out / f'WizardImage-{scale}.png')
        size = round(SMALL * scale / 100)
        icon.frame(size).save(out / f'WizardSmallImage-{scale}.png')
    print(out)


if __name__ == '__main__':
    main()
