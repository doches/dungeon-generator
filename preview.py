#!/usr/bin/env python3
"""
preview.py — render an ASCII dungeon map as a colour-coded PNG.

Usage:
    python3 preview.py <input.txt> <output.png> [--tile-size N]

Tile glyphs (from dungeon-generator):
    ' '  Void
    '#'  Wall
    '.'  Floor
    '-'  Horizontal corridor / spine
    '|'  Vertical corridor
    '+'  Door
"""

import sys
import argparse
from PIL import Image, ImageDraw, ImageFont

# ── Colour palette (sci-fi starship) ─────────────────────────────────────────
PALETTE = {
    " ": (10,  12,  22),   # Void         — deep space black
    "#": (42,  48,  62),   # Wall         — dark gunmetal
    ".": (58,  98, 148),   # Floor        — cool steel blue
    "-": (34,  58,  82),   # H-corridor   — darker slate
    "|": (34,  58,  82),   # V-corridor   — same as H
    "+": (220, 170,  50),  # Door         — amber gold
}
DEFAULT_COLOR = (20, 20, 30)  # fallback for unknown glyphs

# Door gets a small accent highlight so it reads clearly at small sizes
DOOR_HIGHLIGHT = (255, 210, 100)

# ── Label colours for the legend ──────────────────────────────────────────────
LEGEND = [
    (" ", "Void"),
    ("#", "Wall"),
    (".", "Floor"),
    ("-", "Corridor"),
    ("+", "Door"),
]
LEGEND_BG      = (16, 18, 30)
LEGEND_TEXT    = (200, 210, 230)
LEGEND_PADDING = 8


def parse_map(path: str) -> list[str]:
    with open(path, "r") as f:
        lines = f.read().splitlines()
    if not lines:
        raise ValueError("Input file is empty")
    width = max(len(l) for l in lines)
    # Pad all rows to the same width so the grid is rectangular
    return [l.ljust(width) for l in lines]


def render(rows: list[str], tile: int) -> Image.Image:
    height_tiles = len(rows)
    width_tiles  = len(rows[0])
    img = Image.new("RGB", (width_tiles * tile, height_tiles * tile), DEFAULT_COLOR)
    draw = ImageDraw.Draw(img)

    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            color = PALETTE.get(ch, DEFAULT_COLOR)
            px, py = x * tile, y * tile
            draw.rectangle([px, py, px + tile - 1, py + tile - 1], fill=color)

            # Door: draw a small highlight stripe across the centre
            if ch == "+" and tile >= 4:
                cx = px + tile // 2
                cy = py + tile // 2
                r  = max(1, tile // 4)
                draw.rectangle([cx - r, cy - r, cx + r, cy + r], fill=DOOR_HIGHLIGHT)

    return img


def add_legend(img: Image.Image, tile: int) -> Image.Image:
    """Append a legend bar at the bottom of the image."""
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
                                  max(10, tile - 2))
    except OSError:
        font = ImageFont.load_default()

    # Measure text height via a dummy draw
    dummy = ImageDraw.Draw(Image.new("RGB", (1, 1)))
    _, _, _, text_h = dummy.textbbox((0, 0), "Ag", font=font)
    row_h = text_h + LEGEND_PADDING

    swatch = max(10, tile)
    bar_h  = row_h + LEGEND_PADDING * 2 + swatch

    new_img = Image.new("RGB", (img.width, img.height + bar_h), LEGEND_BG)
    new_img.paste(img, (0, 0))

    draw = ImageDraw.Draw(new_img)
    y0   = img.height + LEGEND_PADDING
    x    = LEGEND_PADDING

    for glyph, label in LEGEND:
        color = PALETTE.get(glyph, DEFAULT_COLOR)
        # Colour swatch
        draw.rectangle([x, y0, x + swatch - 1, y0 + swatch - 1], fill=color)
        if glyph == "+":
            r = max(1, swatch // 4)
            cx, cy = x + swatch // 2, y0 + swatch // 2
            draw.rectangle([cx - r, cy - r, cx + r, cy + r], fill=DOOR_HIGHLIGHT)
        # Label
        draw.text((x + swatch + 4, y0 + (swatch - text_h) // 2),
                  label, fill=LEGEND_TEXT, font=font)
        # Advance x: measure label width
        _, _, lw, _ = draw.textbbox((0, 0), label, font=font)
        x += swatch + 4 + lw + LEGEND_PADDING * 2

    return new_img


def main() -> None:
    ap = argparse.ArgumentParser(
        description="Render an ASCII dungeon map as a colour-coded PNG preview.")
    ap.add_argument("input",       help="ASCII map file (from dungeon-generator)")
    ap.add_argument("output",      help="Output PNG file")
    ap.add_argument("--tile-size", type=int, default=12, metavar="N",
                    help="Pixels per tile (default: 12)")
    ap.add_argument("--no-legend", action="store_true",
                    help="Omit the legend bar")
    args = ap.parse_args()

    if args.tile_size < 1:
        print("error: --tile-size must be at least 1", file=sys.stderr)
        sys.exit(1)

    try:
        rows = parse_map(args.input)
    except (OSError, ValueError) as e:
        print(f"error: {e}", file=sys.stderr)
        sys.exit(1)

    img = render(rows, args.tile_size)
    if not args.no_legend:
        img = add_legend(img, args.tile_size)

    img.save(args.output)
    print(f"Saved {img.width}x{img.height}px → {args.output}")


if __name__ == "__main__":
    main()
