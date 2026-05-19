using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

public static class DoorPlacer
{
    private static readonly (int dx, int dy)[] Cardinals = [(0,-1),(0,1),(-1,0),(1,0)];

    public static void PlaceAll(Dungeon dungeon, IReadOnlyList<BendInfo>? bends = null)
    {
        // Corridor wall borders must be in place before we can detect junction walls
        PaintCorridorWalls(dungeon);

        int doorId = 0;
        // Tracks which wall faces already have a door: roomId → set of (dx,dy) walk directions.
        // Each unique (dx,dy) maps to one face: (+1,0)=west, (-1,0)=east, (0,+1)=north, (0,-1)=south.
        var usedWalls = new Dictionary<int, HashSet<(int, int)>>();

        foreach (var corridor in dungeon.Corridors)
        {
            if (corridor.Kind == CorridorKind.Spine)
            {
                // Walk outward from each spine end to find the anchor room wall
                WalkAndPlaceDoor(dungeon, corridor.Start, -1, 0, corridor.Id, ref doorId, usedWalls);
                WalkAndPlaceDoor(dungeon, corridor.End,    1, 0, corridor.Id, ref doorId, usedWalls);
            }
            else
            {
                // Branch: one step past the end tile in the branch direction
                bool goesNorth = corridor.End.Y < corridor.Start.Y;
                int dy = goesNorth ? -1 : 1;
                WalkAndPlaceDoor(dungeon, corridor.End, 0, dy, corridor.Id, ref doorId, usedWalls);
            }
        }

        if (bends is not null)
            PlaceBendRoomDoors(dungeon, bends, ref doorId, usedWalls);

        BuildConnections(dungeon);
    }

    // For each bend, try all three adjoining walls (west, north, south) in preference order.
    private static void PlaceBendRoomDoors(
        Dungeon dungeon, IReadOnlyList<BendInfo> bends, ref int doorId,
        Dictionary<int, HashSet<(int, int)>> usedWalls)
    {
        int spineId = dungeon.MainSpine?.Id ?? 0;

        foreach (var bend in bends)
        {
            // Find the room placed at this bend (AttachPoint set in BuildBendRoom)
            var room = dungeon.Rooms.FirstOrDefault(
                r => !r.IsAnchor && r.AttachPoint == new Pt(bend.JogX, bend.RoomY));

            int yTop = Math.Min(bend.Y1, bend.Y2);
            int yBot = Math.Max(bend.Y1, bend.Y2);
            int yMid = bend.RoomY + bend.RoomHeight / 2;

            // Build candidate (startPt, dx, dy) list in preference order.
            // WalkAndPlaceDoor steps one tile in (dx,dy) from startPt before checking,
            // so the start tile should be the corridor tile immediately outside the wall.
            var candidates = new List<(Pt pt, int dx, int dy)>();

            // West wall — walk east from jog column (midpoint first, then rest)
            candidates.Add((new Pt(bend.JogX, yMid), 1, 0));
            for (int y = bend.RoomY; y < bend.RoomY + bend.RoomHeight; y++)
                if (y != yMid) candidates.Add((new Pt(bend.JogX, y), 1, 0));

            if (room is not null)
            {
                int xMid = room.Bounds.X + room.Bounds.Width / 2;

                // North wall — walk south from the upper horizontal spine segment
                candidates.Add((new Pt(xMid, yTop), 0, 1));
                for (int x = room.Bounds.X; x < room.Bounds.X + room.Bounds.Width; x++)
                    if (x != xMid) candidates.Add((new Pt(x, yTop), 0, 1));

                // South wall — walk north from the lower horizontal spine segment
                candidates.Add((new Pt(xMid, yBot), 0, -1));
                for (int x = room.Bounds.X; x < room.Bounds.X + room.Bounds.Width; x++)
                    if (x != xMid) candidates.Add((new Pt(x, yBot), 0, -1));
            }

            // Place the first door that succeeds
            foreach (var (pt, dx, dy) in candidates)
            {
                int countBefore = dungeon.Doors.Count;
                WalkAndPlaceDoor(dungeon, pt, dx, dy, spineId, ref doorId, usedWalls);
                if (dungeon.Doors.Count > countBefore) break;
            }
        }
    }

    // Walk from `from` in direction (dx,dy), skipping corridor and wall tiles without
    // floor neighbours, until we find a wall that IS adjacent to a floor tile.
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

            if (tile == TileType.Floor) return; // inside room with no intervening wall — skip
            if (tile == TileType.Door)  return; // door already placed here

            if (tile == TileType.Wall && HasFloorNeighbour(dungeon, new Pt(x, y)))
            {
                var room = FindRoomAtDoor(dungeon, new Pt(x, y));

                // Enforce one door per wall face per room
                if (room is not null)
                {
                    if (!usedWalls.TryGetValue(room.Id, out var dirs))
                        usedWalls[room.Id] = dirs = new();
                    if (!dirs.Add((dx, dy)))
                        return; // this face already has a door
                }

                dungeon.Grid[x, y] = TileType.Door;

                var door = new Door { Id = doorId++, Position = new Pt(x, y), CorridorId = corridorId };
                if (room is not null)
                {
                    door.RoomAId = room.Id;
                    room.DoorPositions.Add(new Pt(x, y));
                }
                dungeon.Doors.Add(door);

                // Record which room is at the end of this branch
                var branch = dungeon.Corridors.FirstOrDefault(c => c.Id == corridorId);
                if (branch is not null && branch.Kind == CorridorKind.Branch)
                    branch.AttachedRoomId = room?.Id;

                return;
            }

            x += dx;
            y += dy;
        }
    }

    private static void PaintCorridorWalls(Dungeon dungeon)
    {
        foreach (var corridor in dungeon.Corridors)
        {
            foreach (var tile in corridor.Spine)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = tile.X + dx, ny = tile.Y + dy;
                        if (nx < 0 || ny < 0 || nx >= dungeon.Width || ny >= dungeon.Height) continue;
                        if (dungeon.Grid[nx, ny] == TileType.Void)
                            dungeon.Grid[nx, ny] = TileType.Wall;
                    }
                }
            }
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
