using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

public static class DoorPlacer
{
    private static readonly (int dx, int dy)[] Cardinals = [(0,-1),(0,1),(-1,0),(1,0)];

    public static void PlaceAll(Dungeon dungeon, IReadOnlyList<BendInfo>? bends = null)
    {
        PaintCorridorWalls(dungeon);

        int doorId   = 0;
        // Tracks which wall faces already have a door: roomId → set of (dx,dy) walk directions.
        // Convention: (dx,dy) is the direction walked to reach this room's face.
        //   (+1, 0) → west face   (-1, 0) → east face
        //   ( 0,+1) → north face  ( 0,-1) → south face
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
                r => !r.IsAnchor && r.AttachPoint == new Pt(bend.JogX, bend.RoomY));

            int yTop = Math.Min(bend.Y1, bend.Y2);
            int yBot = Math.Max(bend.Y1, bend.Y2);
            int yMid = bend.RoomY + bend.RoomHeight / 2;

            var candidates = new List<(Pt pt, int dx, int dy)>();

            // West wall — walk east from jog column (midpoint first)
            candidates.Add((new Pt(bend.JogX, yMid), 1, 0));
            for (int y = bend.RoomY; y < bend.RoomY + bend.RoomHeight; y++)
                if (y != yMid) candidates.Add((new Pt(bend.JogX, y), 1, 0));

            if (room is not null)
            {
                int xMid = room.Bounds.X + room.Bounds.Width / 2;

                // North wall — walk south from upper spine segment
                candidates.Add((new Pt(xMid, yTop), 0, 1));
                for (int x = room.Bounds.X; x < room.Bounds.X + room.Bounds.Width; x++)
                    if (x != xMid) candidates.Add((new Pt(x, yTop), 0, 1));

                // South wall — walk north from lower spine segment
                candidates.Add((new Pt(xMid, yBot), 0, -1));
                for (int x = room.Bounds.X; x < room.Bounds.X + room.Bounds.Width; x++)
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
        // Map each wall tile position → the room that owns it
        var wallToRoom = new Dictionary<(int, int), Room>();
        foreach (var room in dungeon.Rooms)
            foreach (var rect in room.Tiles)
                for (int x = rect.X; x < rect.X + rect.Width; x++)
                    for (int y = rect.Y; y < rect.Y + rect.Height; y++)
                        if (dungeon.Grid[x, y] == TileType.Wall)
                            wallToRoom[(x, y)] = room;

        // Collect adjacent wall pairs from different rooms, grouped by room pair
        // Only scan dx=+1 and dy=+1 to avoid counting each pair twice
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
            // Sort for consistent centre selection
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var (x, y, nx, ny, dx, dy) = list[list.Count / 2];

            var roomA = wallToRoom[(x, y)];
            var roomB = wallToRoom[(nx, ny)];

            // Verify floor tiles exist on both interiors
            int ax = x - dx, ay = y - dy;
            int bx = nx + dx, by = ny + dy;
            if (ax < 0 || ay < 0 || ax >= dungeon.Width  || ay >= dungeon.Height) continue;
            if (bx < 0 || by < 0 || bx >= dungeon.Width  || by >= dungeon.Height) continue;
            if (dungeon.Grid[ax, ay] != TileType.Floor) continue;
            if (dungeon.Grid[bx, by] != TileType.Floor) continue;

            // usedWalls convention: entering roomA from roomB's side = walking (-dx,-dy)
            //                       entering roomB from roomA's side = walking ( dx, dy)
            if (!CanUseWall(usedWalls, roomA.Id, -dx, -dy)) continue;
            if (!CanUseWall(usedWalls, roomB.Id,  dx,  dy)) continue;

            dungeon.Grid[x, y]   = TileType.Door;
            dungeon.Grid[nx, ny] = TileType.Floor; // open the second wall into roomB

            MarkWallUsed(usedWalls, roomA.Id, -dx, -dy);
            MarkWallUsed(usedWalls, roomB.Id,  dx,  dy);

            var door = new Door
            {
                Id      = doorId++,
                Position = new Pt(x, y),
                RoomAId  = roomA.Id,
                RoomBId  = roomB.Id,
            };
            roomA.DoorPositions.Add(new Pt(x, y));
            roomB.DoorPositions.Add(new Pt(x, y));
            dungeon.Doors.Add(door);
        }
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
            if (tile == TileType.Floor) return;
            if (tile == TileType.Door)  return;

            if (tile == TileType.Wall && HasFloorNeighbour(dungeon, new Pt(x, y)))
            {
                var room = FindRoomAtDoor(dungeon, new Pt(x, y));

                if (room is not null)
                {
                    if (!CanUseWall(usedWalls, room.Id, dx, dy)) return;
                    MarkWallUsed(usedWalls, room.Id, dx, dy);
                }

                dungeon.Grid[x, y] = TileType.Door;

                var door = new Door { Id = doorId++, Position = new Pt(x, y), CorridorId = corridorId };
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
                foreach (var rect in room.Tiles)
                    if (nx >= rect.X && nx < rect.X + rect.Width
                     && ny >= rect.Y && ny < rect.Y + rect.Height)
                        return room;
        }
        return null;
    }
}
