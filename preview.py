#!/usr/bin/env python3
"""
preview.py — render an ASCII dungeon map as a colour-coded PNG.

Usage:
    python3 preview.py <input.txt> <output.png> [--json <dungeon.json>]
                       [--tile-size N] [--no-legend]

Tile glyphs (from dungeon-generator):
    ' '  Void
    '#'  Wall
    '.'  Floor
    '-'  Horizontal corridor / spine
    '|'  Vertical corridor
    '+'  Door
"""

import sys
import json
import argparse
from PIL import Image, ImageDraw, ImageFont

# ── Colour palette (sci-fi starship) ─────────────────────────────────────────
PALETTE = {
    " ": (10,  12,  22),   # Void         — deep space black
    "#": (42,  48,  62),   # Wall         — dark gunmetal
    ".": (58,  98, 148),   # Floor        — cool steel blue (fallback)
    "-": (34,  58,  82),   # H-corridor   — darker slate
    "|": (34,  58,  82),   # V-corridor   — same as H
    "+": (220, 170,  50),  # Door         — amber gold
}
DEFAULT_COLOR = (20, 20, 30)

# Door highlight
DOOR_HIGHLIGHT = (255, 210, 100)

# ── Per-room-type floor colours ───────────────────────────────────────────────
ROOM_COLORS = {
    "Bridge":       ( 45, 130, 200),  # command blue
    "Engineering":  (200, 100,  30),  # power orange
    "MedBay":       ( 50, 170,  90),  # medical green
    "Armory":       (180,  45,  55),  # weapons red
    "CrewQuarters": (110,  65, 175),  # quarters purple
    "Storage":      (160, 130,  40),  # cargo gold
    "Lab":          ( 45, 175, 190),  # science cyan
}

# ── Legend rows ───────────────────────────────────────────────────────────────
TILE_LEGEND = [
    (" ", "Void"),
    ("#", "Wall"),
    (".", "Floor"),
    ("-", "Corridor"),
    ("+", "Door"),
]
LEGEND_BG      = (16, 18, 30)
LEGEND_TEXT    = (200, 210, 230)
LEGEND_PADDING = 8


def load_room_floor_colors(json_path: str) -> dict:
    """Return {(x, y): rgb} for every tile belonging to a room, keyed by type color."""
    with open(json_path) as f:
        data = json.load(f)

    result = {}
    for room in data.get("rooms", []):
        rtype = room.get("type", "")
        color = ROOM_COLORS.get(rtype, PALETTE["."])
        for rect in room.get("tiles", []):
            x0, y0, w, h = rect["x"], rect["y"], rect["width"], rect["height"]
            for x in range(x0, x0 + w):
                for y in range(y0, y0 + h):
                    result[(x, y)] = color
    return result


def parse_map(path: str) -> list[str]:
    with open(path, "r") as f:
        lines = f.read().splitlines()
    if not lines:
        raise ValueError("Input file is empty")
    width = max(len(l) for l in lines)
    return [l.ljust(width) for l in lines]


def render(rows: list[str], tile: int,
           room_floor_colors: dict | None = None) -> Image.Image:
    height_tiles = len(rows)
    width_tiles  = len(rows[0])
    img  = Image.new("RGB", (width_tiles * tile, height_tiles * tile), DEFAULT_COLOR)
    draw = ImageDraw.Draw(img)

    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch == "." and room_floor_colors and (x, y) in room_floor_colors:
                color = room_floor_colors[(x, y)]
            else:
                color = PALETTE.get(ch, DEFAULT_COLOR)

            px, py = x * tile, y * tile
            draw.rectangle([px, py, px + tile - 1, py + tile - 1], fill=color)

            if ch == "+" and tile >= 4:
                cx = px + tile // 2
                cy = py + tile // 2
                r  = max(1, tile // 4)
                draw.rectangle([cx - r, cy - r, cx + r, cy + r], fill=DOOR_HIGHLIGHT)

    return img


def add_legend(img: Image.Image, tile: int,
               room_types_present: list[str] | None = None) -> Image.Image:
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
                                  max(10, tile - 2))
    except OSError:
        font = ImageFont.load_default()

    dummy = ImageDraw.Draw(Image.new("RGB", (1, 1)))
    _, _, _, text_h = dummy.textbbox((0, 0), "Ag", font=font)
    row_h  = text_h + LEGEND_PADDING
    swatch = max(10, tile)

    n_rows = 2 if room_types_present else 1
    bar_h  = LEGEND_PADDING + n_rows * (swatch + LEGEND_PADDING)

    new_img = Image.new("RGB", (img.width, img.height + bar_h), LEGEND_BG)
    new_img.paste(img, (0, 0))
    draw = ImageDraw.Draw(new_img)

    def draw_row(items, y0):
        x = LEGEND_PADDING
        for color, label, highlight in items:
            draw.rectangle([x, y0, x + swatch - 1, y0 + swatch - 1], fill=color)
            if highlight:
                r = max(1, swatch // 4)
                cx, cy = x + swatch // 2, y0 + swatch // 2
                draw.rectangle([cx - r, cy - r, cx + r, cy + r], fill=DOOR_HIGHLIGHT)
            draw.text((x + swatch + 4, y0 + (swatch - text_h) // 2),
                      label, fill=LEGEND_TEXT, font=font)
            _, _, lw, _ = draw.textbbox((0, 0), label, font=font)
            x += swatch + 4 + lw + LEGEND_PADDING * 2

    # Row 1: tile types
    y0 = img.height + LEGEND_PADDING
    tile_items = [(PALETTE[g], lbl, g == "+") for g, lbl in TILE_LEGEND]
    draw_row(tile_items, y0)

    # Row 2: room types (when JSON was supplied)
    if room_types_present:
        y0 += swatch + LEGEND_PADDING
        room_items = [
            (ROOM_COLORS.get(t, PALETTE["."]), t, False)
            for t in room_types_present
        ]
        draw_row(room_items, y0)

    return new_img


def main() -> None:
    ap = argparse.ArgumentParser(
        description="Render an ASCII dungeon map as a colour-coded PNG preview.")
    ap.add_argument("input",       help="ASCII map file (from dungeon-generator)")
    ap.add_argument("output",      help="Output PNG file")
    ap.add_argument("--json",      metavar="PATH",
                    help="JSON metadata file for room-type colour coding")
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

    room_floor_colors = None
    room_types_present = None
    if args.json:
        try:
            room_floor_colors  = load_room_floor_colors(args.json)
            with open(args.json) as f:
                data = json.load(f)
            seen = []
            for r in data.get("rooms", []):
                t = r.get("type", "")
                if t and t not in seen:
                    seen.append(t)
            room_types_present = seen
        except (OSError, ValueError, KeyError) as e:
            print(f"warning: could not load JSON ({e}); rendering without room colours",
                  file=sys.stderr)

    img = render(rows, args.tile_size, room_floor_colors)
    if not args.no_legend:
        img = add_legend(img, args.tile_size, room_types_present)

    img.save(args.output)
    print(f"Saved {img.width}x{img.height}px → {args.output}")


if __name__ == "__main__":
    main()
