"""Regenerates the Gnoseis app icon images from the logo geometry.

Writes the SVG master and one SVG per size into this folder, renders each size
to PNG with Inkscape, packs the PNGs into ../Gnoseis.ico and copies the title
bar sizes to ../TitleBarIcon.scale-*.png. See README.md.

Usage:  python make_icons.py [path to inkscape.com]
Needs only the Python standard library and Inkscape.
"""

import shutil
import struct
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ASSETS = HERE.parent
INKSCAPE = sys.argv[1] if len(sys.argv) > 1 else r"C:\Program Files\Inkscape\bin\inkscape.com"

GOLD = "#E8B86B"  # gnoseis_accent
NAVY = "#0B0D14"  # gnoseis_background

# The graph mark, in the coordinates of Android's gnoseis_logo.xml before its
# translate(-11, -8): a 46 x 46 box starting at (11, 8).
EDGES = [((18, 20), (46, 16)), ((18, 20), (30, 46)), ((46, 16), (30, 46)),
         ((46, 16), (52, 40)), ((30, 46), (52, 40))]
NODES = [(18, 20, 6.5), (46, 16, 5), (30, 46, 8), (52, 40, 4)]
EDGE_WIDTH = 3.2
EDGE_OPACITY = 0.7

ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
TUNED_SIZES = {16, 20, 24}  # adjusted for the pixel grid, see README.md
TITLE_BAR = {100: 16, 125: 20, 150: 24, 200: 32, 400: 64}  # scale % -> px


def fmt(v):
    return f"{v:.3f}".rstrip("0").rstrip(".")


def icon_svg(size, tuned):
    # Tile: margin of 2/48 of the icon, rounded to whole pixels so its edges stay sharp.
    margin = round(size / 24)
    tile = size - 2 * margin
    radius = tile * 8 / 44
    # Mark: the 46 x 46 logo box at two thirds of the tile's width, centred, as on Android.
    box = tile * 2 / 3
    scale = box / 46
    ox = (size - box) / 2 - 11 * scale
    oy = (size - box) / 2 - 8 * scale
    edge = EDGE_WIDTH * scale
    snap = (lambda v: round(v * 2) / 2) if tuned else (lambda v: v)
    if tuned:
        edge = max(edge, 1.0)
    centre = {(x, y): (snap(ox + x * scale), snap(oy + y * scale)) for x, y, _ in NODES}
    path = "".join(f"M{fmt(centre[a][0])},{fmt(centre[a][1])}L{fmt(centre[b][0])},{fmt(centre[b][1])}"
                   for a, b in EDGES)
    circles = "\n".join(
        f'  <circle cx="{fmt(centre[(x, y)][0])}" cy="{fmt(centre[(x, y)][1])}" r="{fmt(r * scale)}" fill="{GOLD}"/>'
        for x, y, r in NODES)
    note = "hand-tuned for the pixel grid" if tuned else "generated"
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 {size} {size}">
  <!-- Gnoseis app icon, {size} px, {note} by make_icons.py -->
  <rect x="{margin}" y="{margin}" width="{tile}" height="{tile}" rx="{fmt(radius)}" fill="{NAVY}"/>
  <path d="{path}" fill="none" stroke="{GOLD}" stroke-opacity="{EDGE_OPACITY}" stroke-width="{fmt(edge)}" stroke-linecap="round"/>
{circles}
</svg>
'''


def render(svg, png):
    subprocess.run([INKSCAPE, str(svg), "--export-type=png", f"--export-filename={png}"],
                   check=True, capture_output=True)


def write_ico(pngs, ico):
    # ICO with every image stored as a 32-bit PNG.
    data = [p.read_bytes() for _, p in pngs]
    out = struct.pack("<HHH", 0, 1, len(pngs))
    offset = 6 + 16 * len(pngs)
    for (size, _), d in zip(pngs, data):
        dim = 0 if size >= 256 else size
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(d), offset)
        offset += len(d)
    ico.write_bytes(out + b"".join(data))


def main():
    sizes_dir = HERE / "sizes"
    png_dir = HERE / "png"  # build output, not committed
    sizes_dir.mkdir(exist_ok=True)
    png_dir.mkdir(exist_ok=True)

    pngs = []
    for size in ICO_SIZES:
        svg = HERE / "gnoseis-icon.svg" if size == 256 else sizes_dir / f"gnoseis-icon-{size}.svg"
        svg.write_text(icon_svg(size, size in TUNED_SIZES), encoding="utf-8")
        png = png_dir / f"gnoseis-icon-{size}.png"
        render(svg, png)
        pngs.append((size, png))

    write_ico(pngs, ASSETS / "Gnoseis.ico")
    for scale, size in TITLE_BAR.items():
        shutil.copyfile(png_dir / f"gnoseis-icon-{size}.png", ASSETS / f"TitleBarIcon.scale-{scale}.png")
    print("Wrote", ASSETS / "Gnoseis.ico", "and the title bar images")


if __name__ == "__main__":
    main()
