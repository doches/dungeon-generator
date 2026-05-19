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
        var (spine, yAtX, bends, jogCols) =
            SpineBuilder.Build(dungeon, spineXMin, spineXMax, cfg, rng);
        dungeon.MainSpine = spine;
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
        var deadBranches = branches.Where(b => !branchesWithRooms.Contains(b.End)).ToList();
        foreach (var dead in deadBranches)
        {
            foreach (var pt in dead.Spine)
                if (dungeon.Grid[pt.X, pt.Y] == TileType.VertCorridor)
                    dungeon.Grid[pt.X, pt.Y] = TileType.Void;
            dungeon.Corridors.Remove(dead);
        }

        // 5b. Prune rooms with fewer than 5 floor tiles
        var prunedIds          = new HashSet<int>();
        var prunedAttachPoints = new HashSet<Pt>();
        foreach (var room in dungeon.Rooms.ToList())
        {
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

        if (prunedIds.Count > 0)
        {
            // Cascade-prune branch corridors whose room was just removed
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

            // Strip door records referencing pruned rooms (defensive — doors placed after this step)
            dungeon.Doors.RemoveAll(d =>
                (d.RoomAId.HasValue && prunedIds.Contains(d.RoomAId.Value)) ||
                (d.RoomBId.HasValue && prunedIds.Contains(d.RoomBId.Value)));
        }

        // 6. Outside corridors: low-probability horizontal loops along the north/south sides
        int nextCorridorId = dungeon.Corridors.Max(c => c.Id) + 1;
        OutsideCorridorBuilder.Build(dungeon, branches, rng, ref nextCorridorId);

        // 7. Doors, corridor walls, connections
        DoorPlacer.PlaceAll(dungeon, bends);

        return dungeon;
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
}
