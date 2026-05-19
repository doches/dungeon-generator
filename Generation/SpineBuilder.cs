using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

/// <summary>A bend in the spine: vertical jog at column JogX from Y1 to Y2.</summary>
public record BendInfo(int JogX, int Y1, int Y2)
{
    /// <summary>First valid y-row for the bend room (one inside the upper spine level).</summary>
    public int RoomY      => Math.Min(Y1, Y2) + 1;
    /// <summary>Height available for the bend room between the two spine levels.</summary>
    public int RoomHeight => Math.Abs(Y2 - Y1) - 1;
}

public static class SpineBuilder
{
    /// <returns>
    ///   spine      — the full spine corridor<br/>
    ///   yAtX       — spine y-level for each x on a horizontal segment<br/>
    ///   bends      — one entry per jog<br/>
    ///   jogCols    — x-columns occupied by vertical jogs (exclude from branch placement)
    /// </returns>
    public static (Corridor spine, Dictionary<int, int> yAtX,
                   List<BendInfo> bends, HashSet<int> jogCols)
        Build(Dungeon dungeon, int spineXMin, int spineXMax, DungeonConfig cfg, Random rng)
    {
        int baseY  = dungeon.Height / 2;
        var tiles  = new List<Pt>();
        var yAtX   = new Dictionary<int, int>();
        var bends  = new List<BendInfo>();
        var jogCols = new HashSet<int>();

        if (cfg.Snakiness == 0)
            return BuildStraight(dungeon, spineXMin, spineXMax, baseY, tiles, yAtX);

        // ── Snake-y spine ─────────────────────────────────────────────────────
        // Snakiness drives both bend probability and maximum jog magnitude.
        // Minimum jog of 5 ensures the bend room always has height ≥ 3 (enough for floor tiles).
        double bendChance  = cfg.Snakiness / 100.0;
        int    maxJogAmt   = Math.Max(5, cfg.Snakiness / 10);   // 100→10, 50→5
        int    minBendGap  = Math.Max(10, (spineXMax - spineXMin) / 8);
        int    edgeMarginY = 4;
        int    minY        = edgeMarginY;
        int    maxY        = dungeon.Height - edgeMarginY - 1;

        // Start near centre, clamped so there's room to jog in either direction
        int currentY   = Math.Clamp(baseY, minY + maxJogAmt, maxY - maxJogAmt);
        int lastBendX  = spineXMin - 1;

        for (int x = spineXMin; x <= spineXMax; x++)
        {
            bool canBend = x - lastBendX >= minBendGap
                        && x <= spineXMax - minBendGap;

            if (canBend && rng.NextDouble() < bendChance)
            {
                int jogAmt  = rng.Next(5, maxJogAmt + 1);
                bool goDown = rng.Next(2) == 0;

                int newY = currentY + (goDown ? jogAmt : -jogAmt);
                if (newY < minY || newY > maxY)
                {
                    goDown = !goDown;
                    newY   = currentY + (goDown ? jogAmt : -jogAmt);
                }
                newY = Math.Clamp(newY, minY, maxY);

                int actualJog = Math.Abs(newY - currentY);
                if (actualJog >= 5)   // only bend if the room will fit
                {
                    // Corner tile (last tile of upper horizontal segment)
                    dungeon.Grid[x, currentY] = TileType.SpineCorridor;
                    tiles.Add(new Pt(x, currentY));
                    yAtX[x] = currentY;

                    // Vertical jog tiles
                    int step = newY > currentY ? 1 : -1;
                    for (int jy = currentY + step; jy != newY + step; jy += step)
                    {
                        dungeon.Grid[x, jy] = TileType.VertCorridor;
                        tiles.Add(new Pt(x, jy));
                    }

                    bends.Add(new BendInfo(x, currentY, newY));
                    jogCols.Add(x);
                    lastBendX = x;
                    currentY  = newY;
                    continue;   // tile already painted above
                }
            }

            // Normal horizontal tile
            dungeon.Grid[x, currentY] = TileType.SpineCorridor;
            tiles.Add(new Pt(x, currentY));
            yAtX[x] = currentY;
        }

        var spine = new Corridor
        {
            Id    = 0,
            Kind  = CorridorKind.Spine,
            Start = tiles.First(),
            End   = tiles.Last(),
            Spine = tiles,
        };
        return (spine, yAtX, bends, jogCols);
    }

    // ── Straight (snakiness == 0) ─────────────────────────────────────────────
    private static (Corridor, Dictionary<int, int>, List<BendInfo>, HashSet<int>)
        BuildStraight(Dungeon dungeon, int xMin, int xMax, int y,
                      List<Pt> tiles, Dictionary<int, int> yAtX)
    {
        for (int x = xMin; x <= xMax; x++)
        {
            dungeon.Grid[x, y] = TileType.SpineCorridor;
            tiles.Add(new Pt(x, y));
            yAtX[x] = y;
        }
        var spine = new Corridor
        {
            Id    = 0,
            Kind  = CorridorKind.Spine,
            Start = new Pt(xMin, y),
            End   = new Pt(xMax, y),
            Spine = tiles,
        };
        return (spine, yAtX, new List<BendInfo>(), new HashSet<int>());
    }
}
