using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

public static class OutsideCorridorBuilder
{
    /// <summary>
    /// Builds outside corridors connecting same-side rooms and branches off their
    /// horizontal segments. Always attempts to build 1–4 outside corridors total.
    /// Returns the branch corridors so Generator can attach rooms.
    /// All corridors (outside + branches) are added to dungeon.Corridors directly.
    /// </summary>
    public static List<Corridor> Build(
        Dungeon dungeon, List<Corridor> branches, Random rng, ref int nextId)
    {
        var outsideBranches = new List<Corridor>();
        int target = rng.Next(1, 5); // always build 1–4

        int northBuilt = TryConnectSide(
            dungeon, GetSideRooms(dungeon, branches, north: true),  true,  rng, ref nextId, outsideBranches, target);
        TryConnectSide(
            dungeon, GetSideRooms(dungeon, branches, north: false), false, rng, ref nextId, outsideBranches, target - northBuilt);
        return outsideBranches;
    }

    private static List<Room> GetSideRooms(Dungeon dungeon, List<Corridor> branches, bool north)
        => branches
            .Where(b => north ? b.End.Y < b.Start.Y : b.End.Y > b.Start.Y)
            .Select(b => dungeon.Rooms.FirstOrDefault(r => r.AttachPoint == b.End))
            .OfType<Room>()
            .OrderBy(r => r.X)
            .ToList();

    private static int TryConnectSide(
        Dungeon dungeon, List<Room> rooms, bool north, Random rng,
        ref int nextId, List<Corridor> outsideBranches, int maxCount)
    {
        if (maxCount <= 0 || rooms.Count < 2) return 0;

        // Build all valid candidate pairs (2–6 rooms apart), then shuffle for variety
        var candidates = new List<(int i, int j)>();
        for (int i = 0; i < rooms.Count - 1; i++)
            for (int dist = 2; dist <= Math.Min(6, rooms.Count - i - 1); dist++)
                candidates.Add((i, i + dist));

        for (int k = candidates.Count - 1; k > 0; k--)
        {
            int s = rng.Next(k + 1);
            (candidates[k], candidates[s]) = (candidates[s], candidates[k]);
        }

        var used  = new HashSet<int>();
        int built = 0;

        foreach (var (i, j) in candidates)
        {
            if (built >= maxCount) break;
            if (used.Contains(rooms[i].Id) || used.Contains(rooms[j].Id)) continue;

            var (corridor, branchList) = TryBuild(dungeon, rooms[i], rooms[j], north, rng, ref nextId);
            if (corridor is null) continue;

            dungeon.Corridors.Add(corridor);
            foreach (var b in branchList) dungeon.Corridors.Add(b);
            outsideBranches.AddRange(branchList);
            used.Add(rooms[i].Id);
            used.Add(rooms[j].Id);
            built++;
        }

        return built;
    }

    // Find a column at the room's outer boundary row that has Wall + interior Floor neighbour.
    // The bounding-box centre can fall in a gap when a wing widens the box, so we scan outward.
    private static int FindBoundaryX(Dungeon dungeon, Room room, bool north)
    {
        int boundaryY = north ? room.Y : room.Y + room.Height - 1;
        int interiorY = north ? boundaryY + 1  : boundaryY - 1;
        int centerX   = room.X + room.Width / 2;

        for (int delta = 0; delta <= room.Width / 2 + 1; delta++)
        {
            foreach (int x in delta == 0
                ? (IEnumerable<int>)new[] { centerX }
                : new[] { centerX - delta, centerX + delta })
            {
                if (x < 1 || x >= dungeon.Width - 1) continue;
                if (interiorY < 1 || interiorY >= dungeon.Height - 1) continue;
                if (dungeon.Grid[x, boundaryY] == TileType.Wall
                 && dungeon.Grid[x, interiorY]  == TileType.Floor)
                    return x;
            }
        }
        return centerX; // fallback: no scan result (very unusual)
    }

    private static (Corridor? corridor, List<Corridor> branches) TryBuild(
        Dungeon dungeon, Room a, Room b, bool north, Random rng, ref int nextId)
    {
        int corridorId = nextId++;

        int xA = FindBoundaryX(dungeon, a, north);
        int xB = FindBoundaryX(dungeon, b, north);
        if (Math.Abs(xA - xB) < 3) return (null, new());

        int yCorr = north
            ? Math.Min(a.Y, b.Y) - 1
            : Math.Max(a.Y + a.Height - 1, b.Y + b.Height - 1) + 1;

        if (yCorr < 1 || yCorr >= dungeon.Height - 1) return (null, new());

        int yANear = north ? a.Y - 1 : a.Y + a.Height;
        int yBNear = north ? b.Y - 1 : b.Y + b.Height;

        var seen  = new HashSet<(int, int)>();
        var tiles = new List<Pt>();
        void Add(int x, int y) { if (seen.Add((x, y))) tiles.Add(new Pt(x, y)); }

        for (int y = Math.Min(yCorr, yANear); y <= Math.Max(yCorr, yANear); y++) Add(xA, y);
        for (int x = Math.Min(xA, xB); x <= Math.Max(xA, xB); x++)               Add(x, yCorr);
        for (int y = Math.Min(yCorr, yBNear); y <= Math.Max(yCorr, yBNear); y++) Add(xB, y);

        foreach (var pt in tiles)
        {
            if (pt.X < 1 || pt.Y < 1 || pt.X >= dungeon.Width - 1 || pt.Y >= dungeon.Height - 1)
                return (null, new());
            if (dungeon.Grid[pt.X, pt.Y] != TileType.Void)
                return (null, new());
        }

        foreach (var pt in tiles)
            dungeon.Grid[pt.X, pt.Y] = pt.Y == yCorr ? TileType.HorizCorridor : TileType.VertCorridor;

        var entryDir = new Pt(0, north ? 1 : -1);
        var corridor = new Corridor
        {
            Id            = corridorId,
            Kind          = CorridorKind.Outside,
            Start         = new Pt(xA, yANear),
            End           = new Pt(xB, yBNear),
            Spine         = tiles,
            StartEntryDir = entryDir,
            EndEntryDir   = entryDir,
        };

        // Grow branches off the inner part of the horizontal segment
        int xMin = Math.Min(xA, xB) + 1;
        int xMax = Math.Max(xA, xB) - 1;
        var branchList = BuildHorizontalBranches(dungeon, xMin, xMax, yCorr, north, rng, ref nextId, corridorId);

        return (corridor, branchList);
    }

    private static List<Corridor> BuildHorizontalBranches(
        Dungeon dungeon, int xMin, int xMax, int yCorr, bool north,
        Random rng, ref int nextId, int parentId)
    {
        var result = new List<Corridor>();
        if (xMax - xMin < 4) return result;

        int dy     = north ? -1 : 1;
        int offset = rng.Next(3, 6);

        while (xMin + offset <= xMax)
        {
            int x         = xMin + offset;
            int branchLen = rng.Next(3, 7);

            var branchTiles = new List<Pt>();
            bool valid      = true;

            for (int k = 1; k <= branchLen; k++)
            {
                int by = yCorr + dy * k;
                if (by < 1 || by >= dungeon.Height - 1) { valid = false; break; }
                if (dungeon.Grid[x, by] != TileType.Void) { valid = false; break; }
                branchTiles.Add(new Pt(x, by));
            }

            if (valid && branchTiles.Count > 0)
            {
                foreach (var pt in branchTiles)
                    dungeon.Grid[pt.X, pt.Y] = TileType.VertCorridor;

                result.Add(new Corridor
                {
                    Id               = nextId++,
                    Kind             = CorridorKind.Branch,
                    Start            = new Pt(x, yCorr),
                    End              = branchTiles[^1],
                    Spine            = branchTiles,
                    ParentCorridorId = parentId,
                });
            }

            offset += rng.Next(4, 9);
        }

        return result;
    }
}
