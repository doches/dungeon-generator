using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

public static class OutsideCorridorBuilder
{
    private const double Probability = 0.25;

    public static void Build(Dungeon dungeon, List<Corridor> branches, Random rng, ref int nextId)
    {
        TryConnectSide(dungeon, GetSideRooms(dungeon, branches, north: true),  north: true,  rng, ref nextId);
        TryConnectSide(dungeon, GetSideRooms(dungeon, branches, north: false), north: false, rng, ref nextId);
    }

    private static List<Room> GetSideRooms(Dungeon dungeon, List<Corridor> branches, bool north)
        => branches
            .Where(b => north ? b.End.Y < b.Start.Y : b.End.Y > b.Start.Y)
            .Select(b => dungeon.Rooms.FirstOrDefault(r => r.AttachPoint == b.End))
            .OfType<Room>()
            .OrderBy(r => r.Bounds.X)
            .ToList();

    private static void TryConnectSide(
        Dungeon dungeon, List<Room> rooms, bool north, Random rng, ref int nextId)
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

            var corridor = TryBuild(dungeon, rooms[i], rooms[j], north, nextId);
            if (corridor is null) continue;

            dungeon.Corridors.Add(corridor);
            used.Add(rooms[i].Id);
            used.Add(rooms[j].Id);
            nextId++;
        }
    }

    private static Corridor? TryBuild(Dungeon dungeon, Room a, Room b, bool north, int id)
    {
        // Centre-column of each room (avoids the outermost wall columns)
        int xA = a.Bounds.X + a.Bounds.Width  / 2;
        int xB = b.Bounds.X + b.Bounds.Width  / 2;

        if (Math.Abs(xA - xB) < 3) return null; // rooms too close horizontally

        // y of the horizontal run: one tile beyond the outermost room edge on this side
        int yCorr = north
            ? Math.Min(a.Bounds.Y, b.Bounds.Y) - 1
            : Math.Max(a.Bounds.Y + a.Bounds.Height - 1, b.Bounds.Y + b.Bounds.Height - 1) + 1;

        if (yCorr < 1 || yCorr >= dungeon.Height - 1) return null;

        // Last corridor tile before each room's outer wall (used as Start/End for door-walking)
        int yANear = north ? a.Bounds.Y - 1 : a.Bounds.Y + a.Bounds.Height;
        int yBNear = north ? b.Bounds.Y - 1 : b.Bounds.Y + b.Bounds.Height;

        // Collect path tiles (no duplicates)
        var seen  = new HashSet<(int, int)>();
        var tiles = new List<Pt>();
        void Add(int x, int y) { if (seen.Add((x, y))) tiles.Add(new Pt(x, y)); }

        for (int y = Math.Min(yCorr, yANear); y <= Math.Max(yCorr, yANear); y++) Add(xA, y);
        for (int x = Math.Min(xA, xB); x <= Math.Max(xA, xB); x++)               Add(x, yCorr);
        for (int y = Math.Min(yCorr, yBNear); y <= Math.Max(yCorr, yBNear); y++) Add(xB, y);

        // Every tile must be Void — never overlap anything
        foreach (var pt in tiles)
        {
            if (pt.X < 1 || pt.Y < 1 || pt.X >= dungeon.Width - 1 || pt.Y >= dungeon.Height - 1)
                return null;
            if (dungeon.Grid[pt.X, pt.Y] != TileType.Void)
                return null;
        }

        // Paint
        foreach (var pt in tiles)
            dungeon.Grid[pt.X, pt.Y] = pt.Y == yCorr ? TileType.HorizCorridor : TileType.VertCorridor;

        var entryDir = new Pt(0, north ? 1 : -1);

        return new Corridor
        {
            Id            = id,
            Kind          = CorridorKind.Outside,
            Start         = new Pt(xA, yANear),
            End           = new Pt(xB, yBNear),
            Spine         = tiles,
            StartEntryDir = entryDir,
            EndEntryDir   = entryDir,
        };
    }
}
