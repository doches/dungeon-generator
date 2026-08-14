using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

public static class DoorPlacer
{
    private static readonly (int dx, int dy)[] Cardinals = [(0,-1),(0,1),(-1,0),(1,0)];

    public static void PlaceAll(Dungeon dungeon, IReadOnlyList<BendInfo>? bends = null)
    {
        PaintCorridorWalls(dungeon);

        int doorId   = 0;
        var usedWalls = new Dictionary<int, HashSet<(int, int)>>();

        foreach (var corridor in dungeon.Corridors)
        {
            if (corridor.Kind == CorridorKind.Spine)
            {
                WalkAndPlaceDoor(dungeon, corridor.Start, -1, 0, corridor.Id, ref doorId, usedWalls);
                WalkAndPlaceDoor(dungeon, corridor.End,    1, 0, corridor.Id, ref doorId, usedWalls);
            }
            else if (corridor.Kind == CorridorKind.Branch)
            {
                bool goesNorth = corridor.End.Y < corridor.Start.Y;
                int dy = goesNorth ? -1 : 1;
                WalkAndPlaceDoor(dungeon, corridor.End, 0, dy, corridor.Id, ref doorId, usedWalls);
            }
            else if (corridor.Kind == CorridorKind.Outside)
            {
                if (corridor.StartEntryDir is { } sDir)
                    WalkAndPlaceDoor(dungeon, corridor.Start, sDir.X, sDir.Y, corridor.Id, ref doorId, usedWalls);
                if (corridor.EndEntryDir is { } eDir)
                    WalkAndPlaceDoor(dungeon, corridor.End, eDir.X, eDir.Y, corridor.Id, ref doorId, usedWalls);
            }
        }

        if (bends is not null)
            PlaceBendRoomDoors(dungeon, bends, ref doorId, usedWalls);

        PlaceSharedWallDoors(dungeon, ref doorId, usedWalls);

        RemoveStrayDoors(dungeon);

        BuildConnections(dungeon);
    }

    // ── Bend room doors ───────────────────────────────────────────────────────
    private static void PlaceBendRoomDoors(
        Dungeon dungeon, IReadOnlyList<BendInfo> bends, ref int doorId,
        Dictionary<int, HashSet<(int, int)>> usedWalls)
    {
        int spineId = dungeon.MainSpine?.Id ?? 0;

        foreach (var bend in bends)
        {
            var room = dungeon.Rooms.FirstOrDefault(
                r => !r.IsAnchor && r.Type != "Corridor" && r.AttachPoint == new Pt(bend.JogX, bend.RoomY));

            int yTop = Math.Min(bend.Y1, bend.Y2);
            int yBot = Math.Max(bend.Y1, bend.Y2);
            int yMid = bend.RoomY + bend.RoomHeight / 2;
            int yTopWalkRow = yTop + bend.SpineWidth - 1;

            var candidates = new List<(Pt pt, int dx, int dy)>();

            // West wall — walk east from jog column (midpoint first)
            candidates.Add((new Pt(bend.JogX, yMid), 1, 0));
            for (int y = bend.RoomY; y < bend.RoomY + bend.RoomHeight; y++)
                if (y != yMid) candidates.Add((new Pt(bend.JogX, y), 1, 0));

            if (room is not null)
            {
                int xMid = room.X + room.Width / 2;

                // North wall — walk south from bottom row of upper spine segment
                candidates.Add((new Pt(xMid, yTopWalkRow), 0, 1));
                for (int x = room.X; x < room.X + room.Width; x++)
                    if (x != xMid) candidates.Add((new Pt(x, yTopWalkRow), 0, 1));

                // South wall — walk north from top row of lower spine segment
                candidates.Add((new Pt(xMid, yBot), 0, -1));
                for (int x = room.X; x < room.X + room.Width; x++)
                    if (x != xMid) candidates.Add((new Pt(x, yBot), 0, -1));
            }

            foreach (var (pt, dx, dy) in candidates)
            {
                int before = dungeon.Doors.Count;
                WalkAndPlaceDoor(dungeon, pt, dx, dy, spineId, ref doorId, usedWalls);
                if (dungeon.Doors.Count > before) break;
            }
        }
    }

    // ── Shared-wall doors (room adjacent to room) ─────────────────────────────
    private static void PlaceSharedWallDoors(
        Dungeon dungeon, ref int doorId,
        Dictionary<int, HashSet<(int, int)>> usedWalls)
    {
        // Map each wall tile position → the room that owns it (skip Corridor rooms)
        var wallToRoom = new Dictionary<(int, int), Room>();
        foreach (var room in dungeon.Rooms)
        {
            if (room.Type == "Corridor") continue;
            for (int x = room.X; x < room.X + room.Width; x++)
            {
                TryAddWall(dungeon, x, room.Y,                   room, wallToRoom);
                TryAddWall(dungeon, x, room.Y + room.Height - 1, room, wallToRoom);
            }
            for (int y = room.Y + 1; y < room.Y + room.Height - 1; y++)
            {
                TryAddWall(dungeon, room.X,                  y, room, wallToRoom);
                TryAddWall(dungeon, room.X + room.Width - 1, y, room, wallToRoom);
            }
        }

        // Collect adjacent wall pairs from different rooms, grouped by room pair
        var groups = new Dictionary<(int, int), List<(int x, int y, int nx, int ny, int dx, int dy)>>();

        foreach (var ((x, y), roomA) in wallToRoom)
        {
            foreach (var (dx, dy) in new[] { (1, 0), (0, 1) })
            {
                int nx = x + dx, ny = y + dy;
                if (!wallToRoom.TryGetValue((nx, ny), out var roomB)) continue;
                if (roomB.Id == roomA.Id) continue;

                var key = (Math.Min(roomA.Id, roomB.Id), Math.Max(roomA.Id, roomB.Id));
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new();
                list.Add((x, y, nx, ny, dx, dy));
            }
        }

        // For each adjacent room pair, pick the centre-most candidate and place one door
        foreach (var list in groups.Values)
        {
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var (x, y, nx, ny, dx, dy) = list[list.Count / 2];

            var roomA = wallToRoom[(x, y)];
            var roomB = wallToRoom[(nx, ny)];

            int ax = x - dx, ay = y - dy;
            int bx = nx + dx, by = ny + dy;
            if (ax < 0 || ay < 0 || ax >= dungeon.Width  || ay >= dungeon.Height) continue;
            if (bx < 0 || by < 0 || bx >= dungeon.Width  || by >= dungeon.Height) continue;
            if (dungeon.Grid[ax, ay] != TileType.Floor) continue;
            if (dungeon.Grid[bx, by] != TileType.Floor) continue;

            if (!CanUseWall(usedWalls, roomA.Id, -dx, -dy)) continue;
            if (!CanUseWall(usedWalls, roomB.Id,  dx,  dy)) continue;

            dungeon.Grid[x, y]   = TileType.Door;
            dungeon.Grid[nx, ny] = TileType.Floor;

            MarkWallUsed(usedWalls, roomA.Id, -dx, -dy);
            MarkWallUsed(usedWalls, roomB.Id,  dx,  dy);

            var door = new Door
            {
                Id       = doorId++,
                Position = new Pt(x, y),
                RoomAId  = roomA.Id,
                RoomBId  = roomB.Id,
                Face     = dx == 1 ? DoorFace.East : DoorFace.South,
            };
            roomA.DoorPositions.Add(new Pt(x, y));
            roomB.DoorPositions.Add(new Pt(x, y));
            dungeon.Doors.Add(door);
        }
    }

    private static void TryAddWall(Dungeon dungeon, int x, int y, Room room, Dictionary<(int, int), Room> wallToRoom)
    {
        if (x < 0 || y < 0 || x >= dungeon.Width || y >= dungeon.Height) return;
        if (dungeon.Grid[x, y] == TileType.Wall)
            wallToRoom[(x, y)] = room;
    }

    // ── WalkAndPlaceDoor ──────────────────────────────────────────────────────
    private static void WalkAndPlaceDoor(
        Dungeon dungeon, Pt from, int dx, int dy, int corridorId, ref int doorId,
        Dictionary<int, HashSet<(int, int)>> usedWalls)
    {
        int x = from.X + dx, y = from.Y + dy;
        int maxSteps = Math.Max(dungeon.Width, dungeon.Height);

        for (int step = 0; step < maxSteps; step++)
        {
            if (x < 0 || y < 0 || x >= dungeon.Width || y >= dungeon.Height) return;

            var tile = dungeon.Grid[x, y];
            if (tile == TileType.Floor)    return;
            if (tile == TileType.Door)     return;
            if (tile == TileType.WideDoor) return;

            if (tile == TileType.Wall && HasFloorNeighbour(dungeon, new Pt(x, y)))
            {
                var room = FindRoomAtDoor(dungeon, new Pt(x, y));

                if (room is not null)
                {
                    if (!CanUseWall(usedWalls, room.Id, dx, dy)) return;
                    MarkWallUsed(usedWalls, room.Id, dx, dy);
                }

                dungeon.Grid[x, y] = TileType.Door;

                var door = new Door
                {
                    Id         = doorId++,
                    Position   = new Pt(x, y),
                    CorridorId = corridorId,
                    Face       = room is not null ? FaceFromWalkDir(dx, dy) : null,
                };
                if (room is not null)
                {
                    door.RoomAId = room.Id;
                    room.DoorPositions.Add(new Pt(x, y));
                }
                dungeon.Doors.Add(door);

                var branch = dungeon.Corridors.FirstOrDefault(c => c.Id == corridorId);
                if (branch is not null && branch.Kind == CorridorKind.Branch)
                    branch.AttachedRoomId = room?.Id;

                return;
            }

            x += dx;
            y += dy;
        }
    }

    // Walking south (dy=+1) → hits north wall; walking north (dy=-1) → hits south wall.
    // Walking east (dx=+1) → hits west wall;  walking west (dx=-1) → hits east wall.
    private static DoorFace FaceFromWalkDir(int dx, int dy) => (dx, dy) switch
    {
        ( 0,  1) => DoorFace.North,
        ( 0, -1) => DoorFace.South,
        ( 1,  0) => DoorFace.West,
        _        => DoorFace.East,
    };

    // ── Stray door removal ────────────────────────────────────────────────────
    private static void RemoveStrayDoors(Dungeon dungeon)
    {
        static bool Traversable(TileType t) =>
            t == TileType.Floor         || t == TileType.Door
         || t == TileType.SpineCorridor || t == TileType.HorizCorridor
         || t == TileType.VertCorridor  || t == TileType.WideDoor;

        bool TileOk(int x, int y)
        {
            if (x < 0 || y < 0 || x >= dungeon.Width || y >= dungeon.Height) return false;
            return Traversable(dungeon.Grid[x, y]);
        }

        var stray = new List<Pt>();
        for (int x = 0; x < dungeon.Width; x++)
            for (int y = 0; y < dungeon.Height; y++)
            {
                if (dungeon.Grid[x, y] != TileType.Door) continue;
                bool horizOk = TileOk(x - 1, y) && TileOk(x + 1, y);
                bool vertOk  = TileOk(x, y - 1) && TileOk(x, y + 1);
                if (!horizOk && !vertOk) stray.Add(new Pt(x, y));
            }

        foreach (var pt in stray)
        {
            dungeon.Grid[pt.X, pt.Y] = TileType.Wall;
            foreach (var room in dungeon.Rooms)
                room.DoorPositions.RemoveAll(d => d == pt);
            dungeon.Doors.RemoveAll(d => d.Position == pt);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static bool CanUseWall(Dictionary<int, HashSet<(int, int)>> used, int roomId, int dx, int dy)
        => !used.TryGetValue(roomId, out var dirs) || !dirs.Contains((dx, dy));

    private static void MarkWallUsed(Dictionary<int, HashSet<(int, int)>> used, int roomId, int dx, int dy)
    {
        if (!used.TryGetValue(roomId, out var dirs))
            used[roomId] = dirs = new();
        dirs.Add((dx, dy));
    }

    private static void PaintCorridorWalls(Dungeon dungeon)
    {
        foreach (var corridor in dungeon.Corridors)
            foreach (var tile in corridor.Spine)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = tile.X + dx, ny = tile.Y + dy;
                        if (nx < 0 || ny < 0 || nx >= dungeon.Width || ny >= dungeon.Height) continue;
                        if (dungeon.Grid[nx, ny] == TileType.Void)
                            dungeon.Grid[nx, ny] = TileType.Wall;
                    }
    }

    private static void BuildConnections(Dungeon dungeon)
    {
        foreach (var door in dungeon.Doors)
        {
            if (door.RoomAId.HasValue && door.CorridorId.HasValue)
            {
                var room = dungeon.Rooms.FirstOrDefault(r => r.Id == door.RoomAId);
                if (room is not null && !room.ConnectedIds.Contains(door.CorridorId.Value))
                    room.ConnectedIds.Add(door.CorridorId.Value);
            }

            if (door.RoomAId.HasValue && door.RoomBId.HasValue)
            {
                var a = dungeon.Rooms.FirstOrDefault(r => r.Id == door.RoomAId);
                var b = dungeon.Rooms.FirstOrDefault(r => r.Id == door.RoomBId);
                if (a is not null && !a.ConnectedIds.Contains(door.RoomBId.Value))
                    a.ConnectedIds.Add(door.RoomBId.Value);
                if (b is not null && !b.ConnectedIds.Contains(door.RoomAId.Value))
                    b.ConnectedIds.Add(door.RoomAId.Value);
            }
        }
    }

    private static bool HasFloorNeighbour(Dungeon dungeon, Pt p)
    {
        foreach (var (dx, dy) in Cardinals)
        {
            int nx = p.X + dx, ny = p.Y + dy;
            if (nx < 0 || ny < 0 || nx >= dungeon.Width || ny >= dungeon.Height) continue;
            if (dungeon.Grid[nx, ny] == TileType.Floor) return true;
        }
        return false;
    }

    private static Room? FindRoomAtDoor(Dungeon dungeon, Pt doorPt)
    {
        foreach (var (dx, dy) in Cardinals)
        {
            int nx = doorPt.X + dx, ny = doorPt.Y + dy;
            if (nx < 0 || ny < 0 || nx >= dungeon.Width || ny >= dungeon.Height) continue;
            if (dungeon.Grid[nx, ny] != TileType.Floor) continue;
            foreach (var room in dungeon.Rooms)
                if (room.Type != "Corridor"
                 && nx >= room.X && nx < room.X + room.Width
                 && ny >= room.Y && ny < room.Y + room.Height)
                    return room;
        }
        return null;
    }
}
