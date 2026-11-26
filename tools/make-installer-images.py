"""Draws the setup wizard images (installer/WizardImage-*.png, installer/WizardSmallImage-*.png).

The large image fills the left side of the welcome and finish pages: the app icon's blue gradient with the white
power symbol. The small image is the app icon itself, shown at the top of the other pages. One file per display
scale, so setup never has to stretch a bitmap. Usage: python tools/make-installer-images.py
"""
import importlib.util
from pathlib import Path

import numpy as np
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
    ss = 4
    xs = (np.arange(width * ss) + 0.5) / ss
    ys = (np.arange(height * ss) + 0.5) / ss
    x, y = np.meshgrid(xs, ys)
    ro = width * 0.26
    w = ro * 0.28
    cx, cy = width / 2, height * 0.42 + 0.1 * ro
    glyph = icon.power_symbol(x, y, cx, cy, ro, w)

    # Diagonal gradient, lighter at the top left, like light falling on the icon tile.
    t = np.clip((y / height) * 0.8 + (x / width) * 0.2, 0, 1)[..., None]
    rgb = icon.TOP * (1 - t) + icon.BOTTOM * t
    rgb = np.where(glyph[..., None], 255.0, rgb)
    rgb = rgb.reshape(height, ss, width, ss, 3).mean(axis=(1, 3))
    return Image.fromarray(rgb.round().astype(np.uint8), 'RGB')


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
