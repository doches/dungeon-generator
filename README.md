# dungeon-generator

Procedural sci-fi starship dungeon layout generator. Outputs an ASCII tile map and a JSON metadata file describing rooms, corridors, and doors. Pair it with `preview.py` to render a colour-coded PNG.

## Requirements

| Tool | Minimum version | Notes |
|------|----------------|-------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 | `dotnet` must be on your `PATH` |
| [Python](https://python.org) | 3.10 | For `preview.py` only |
| [Pillow](https://pillow.readthedocs.io) | 9.0 | `pip install Pillow` |

Check your versions:

```sh
dotnet --version   # should print 10.x.y
python3 --version  # should print 3.10 or later
python3 -c "import PIL; print(PIL.__version__)"
```

---

## Building

```sh
cd dungeon-generator
dotnet build
```

To produce a self-contained executable:

```sh
dotnet publish -c Release -r linux-x64 --self-contained
```

---

## Generating a layout

```sh
dotnet run -- [OPTIONS]
```

Or, after publishing:

```sh
./dungeon-generator [OPTIONS]
```

### Options

| Option | Default | Description |
|--------|---------|-------------|
| `--width <int>` | `80` | Map width in tiles |
| `--height <int>` | `40` | Map height in tiles |
| `--snakiness <0-100>` | `0` | How much the central spine bends. `0` = dead straight, `100` = maximum snake |
| `--out-ascii <path>` | stdout | Write the ASCII tile map to a file |
| `--out-json <path>` | stdout | Write the JSON metadata to a file |
| `--config <path>` | built-in | Custom room-type configuration JSON (see below) |
| `--verbose-json` | off | Include full tile coordinate lists for every corridor in the JSON |
| `--help` | | Print usage and exit |

### Examples

```sh
# Quick preview to terminal (both outputs go to stdout)
dotnet run

# Standard-sized layout to files
dotnet run -- --out-ascii dungeon.txt --out-json dungeon.json

# Large map with a snake-y spine
dotnet run -- --width 160 --height 70 --snakiness 50 \
             --out-ascii dungeon.txt --out-json dungeon.json

# Custom room types
dotnet run -- --config myrooms.json --out-ascii dungeon.txt --out-json dungeon.json
```

---

## ASCII tile map

Each character in the output map represents one tile:

| Glyph | Tile | Description |
|-------|------|-------------|
| ` ` | Void | Empty space outside the ship |
| `#` | Wall | Room or corridor wall |
| `.` | Floor | Interior room floor |
| `-` | Corridor | Horizontal corridor / spine |
| `\|` | Corridor | Vertical corridor (branch) |
| `+` | Door | Single-tile door |
| `=` | Wide Door | Two-tile-tall airlock / security door on the spine |

---

## JSON metadata

The JSON file contains the full machine-readable dungeon description.

```jsonc
{
  "gridWidth": 100,
  "gridHeight": 50,
  "rooms": [
    {
      "id": 0,
      "type": "Bridge",
      "isAnchor": true,
      "bounds": { "x": 3, "y": 14, "width": 24, "height": 17 },
      "tiles": [
        { "x": 3, "y": 14, "width": 24, "height": 17 }
      ],
      "doorPositions": [{ "x": 27, "y": 22 }],
      "connections": [0, 5]
    }
  ],
  "corridors": [
    {
      "id": 0,
      "kind": "Spine",
      "start": { "x": 27, "y": 22 },
      "end":   { "x": 76, "y": 22 }
    }
  ],
  "doors": [
    { "id": 0, "position": { "x": 27, "y": 22 }, "roomAId": 0, "corridorId": 0 },
    { "id": 4, "position": { "x": 38, "y": 22 }, "roomAId": 2, "roomBId": 3, "isWide": true }
  ]
}
```

**Room types** in the default configuration: `Bridge`, `Engineering`, `MedBay`, `Armory`, `CrewQuarters`, `Storage`, `Lab`, `Corridor`.

**Corridor kinds**: `Spine` (the central horizontal spine), `Branch` (vertical offshoot from spine), `Outside` (horizontal loop connecting same-side rooms).

**Wide doors** (`isWide: true`) appear in pairs on the spine, dividing it into named `Corridor` rooms that represent airlock or security sections.

---

## Custom room configuration

Pass `--config <path>` with a JSON file containing an array of room-type objects:

```json
[
  {
    "name": "Bridge",
    "weight": 0,
    "minWidth": 13, "maxWidth": 25,
    "minHeight": 11, "maxHeight": 21,
    "isAnchorFore": true
  },
  {
    "name": "CrewQuarters",
    "weight": 12,
    "minWidth": 10, "maxWidth": 21,
    "minHeight": 9,  "maxHeight": 19
  }
]
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Room type identifier (used in JSON output and colour coding) |
| `weight` | int | Relative probability for random placement. `0` = anchor-only (never randomly placed) |
| `minWidth` / `maxWidth` | int | Tile width range for the base rectangle |
| `minHeight` / `maxHeight` | int | Tile height range for the base rectangle |
| `isAnchorFore` | bool | If `true`, one room of this type is placed at the fore (left) end of the spine |
| `isAnchorAft` | bool | If `true`, one room of this type is placed at the aft (right) end of the spine |

Rooms can grow wings (smaller rectangles attached to any side) to create L-, T-, or U-shapes. Wing size is bounded by the room's min/max dimensions.

---

## Generating a PNG preview

`preview.py` renders an ASCII tile map as a colour-coded PNG. It is most useful when combined with the JSON file so that rooms are coloured by type.

```sh
python3 preview.py <input.txt> <output.png> [OPTIONS]
```

### Options

| Option | Default | Description |
|--------|---------|-------------|
| `--json <path>` | none | JSON metadata file; enables per-room-type colour coding |
| `--tile-size <N>` | `12` | Pixels per tile. Use `6`–`8` for large maps, `14`–`18` for detail |
| `--no-legend` | off | Omit the colour legend bar at the bottom |

### Examples

```sh
# Basic render (no room colour coding)
python3 preview.py dungeon.txt dungeon.png

# Full colour-coded render
python3 preview.py dungeon.txt dungeon.png --json dungeon.json

# Large map at smaller tile size
python3 preview.py dungeon.txt dungeon.png --json dungeon.json --tile-size 8

# High-detail small map
python3 preview.py dungeon.txt dungeon.png --json dungeon.json --tile-size 16

# No legend
python3 preview.py dungeon.txt dungeon.png --json dungeon.json --no-legend
```

### Colour palette

| Colour | Tile / Room type |
|--------|-----------------|
| Deep space black | Void |
| Dark gunmetal | Wall |
| Amber gold | Door (`+`) |
| Deep orange | Wide door (`=`) |
| Command blue | Bridge |
| Power orange | Engineering |
| Medical green | MedBay |
| Weapons red | Armory |
| Quarters purple | CrewQuarters |
| Cargo gold | Storage |
| Science cyan | Lab |
| Dark steel | Corridor (spine section) |

---

## End-to-end example

```sh
# 1. Generate
dotnet run -- --width 120 --height 55 --snakiness 40 \
             --out-ascii map.txt --out-json map.json

# 2. Render
python3 preview.py map.txt map.png --json map.json --tile-size 10

# 3. Inspect the JSON
python3 -m json.tool map.json | head -60
```
