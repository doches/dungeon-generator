using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

public static class Generator
{
    public static Dungeon Generate(DungeonConfig cfg)
    {
        var rng = new Random();
        var dungeon = new Dungeon
        {
            Width  = cfg.Width,
            Height = cfg.Height,
            Grid   = new TileType[cfg.Width, cfg.Height],
            Rooms  = new(),
            Corridors = new(),
            Doors  = new(),
        };

        // Reserve horizontal margins for anchor rooms
        int spineXMin = 3;
        int spineXMax = cfg.Width - 4;

        var foreCfg = cfg.RoomTypes.FirstOrDefault(r => r.IsAnchorFore);
        var aftCfg  = cfg.RoomTypes.FirstOrDefault(r => r.IsAnchorAft);
        if (foreCfg is not null) spineXMin = foreCfg.MaxWidth + 2;
        if (aftCfg  is not null) spineXMax = cfg.Width - aftCfg.MaxWidth - 3;

        // 1. Spine (straight or snake-y depending on Snakiness)
        var (spine, yAtX, bends, jogCols, spineWidth) =
            SpineBuilder.Build(dungeon, spineXMin, spineXMax, cfg, rng);
        dungeon.MainSpine  = spine;
        dungeon.SpineWidth = spineWidth;
        dungeon.Corridors.Add(spine);

        // 2. Branch corridors (uses yAtX so each branch meets the spine at the right height)
        var branches = BranchBuilder.Build(dungeon, spine, cfg, rng, yAtX, jogCols);
        dungeon.Corridors.AddRange(branches);

        // 3. Anchor rooms (Bridge at fore end, Engineering at aft end)
        int nextRoomId = 0;
        if (foreCfg is not null)
        {
            var room = RoomBuilder.BuildAnchorFore(
                dungeon, spineXMin, spine.Start.Y, foreCfg, nextRoomId++, rng);
            if (room is not null) dungeon.Rooms.Add(room);
        }
        if (aftCfg is not null)
        {
            var room = RoomBuilder.BuildAnchorAft(
                dungeon, spineXMax, spine.End.Y, aftCfg, nextRoomId++, rng);
            if (room is not null) dungeon.Rooms.Add(room);
        }

        // Weighted type pool for regular rooms
        var weightedTypes = cfg.RoomTypes.Where(r => r.Weight > 0).ToList();
        int totalWeight   = weightedTypes.Sum(r => r.Weight);

        // 4. Bend rooms (one per spine jog, placed east of the jog to "justify" the bend)
        foreach (var bend in bends)
        {
            var typeCfg = PickRoomType(weightedTypes, totalWeight, rng);
            var room    = RoomBuilder.BuildBendRoom(dungeon, bend, typeCfg, nextRoomId++, rng);
            if (room is not null) dungeon.Rooms.Add(room);
        }

        // 5. Branch rooms (one per branch, at the branch end)
        var branchesWithRooms = new HashSet<Pt>();
        foreach (var branch in branches)
        {
            var typeCfg = PickRoomType(weightedTypes, totalWeight, rng);
            var room    = RoomBuilder.BuildForBranch(dungeon, branch, typeCfg, nextRoomId++, rng);
            if (room is not null)
            {
                dungeon.Rooms.Add(room);
                branchesWithRooms.Add(branch.End);
            }
        }

        // 5a. Prune dead-end branches (no room could be placed at the end)
        PruneDeadEndBranches(dungeon, branches, branchesWithRooms);

        // 5b. Prune rooms with fewer than 5 floor tiles
        PruneSmallRooms(dungeon);

        // 6. Outside corridors: low-probability horizontal loops along the north/south sides
        int nextCorridorId = dungeon.Corridors.Max(c => c.Id) + 1;
        var outsideBranches = OutsideCorridorBuilder.Build(dungeon, branches, rng, ref nextCorridorId);

        // 6a. Build rooms at outside corridor branch ends
        var outsideBranchesWithRooms = new HashSet<Pt>();
        foreach (var branch in outsideBranches)
        {
            var typeCfg = PickRoomType(weightedTypes, totalWeight, rng);
            var room    = RoomBuilder.BuildForBranch(dungeon, branch, typeCfg, nextRoomId++, rng);
            if (room is not null)
            {
                dungeon.Rooms.Add(room);
                outsideBranchesWithRooms.Add(branch.End);
            }
        }

        // 6b. Prune dead-end outside branches and small outside rooms
        PruneDeadEndBranches(dungeon, outsideBranches, outsideBranchesWithRooms);
        PruneSmallRooms(dungeon);

        // 6c. Divide spine into named corridor rooms separated by wide airlock doors
        CreateCorridorRooms(dungeon, yAtX, jogCols, branches, spineWidth, ref nextRoomId, rng);

        // 7. Doors, corridor walls, connections
        DoorPlacer.PlaceAll(dungeon, bends);

        return dungeon;
    }

    // ── Corridor rooms and wide airlock doors ─────────────────────────────────
    private static void CreateCorridorRooms(
        Dungeon dungeon, Dictionary<int, int> yAtX, IReadOnlySet<int> jogCols,
        List<Corridor> spineBranches, int spineWidth, ref int nextRoomId, Random rng)
    {
        var segments    = FindHorizontalSegments(yAtX, jogCols);
        var branchXCols = new HashSet<int>(spineBranches.Select(b => b.Start.X));
        int nextDoorId  = dungeon.Doors.Count > 0 ? dungeon.Doors.Max(d => d.Id) + 1 : 0;

        int doorCount       = Math.Min(2, spineWidth);
        int doorStartOffset = spineWidth / 2 - doorCount / 2;

        foreach (var (xMin, xMax, segY) in segments)
        {
            int segLen      = xMax - xMin + 1;
            int maxDividers = segLen < 10 ? 0 : segLen < 20 ? 1 : segLen < 35 ? 2 : 3;
            int numDividers = maxDividers == 0 ? 0 : rng.Next(0, maxDividers + 1);

            var dividers = new List<int>();
            for (int k = 1; k <= numDividers; k++)
            {
                int idealX = xMin + segLen * k / (numDividers + 1);
                int found  = -1;
                for (int delta = 0; delta <= segLen / 4 && found < 0; delta++)
                {
                    foreach (int candidate in delta == 0
                        ? (IEnumerable<int>)new[] { idealX }
                        : new[] { idealX - delta, idealX + delta })
                    {
                        if (candidate <= xMin || candidate >= xMax) continue;
                        if (branchXCols.Contains(candidate)) continue;
                        if (jogCols.Contains(candidate)) continue;
                        if (dividers.Any(d => Math.Abs(d - candidate) < 4)) continue;
                        found = candidate;
                        break;
                    }
                }
                if (found >= 0) dividers.Add(found);
            }
            dividers.Sort();

            // Paint wide door tiles at each divider column
            foreach (int divX in dividers)
                for (int dy = 0; dy < spineWidth; dy++)
                {
                    bool isWideDoor = dy >= doorStartOffset && dy < doorStartOffset + doorCount;
                    dungeon.Grid[divX, segY + dy] = isWideDoor ? TileType.WideDoor : TileType.Wall;
                }

            // Create one corridor room per sub-segment between dividers
            var segRooms    = new List<Room>();
            var roomBounds  = new List<(int x1, int x2)>();
            int prevX = xMin;
            foreach (int divX in dividers)
            {
                if (divX - 1 >= prevX) roomBounds.Add((prevX, divX - 1));
                prevX = divX + 1;
            }
            if (prevX <= xMax) roomBounds.Add((prevX, xMax));

            foreach (var (x1, x2) in roomBounds)
            {
                if (x2 < x1) continue;

                int roomW = x2 - x1 + 1;
                var rect  = new Rect(x1, segY, roomW, spineWidth);
                var room  = new Room
                {
                    Id          = nextRoomId++,
                    Type        = "Corridor",
                    Tiles       = new List<Rect> { rect },
                    Bounds      = new BoundingBox(x1, segY, roomW, spineWidth),
                    IsAnchor    = false,
                    AttachPoint = new Pt(x1, segY),
                };
                dungeon.Rooms.Add(room);
                segRooms.Add(room);
            }

            // Register a Door object for each wide door tile
            foreach (int divX in dividers)
            {
                var roomLeft  = segRooms.LastOrDefault(r  => r.Bounds.X + r.Bounds.Width - 1 < divX);
                var roomRight = segRooms.FirstOrDefault(r => r.Bounds.X > divX);

                for (int dy = doorStartOffset; dy < doorStartOffset + doorCount; dy++)
                {
                    var doorPt = new Pt(divX, segY + dy);
                    var door   = new Door
                    {
                        Id       = nextDoorId++,
                        Position = doorPt,
                        RoomAId  = roomLeft?.Id,
                        RoomBId  = roomRight?.Id,
                        IsWide   = true,
                    };
                    roomLeft?.DoorPositions.Add(doorPt);
                    roomRight?.DoorPositions.Add(doorPt);
                    dungeon.Doors.Add(door);
                }
            }
        }
    }

    private static List<(int xMin, int xMax, int y)> FindHorizontalSegments(
        Dictionary<int, int> yAtX, IReadOnlySet<int> jogCols)
    {
        var sorted = yAtX
            .Where(kv => !jogCols.Contains(kv.Key))
            .OrderBy(kv => kv.Key)
            .ToList();

        if (sorted.Count == 0) return new();

        var segments = new List<(int, int, int)>();
        int segStart = sorted[0].Key;
        int segY     = sorted[0].Value;
        int prevX    = sorted[0].Key;

        for (int i = 1; i < sorted.Count; i++)
        {
            int x = sorted[i].Key, y = sorted[i].Value;
            if (y != segY || x != prevX + 1)
            {
                segments.Add((segStart, prevX, segY));
                segStart = x;
                segY     = y;
            }
            prevX = x;
        }
        segments.Add((segStart, prevX, segY));
        return segments;
    }

    private static RoomTypeConfig PickRoomType(
        List<RoomTypeConfig> types, int total, Random rng)
    {
        if (total <= 0)
            return new RoomTypeConfig { Name = "Room", MinWidth = 5, MaxWidth = 9, MinHeight = 4, MaxHeight = 7 };

        int roll = rng.Next(total);
        int cumulative = 0;
        foreach (var t in types)
        {
            cumulative += t.Weight;
            if (roll < cumulative) return t;
        }
        return types[^1];
    }

    private static void PruneDeadEndBranches(Dungeon dungeon, IEnumerable<Corridor> branches, HashSet<Pt> builtEnds)
    {
        var dead = branches.Where(b => !builtEnds.Contains(b.End)).ToList();
        foreach (var c in dead)
        {
            foreach (var pt in c.Spine)
                if (dungeon.Grid[pt.X, pt.Y] == TileType.VertCorridor)
                    dungeon.Grid[pt.X, pt.Y] = TileType.Void;
            dungeon.Corridors.Remove(c);
        }
    }

    private static void PruneSmallRooms(Dungeon dungeon)
    {
        var prunedIds          = new HashSet<int>();
        var prunedAttachPoints = new HashSet<Pt>();

        foreach (var room in dungeon.Rooms.ToList())
        {
            if (room.Type == "Corridor") continue; // corridor rooms have no floor tiles

            var union = new HashSet<(int x, int y)>();
            foreach (var rect in room.Tiles)
                for (int rx = rect.X; rx < rect.X + rect.Width; rx++)
                    for (int ry = rect.Y; ry < rect.Y + rect.Height; ry++)
                        union.Add((rx, ry));

            if (union.Count(p => dungeon.Grid[p.x, p.y] == TileType.Floor) >= 5) continue;

            foreach (var (x, y) in union)
                dungeon.Grid[x, y] = TileType.Void;

            prunedIds.Add(room.Id);
            prunedAttachPoints.Add(room.AttachPoint);
            dungeon.Rooms.Remove(room);
        }

        if (prunedIds.Count == 0) return;

        var orphans = dungeon.Corridors
            .Where(c => c.Kind == CorridorKind.Branch && prunedAttachPoints.Contains(c.End))
            .ToList();
        foreach (var c in orphans)
        {
            foreach (var pt in c.Spine)
                if (dungeon.Grid[pt.X, pt.Y] == TileType.VertCorridor)
                    dungeon.Grid[pt.X, pt.Y] = TileType.Void;
            dungeon.Corridors.Remove(c);
        }

        dungeon.Doors.RemoveAll(d =>
            (d.RoomAId.HasValue && prunedIds.Contains(d.RoomAId.Value)) ||
            (d.RoomBId.HasValue && prunedIds.Contains(d.RoomBId.Value)));
    }
}
