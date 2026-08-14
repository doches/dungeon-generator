using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

public enum AttachDirection { North, South, East, West }

public static class RoomBuilder
{
    public static Room? BuildForBranch(
        Dungeon dungeon, Corridor branch, RoomTypeConfig typeCfg, int id, Random rng)
    {
        bool goesNorth = branch.End.Y < branch.Start.Y;
        AttachDirection dir = goesNorth ? AttachDirection.North : AttachDirection.South;
        return TryBuild(dungeon, branch.End, dir, typeCfg, id, rng, maxRetries: 5);
    }

    public static Room? BuildBendRoom(
        Dungeon dungeon, BendInfo bend, RoomTypeConfig typeCfg, int id, Random rng)
    {
        int h = bend.RoomHeight;
        if (h < typeCfg.MinHeight) return null;

        int x = bend.JogX + 1;
        int y = bend.RoomY;

        int minW = Math.Max(typeCfg.MinWidth, 4);
        int maxW = Math.Min(typeCfg.MaxWidth, dungeon.Width - x - 2);
        if (maxW < minW || x >= dungeon.Width - 2) return null;

        int w = rng.Next(minW, maxW + 1);
        // Clamp height to config range
        h = Math.Clamp(h, typeCfg.MinHeight, typeCfg.MaxHeight);

        if (CollisionCheck(dungeon, x, y, w, h)) return null;

        PaintRoom(dungeon, x, y, w, h);

        return new Room
        {
            Id          = id,
            Type        = typeCfg.Name,
            X           = x,
            Y           = y,
            Width       = w,
            Height      = h,
            IsAnchor    = false,
            AttachPoint = new Pt(bend.JogX, y),
        };
    }

    public static Room? BuildAnchorFore(
        Dungeon dungeon, int spineXMin, int spineY, RoomTypeConfig typeCfg, int id, Random rng)
        => TryBuild(dungeon, new Pt(spineXMin, spineY), AttachDirection.West, typeCfg, id, rng, maxRetries: 5);

    public static Room? BuildAnchorAft(
        Dungeon dungeon, int spineXMax, int spineY, RoomTypeConfig typeCfg, int id, Random rng)
        => TryBuild(dungeon, new Pt(spineXMax, spineY), AttachDirection.East, typeCfg, id, rng, maxRetries: 5);

    private static Room? TryBuild(
        Dungeon dungeon, Pt attach, AttachDirection dir,
        RoomTypeConfig cfg, int id, Random rng, int maxRetries)
    {
        int minW = cfg.MinWidth, maxW = cfg.MaxWidth;
        int minH = cfg.MinHeight, maxH = cfg.MaxHeight;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            int w = rng.Next(minW, maxW + 1);
            int h = rng.Next(minH, maxH + 1);
            var (x, y) = Position(attach, dir, w, h, dungeon.Width, dungeon.Height);

            if (CollisionCheck(dungeon, x, y, w, h))
            {
                minW = Math.Max(3, minW - 1);
                minH = Math.Max(3, minH - 1);
                maxW = Math.Max(minW, maxW - 1);
                maxH = Math.Max(minH, maxH - 1);
                continue;
            }

            PaintRoom(dungeon, x, y, w, h);

            return new Room
            {
                Id          = id,
                Type        = cfg.Name,
                X           = x,
                Y           = y,
                Width       = w,
                Height      = h,
                IsAnchor    = cfg.IsAnchorFore || cfg.IsAnchorAft,
                AttachPoint = attach,
            };
        }

        return null;
    }

    private static (int x, int y) Position(Pt attach, AttachDirection dir, int w, int h, int mapW, int mapH)
    {
        int x, y;
        switch (dir)
        {
            case AttachDirection.North:
                x = attach.X - w / 2;
                y = attach.Y - h;
                break;
            case AttachDirection.South:
                x = attach.X - w / 2;
                y = attach.Y + 1;
                break;
            case AttachDirection.West:
                x = attach.X - w;
                y = attach.Y - h / 2;
                break;
            default: // East
                x = attach.X + 1;
                y = attach.Y - h / 2;
                break;
        }

        x = Math.Clamp(x, 1, mapW - w - 1);
        y = Math.Clamp(y, 1, mapH - h - 1);
        return (x, y);
    }

    private static void PaintRoom(Dungeon dungeon, int x, int y, int w, int h)
    {
        for (int rx = x; rx < x + w; rx++)
            for (int ry = y; ry < y + h; ry++)
            {
                bool isWall = rx == x || rx == x + w - 1 || ry == y || ry == y + h - 1;
                dungeon.Grid[rx, ry] = isWall ? TileType.Wall : TileType.Floor;
            }
    }

    public static bool CollisionCheck(Dungeon dungeon, int x, int y, int w, int h)
    {
        if (x < 1 || y < 1 || x + w > dungeon.Width - 1 || y + h > dungeon.Height - 1)
            return true;

        for (int rx = x; rx < x + w; rx++)
            for (int ry = y; ry < y + h; ry++)
                if (IsOccupied(dungeon.Grid[rx, ry])) return true;

        return false;
    }

    private static bool IsOccupied(TileType t) =>
        t == TileType.Floor || t == TileType.Wall
     || t == TileType.SpineCorridor || t == TileType.VertCorridor
     || t == TileType.HorizCorridor || t == TileType.Door;
}
