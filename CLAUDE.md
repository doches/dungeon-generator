# CLAUDE.md — dungeon-generator

This file gives context for AI agents working on this codebase.

## Project overview

C# .NET 10 console application that procedurally generates sci-fi starship dungeon layouts. Each run produces:

- An **ASCII tile map** (`.txt`) for visual inspection and rendering
- A **JSON metadata file** describing rooms, corridors, doors, and connections

A companion Python script (`preview.py`) renders the ASCII map as a colour-coded PNG using Pillow.

## File structure

```
dungeon-generator/
├── DungeonGenerator.csproj     .NET 10, no external packages
├── Program.cs                  CLI parsing, config loading, orchestration
├── preview.py                  Python/Pillow PNG renderer
├── Models/
│   ├── TileType.cs             TileType enum (byte)
│   ├── Room.cs                 Room, Pt, Rect, BoundingBox records
│   ├── Corridor.cs             Corridor, CorridorKind enum
│   ├── Door.cs                 Door (with IsWide flag)
│   └── Dungeon.cs              Dungeon, DungeonConfig, RoomTypeConfig
├── Generation/
│   ├── Generator.cs            Top-level orchestrator
│   ├── SpineBuilder.cs         Spine + BendInfo record
│   ├── BranchBuilder.cs        Vertical branch corridors
│   ├── RoomBuilder.cs          Room placement, wings, collision detection
│   ├── DoorPlacer.cs           Door placement, corridor walls, stray door removal
│   └── OutsideCorridorBuilder.cs  Horizontal outside loops
└── Output/
    ├── AsciiRenderer.cs        Grid → ASCII string
    └── JsonExporter.cs         Dungeon → JSON (DTOs, System.Text.Json)
```

## Key design decisions

- **Single source of truth**: `TileType[,] Grid` — all rendering reads the grid; JSON metadata is derived from it.
- **Rooms as rect unions**: each `Room` stores `List<Rect>` (base rectangle + wings). Union of rects = rectilinear room shape.
- **No NuGet packages**: uses `System.Text.Json` (in-box for .NET 10).
- **Wall painting**: a tile is `Wall` if any cardinal neighbour is outside the union of all room rects.

## TileType enum

```csharp
Void=0, Wall=1, Floor=2, HorizCorridor=3, VertCorridor=4, Door=5, SpineCorridor=6, WideDoor=7
```

ASCII glyphs: `' '  '#'  '.'  '-'  '|'  '+'  '-'  '='`

## Generation algorithm (Generator.cs)

1. **Spine** (`SpineBuilder`) — horizontal corridor, 2–4 tiles wide, straight or snake-y
2. **Branch corridors** (`BranchBuilder`) — vertical offshoots north/south; north branches start at `spineTopY - 1`, south branches start at `spineBottomY + 1`
3. **Anchor rooms** — Bridge (fore/west) and Engineering (aft/east), built by `RoomBuilder.BuildAnchorFore/Aft`
4. **Bend rooms** (`RoomBuilder.BuildBendRoom`) — one per spine jog, east of the jog column
5. **Branch rooms** (`RoomBuilder.BuildForBranch`) — one per branch, at the branch end
6. **Prune dead-end branches** — branches with no room are erased from the grid
7. **Prune small rooms** — rooms with fewer than 5 floor tiles are erased (skips `"Corridor"` type)
8. **Outside corridors** (`OutsideCorridorBuilder`) — horizontal ∩/∪ loops connecting same-side rooms 2–6 apart; rooms may grow off their branch ends; pruned same as above
9. **Corridor rooms** (`Generator.CreateCorridorRooms`) — each horizontal spine segment is divided into named `"Corridor"` rooms by wide-door dividers; `WideDoor` tiles are painted and `Door` objects (IsWide=true) are registered
10. **DoorPlacer** — paints corridor walls, places single doors at room/corridor junctions, bend room doors, shared-wall doors between adjacent rooms, then removes stray doors and builds connection lists

## Spine details (SpineBuilder)

- `spineWidth = rng.Next(2, 5)` — 2, 3, or 4 tiles wide
- `baseY = dungeon.Height / 2 - (spineWidth - 1) / 2` — top row, vertically centred
- `yAtX[x]` stores the **top row** of the spine at each horizontal column x
- Jog columns are in `jogCols` and excluded from branch placement
- `BendInfo(JogX, Y1, Y2, SpineWidth)` — Y1/Y2 are **top rows** of each segment:
  - `RoomY = Min(Y1,Y2) + SpineWidth`
  - `RoomHeight = |Y2−Y1| − SpineWidth`
- Minimum jog magnitude = `spineWidth + 3` (ensures bend room height ≥ 3)
- `spine.Start` and `spine.End` are tracked explicitly (top row of first/last horizontal column)

## Corridor rooms and wide doors

- `CreateCorridorRooms` finds horizontal segments via `FindHorizontalSegments` (excludes jog columns)
- Each segment gets 0–3 dividers placed at roughly equal intervals, avoiding branch and jog columns
- At each divider column: outer rows become `Wall`, inner `min(2, spineWidth)` rows become `WideDoor`
- `Room` objects of type `"Corridor"` cover the spine tiles between dividers
- Wide door `Door` objects are added after DoorPlacer with `IsWide = true` and correct `RoomAId`/`RoomBId`
- `DoorPlacer.WalkAndPlaceDoor` stops at `WideDoor` tiles; `RemoveStrayDoors` treats them as traversable

## DoorPlacer

- **`PaintCorridorWalls`** — for each corridor spine tile, any adjacent Void becomes Wall
- **`WalkAndPlaceDoor`** — walks from a corridor end in a given direction until it hits Wall→places Door, or Floor/Door/WideDoor→stops
- **`PlaceBendRoomDoors`** — north-wall walk starts from `yTop + bend.SpineWidth - 1` (bottom row of upper segment)
- **`PlaceSharedWallDoors`** — adjacent wall tiles from different rooms → one door per pair
- **`RemoveStrayDoors`** — removes Door tiles that don't have traversable tiles on both sides of one axis
- **`usedWalls`** — `Dictionary<int, HashSet<(int,int)>>` prevents multiple doors on the same room face

## RoomBuilder

- **`TryBuild`** — up to 5 retries with shrinking size on collision; rooms clamped 1 tile from map edge
- **`AddWings`** — 0–2 wings per room; each wing shares one edge side with the base rect (1-tile overlap)
- **`CollisionCheck`** — returns true if any tile in rect is Floor, Wall, or any Corridor type
- **`PaintRoom`** — union of all rects; perimeter tiles with any cardinal neighbour outside the union → Wall; interior → Floor

## OutsideCorridorBuilder

- `GetSideRooms` — filters branches going north/south by `b.End.Y < b.Start.Y` etc.
- `FindBoundaryX` — scans outward from bounding-box centre along the room's outer boundary row; finds first column with Wall + interior Floor neighbour
- Outside corridors are rejected if any tile is non-Void (strict collision)
- `BuildHorizontalBranches` — grows short VertCorridor stubs off the outside corridor's horizontal segment

## Preview (preview.py)

- Requires Pillow (`pip install Pillow`)
- `load_room_floor_colors(json_path)` — builds `{(x,y): rgb}` from all room tile rects
- `render()` — applies room color to `'.'` and `'-'` tiles when position is in the color dict
- `add_legend()` — appends a legend bar; two rows when JSON supplied (tile types + room types)
- Wide doors `'='` render in deep orange; corridor rooms `"Corridor"` render in dark steel blue

## Default room configuration (Program.cs)

```json
[
  { "name": "Bridge",       "weight": 0,  "minWidth": 13, "maxWidth": 25, "minHeight": 11, "maxHeight": 21, "isAnchorFore": true },
  { "name": "Engineering",  "weight": 0,  "minWidth": 13, "maxWidth": 25, "minHeight": 11, "maxHeight": 21, "isAnchorAft":  true },
  { "name": "MedBay",       "weight": 10, "minWidth": 10, "maxWidth": 20, "minHeight": 9,  "maxHeight": 18 },
  { "name": "Armory",       "weight": 8,  "minWidth": 9,  "maxWidth": 19, "minHeight": 9,  "maxHeight": 17 },
  { "name": "CrewQuarters", "weight": 12, "minWidth": 10, "maxWidth": 21, "minHeight": 9,  "maxHeight": 19 },
  { "name": "Storage",      "weight": 15, "minWidth": 9,  "maxWidth": 18, "minHeight": 8,  "maxHeight": 17 },
  { "name": "Lab",          "weight": 8,  "minWidth": 10, "maxWidth": 20, "minHeight": 9,  "maxHeight": 18 }
]
```

`weight: 0` = anchor-only (Bridge/Engineering). Higher weight = more frequent random placement.

## Known limitations / future work ideas

- Anchor room doors are placed only at the spine's top row (single door even on a wide spine)
- Corridor rooms that happen to touch a regular room's wall will get a shared-wall door — this is intentional
- Outside corridors require all tiles to be Void; complex maps may fail to place any
- `PruneDeadEndBranches` only handles `VertCorridor` tiles; HorizCorridor stubs from outside corridors are not pruned
- No seeded RNG — each run is fully random; add `new Random(seed)` to `Generator.Generate` for reproducibility
- No pathfinding validation — it's possible (rarely) for a room to be isolated if all its doors are pruned
