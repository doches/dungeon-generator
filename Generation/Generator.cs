using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

public static class Generator
{
    public static Dungeon Generate(DungeonConfig cfg)
    {
        var rng = new Random();
        var dungeon = new Dungeon
        {
            Width = cfg.Width,
            Height = cfg.Height,
            Grid = new TileType[cfg.Width, cfg.Height],
            Rooms = new(),
            Corridors = new(),
            Doors = new(),
        };

        // Reserve margins for anchor rooms.
        // spineXMin/spineXMax are the first/last spine tiles; anchor rooms grow outward from them.
        int spineXMin = 3;
        int spineXMax = cfg.Width - 4;

        var foreCfg = cfg.RoomTypes.FirstOrDefault(r => r.IsAnchorFore);
        var aftCfg  = cfg.RoomTypes.FirstOrDefault(r => r.IsAnchorAft);
        if (foreCfg is not null) spineXMin = foreCfg.MaxWidth + 2;
        if (aftCfg  is not null) spineXMax = cfg.Width - aftCfg.MaxWidth - 3;

        // 1. Spine
        var spine = SpineBuilder.Build(dungeon, spineXMin, spineXMax);
        dungeon.MainSpine = spine;
        dungeon.Corridors.Add(spine);

        // 2. Branches
        var branches = BranchBuilder.Build(dungeon, spine, cfg, rng);
        dungeon.Corridors.AddRange(branches);

        // 3. Anchor rooms
        int nextRoomId = 0;
        int spineY = spine.Start.Y;

        if (foreCfg is not null)
        {
            var room = RoomBuilder.BuildAnchorFore(dungeon, spineXMin, spineY, foreCfg, nextRoomId++, rng);
            if (room is not null) dungeon.Rooms.Add(room);
        }
        if (aftCfg is not null)
        {
            var room = RoomBuilder.BuildAnchorAft(dungeon, spineXMax, spineY, aftCfg, nextRoomId++, rng);
            if (room is not null) dungeon.Rooms.Add(room);
        }

        // 4. Branch rooms
        var weightedTypes = cfg.RoomTypes.Where(r => r.Weight > 0).ToList();
        int totalWeight = weightedTypes.Sum(r => r.Weight);

        foreach (var branch in branches)
        {
            var typeCfg = totalWeight > 0
                ? PickType(weightedTypes, totalWeight, rng)
                : new RoomTypeConfig { Name = "Room", MinWidth = 5, MaxWidth = 9, MinHeight = 4, MaxHeight = 7 };

            var room = RoomBuilder.BuildForBranch(dungeon, branch, typeCfg, nextRoomId++, rng);
            if (room is not null) dungeon.Rooms.Add(room);
        }

        // 5. Doors + corridor walls + connections
        DoorPlacer.PlaceAll(dungeon);

        return dungeon;
    }

    private static RoomTypeConfig PickType(List<RoomTypeConfig> types, int total, Random rng)
    {
        int roll = rng.Next(total);
        int cumulative = 0;
        foreach (var t in types)
        {
            cumulative += t.Weight;
            if (roll < cumulative) return t;
        }
        return types[^1];
    }
}
