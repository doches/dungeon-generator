using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

public static class OutsideCorridorBuilder
{
    private const double Probability = 0.25;

    /// <summary>
    /// Builds outside corridors connecting same-side rooms and branches off their
    /// horizontal segments. Returns the branch corridors so Generator can attach rooms.
    /// All corridors (outside + branches) are added to dungeon.Corridors directly.
    /// </summary>
    public static List<Corridor> Build(
        Dungeon dungeon, List<Corridor> branches, Random rng, ref int nextId)
    {
        var outsideBranches = new List<Corridor>();
        TryConnectSide(dungeon, GetSideRooms(dungeon, branches, north: true),  true,  rng, ref nextId, outsideBranches);
        TryConnectSide(dungeon, GetSideRooms(dungeon, branches, north: false), false, rng, ref nextId, outsideBranches);
        return outsideBranches;
    }

    private static List<Room> GetSideRooms(Dungeon dungeon, List<Corridor> branches, bool north)
        => branches
            .Where(b => north ? b.End.Y < b.Start.Y : b.End.Y > b.Start.Y)
            .Select(b => dungeon.Rooms.FirstOrDefault(r => r.AttachPoint == b.End))
            .OfType<Room>()
            .OrderBy(r => r.Bounds.X)
            .ToList();

    private static void TryConnectSide(
        Dungeon dungeon, List<Room> rooms, bool north, Random rng,
        ref int nextId, List<Corridor> outsideBranches)
    {
        var used = new HashSet<int>();

        for (int i = 0; i < rooms.Count; i++)
        {
            if (used.Contains(rooms[i].Id)) continue;
            if (rng.NextDouble() > Probability) continue;

            int maxDist = Math.Min(6, rooms.Count - i - 1);
            if (maxDist < 2) continue;

            int dist = rng.Next(2, maxDist + 1);
            int j    = i + dist;
            if (used.Contains(rooms[j].Id)) continue;

            var (corridor, branchList) = TryBuild(dungeon, rooms[i], rooms[j], north, rng, ref nextId);
            if (corridor is null) continue;

            dungeon.Corridors.Add(corridor);
            foreach (var b in branchList) dungeon.Corridors.Add(b);
            outsideBranches.AddRange(branchList);
            used.Add(rooms[i].Id);
            used.Add(rooms[j].Id);
        }
    }

    private static (Corridor? corridor, List<Corridor> branches) TryBuild(
        Dungeon dungeon, Room a, Room b, bool north, Random rng, ref int nextId)
    {
        int corridorId = nextId++;

        int xA = a.Bounds.X + a.Bounds.Width  / 2;
        int xB = b.Bounds.X + b.Bounds.Width  / 2;
        if (Math.Abs(xA - xB) < 3) return (null, new());

        int yCorr = north
            ? Math.Min(a.Bounds.Y, b.Bounds.Y) - 1
            : Math.Max(a.Bounds.Y + a.Bounds.Height - 1, b.Bounds.Y + b.Bounds.Height - 1) + 1;

        if (yCorr < 1 || yCorr >= dungeon.Height - 1) return (null, new());

        int yANear = north ? a.Bounds.Y - 1 : a.Bounds.Y + a.Bounds.Height;
        int yBNear = north ? b.Bounds.Y - 1 : b.Bounds.Y + b.Bounds.Height;

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
