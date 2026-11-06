# App icon sources

The app icon is the gnoseis.org logo, as on Gnoseis for Android: the gold graph mark
(`#E8B86B`, edges at 70 % opacity) centred on a navy (`#0B0D14`) rounded square.

| File | What it is |
|---|---|
| `make_icons.py` | Writes the SVGs below from the logo geometry, renders them and builds the icon files |
| `gnoseis-icon.svg` | The master, 256 × 256 |
| `sizes/gnoseis-icon-<n>.svg` | One SVG per smaller size in the `.ico` |

`make_icons.py` writes `../Gnoseis.ico` (16, 20, 24, 32, 40, 48, 64 and 256 px, each a 32-bit PNG) and
`../TitleBarIcon.scale-100.png` to `scale-400.png` (16, 20, 24, 32 and 64 px) for the title bar.
Edit the geometry in the script, not the SVGs: the script overwrites them.

## Geometry
- The mark's shapes and colours are those of Android's `gnoseis_logo.xml` (46 × 46 box, edges 3.2 wide with round caps).
- Tile: a margin of 2/48 of the icon (rounded to whole pixels, so its edges stay sharp at every size),
  corner radius 8/44 of the tile's width.
- Mark: its 46 × 46 box at two thirds of the tile's width, centred, as on Android's launcher icon.
- No drop shadow.

## Small sizes
16, 20 and 24 px are hand-tuned in `make_icons.py` (`TUNED_SIZES`): the node centres are snapped to
half pixels and the edges are drawn at least 1 px wide (at 16 px they would be about 0.65 px).
Every other size is the master scaled down.

## Regenerating
Tool: Inkscape 1.4.4 (`winget install Inkscape.Inkscape`), used offline through its command line,
with Python 3 (standard library only). Nothing here is part of the build.

```
python make_icons.py "C:\Program Files\Inkscape\bin\inkscape.com"
```

The rendered PNGs go to `png/`, which is not committed. Windows caches icons: copy the build to a new
folder before checking a new icon in Explorer or the taskbar.
