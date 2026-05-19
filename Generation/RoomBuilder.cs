using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

public enum AttachDirection { North, South, East, West }

public static class RoomBuilder
{
    public static Room? BuildForBranch(
        Dungeon dungeon, Corridor branch, RoomTypeConfig typeCfg, int id, Random rng)
    {
        Pt branchEnd = branch.End;
        bool goesNorth = branch.End.Y < branch.Start.Y;
        AttachDirection dir = goesNorth ? AttachDirection.North : AttachDirection.South;
        return TryBuild(dungeon, branchEnd, dir, typeCfg, id, rng, maxRetries: 5);
    }

    // Attach point is the spine start tile; room grows west from it
    public static Room? BuildAnchorFore(
        Dungeon dungeon, int spineXMin, int spineY, RoomTypeConfig typeCfg, int id, Random rng)
    {
        var attach = new Pt(spineXMin, spineY);
        return TryBuild(dungeon, attach, AttachDirection.West, typeCfg, id, rng, maxRetries: 5);
    }

    // Attach point is the spine end tile; room grows east from it
    public static Room? BuildAnchorAft(
        Dungeon dungeon, int spineXMax, int spineY, RoomTypeConfig typeCfg, int id, Random rng)
    {
        var attach = new Pt(spineXMax, spineY);
        return TryBuild(dungeon, attach, AttachDirection.East, typeCfg, id, rng, maxRetries: 5);
    }

    private static Room? TryBuild(
        Dungeon dungeon, Pt attach, AttachDirection dir,
        RoomTypeConfig cfg, int id, Random rng, int maxRetries)
    {
        int minW = cfg.MinWidth, maxW = cfg.MaxWidth;
        int minH = cfg.MinHeight, maxH = cfg.MaxHeight;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            int baseW = rng.Next(minW, maxW + 1);
            int baseH = rng.Next(minH, maxH + 1);

            Rect baseRect = PositionBase(attach, dir, baseW, baseH, dungeon.Width, dungeon.Height);

            if (CollisionCheck(dungeon, baseRect))
            {
                minW = Math.Max(3, minW - 1);
                minH = Math.Max(3, minH - 1);
                maxW = Math.Max(minW, maxW - 1);
                maxH = Math.Max(minH, maxH - 1);
                continue;
            }

            var rects = new List<Rect> { baseRect };
            AddWings(dungeon, rects, baseRect, rng);

            PaintRoom(dungeon, rects);

            var bounds = ComputeBounds(rects);
            var room = new Room
            {
                Id = id,
                Type = cfg.Name,
                Tiles = rects,
                Bounds = bounds,
                IsAnchor = cfg.IsAnchorFore || cfg.IsAnchorAft,
                AttachPoint = attach,
            };
            return room;
        }

        return null;
    }

    private static Rect PositionBase(Pt attach, AttachDirection dir, int w, int h, int mapW, int mapH)
    {
        int x, y;
        switch (dir)
        {
            case AttachDirection.North:
                // Room south wall at attach.Y, grows upward
                x = attach.X - w / 2;
                y = attach.Y - h;
                break;
            case AttachDirection.South:
                // Room north wall at attach.Y + 1
                x = attach.X - w / 2;
                y = attach.Y + 1;
                break;
            case AttachDirection.West:
                // Room east wall at attach.X - 1, grows left
                x = attach.X - w;
                y = attach.Y - h / 2;
                break;
            default: // East
                // Room west wall at attach.X + 1, grows right
                x = attach.X + 1;
                y = attach.Y - h / 2;
                break;
        }

        x = Math.Clamp(x, 1, mapW - w - 1);
        y = Math.Clamp(y, 1, mapH - h - 1);
        return new Rect(x, y, w, h);
    }

    private static void AddWings(Dungeon dungeon, List<Rect> rects, Rect baseRect, Random rng)
    {
        int wingCount = rng.Next(0, 3);
        var usedSides = new HashSet<int>();

        for (int i = 0; i < wingCount; i++)
        {
            int side = rng.Next(4);
            if (usedSides.Contains(side)) continue;

            Rect? wing = TryMakeWing(dungeon, baseRect, side, rng);
            if (wing is not null)
            {
                rects.Add(wing);
                usedSides.Add(side);
            }
        }
    }

    private static Rect? TryMakeWing(Dungeon dungeon, Rect b, int side, Random rng)
    {
        int wingX, wingY, wingW, wingH;

        if (side == 0 || side == 1) // North or South
        {
            // Both dimensions ≥ 3: wingW so centre col is interior, wingH so there's a middle row
            wingW = rng.Next(3, Math.Max(4, b.Width * 2 / 3 + 1));
            wingH = rng.Next(3, Math.Max(4, b.Height / 2 + 1));
            wingX = rng.Next(b.X, Math.Max(b.X + 1, b.X + b.Width - wingW));
            // Overlap base by 1 row so shared edge is interior in the union
            wingY = side == 0 ? b.Y - wingH + 1 : b.Y + b.Height - 1;
        }
        else // East or West
        {
            // Both dimensions ≥ 3: wingH so centre row is interior, wingW so there's a middle col
            wingW = rng.Next(3, Math.Max(4, b.Width / 2 + 1));
            wingH = rng.Next(3, Math.Max(4, b.Height * 2 / 3 + 1));
            // Overlap base by 1 column
            wingX = side == 3 ? b.X - wingW + 1 : b.X + b.Width - 1;
            wingY = rng.Next(b.Y, Math.Max(b.Y + 1, b.Y + b.Height - wingH));
        }

        wingX = Math.Clamp(wingX, 1, dungeon.Width - wingW - 1);
        wingY = Math.Clamp(wingY, 1, dungeon.Height - wingH - 1);

        var rect = new Rect(wingX, wingY, wingW, wingH);
        // Collision check excluding the base rect (overlap is intentional)
        return CollisionCheckExcluding(dungeon, rect, b) ? null : rect;
    }

    private static void PaintRoom(Dungeon dungeon, List<Rect> rects)
    {
        // Collect the union of all tiles
        var allTiles = new HashSet<(int x, int y)>();
        foreach (var r in rects)
            for (int x = r.X; x < r.X + r.Width; x++)
                for (int y = r.Y; y < r.Y + r.Height; y++)
                    allTiles.Add((x, y));

        // A tile is Wall if any cardinal neighbour is outside the union; else Floor
        foreach (var (x, y) in allTiles)
        {
            bool isWall = !allTiles.Contains((x - 1, y))
                       || !allTiles.Contains((x + 1, y))
                       || !allTiles.Contains((x, y - 1))
                       || !allTiles.Contains((x, y + 1));
            dungeon.Grid[x, y] = isWall ? TileType.Wall : TileType.Floor;
        }
    }

    public static bool CollisionCheck(Dungeon dungeon, Rect r)
    {
        if (r.X < 1 || r.Y < 1
         || r.X + r.Width > dungeon.Width - 1
         || r.Y + r.Height > dungeon.Height - 1)
            return true;

        for (int x = r.X; x < r.X + r.Width; x++)
            for (int y = r.Y; y < r.Y + r.Height; y++)
                if (IsOccupied(dungeon.Grid[x, y])) return true;

        return false;
    }

    private static bool CollisionCheckExcluding(Dungeon dungeon, Rect r, Rect exclude)
    {
        if (r.X < 1 || r.Y < 1
         || r.X + r.Width > dungeon.Width - 1
         || r.Y + r.Height > dungeon.Height - 1)
            return true;

        for (int x = r.X; x < r.X + r.Width; x++)
        {
            for (int y = r.Y; y < r.Y + r.Height; y++)
            {
                // The overlap row/col shared with the base is expected — skip it
                if (x >= exclude.X && x < exclude.X + exclude.Width
                 && y >= exclude.Y && y < exclude.Y + exclude.Height)
                    continue;
                if (IsOccupied(dungeon.Grid[x, y])) return true;
            }
        }
        return false;
    }

    private static bool IsOccupied(TileType t) =>
        t == TileType.Floor || t == TileType.Wall
     || t == TileType.SpineCorridor || t == TileType.VertCorridor
     || t == TileType.HorizCorridor || t == TileType.Door;

    private static BoundingBox ComputeBounds(List<Rect> rects)
    {
        int minX = rects.Min(r => r.X);
        int minY = rects.Min(r => r.Y);
        int maxX = rects.Max(r => r.X + r.Width);
        int maxY = rects.Max(r => r.Y + r.Height);
        return new BoundingBox(minX, minY, maxX - minX, maxY - minY);
    }
}
