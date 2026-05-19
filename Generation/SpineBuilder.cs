using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

/// <summary>A bend in the spine: vertical jog at column JogX. Y1 and Y2 are the TOP rows of the two wide segments.</summary>
public record BendInfo(int JogX, int Y1, int Y2, int SpineWidth = 1)
{
    /// <summary>First valid y-row for the bend room (SpineWidth rows below the top of the upper segment).</summary>
    public int RoomY      => Math.Min(Y1, Y2) + SpineWidth;
    /// <summary>Height available for the bend room between the two spine levels.</summary>
    public int RoomHeight => Math.Abs(Y2 - Y1) - SpineWidth;
}

public static class SpineBuilder
{
    /// <returns>
    ///   spine      — the full spine corridor<br/>
    ///   yAtX       — top-row spine y for each x column on a horizontal segment<br/>
    ///   bends      — one entry per jog<br/>
    ///   jogCols    — x-columns occupied by vertical jogs (excluded from branch placement)<br/>
    ///   spineWidth — number of tile rows the spine occupies (2–4)
    /// </returns>
    public static (Corridor spine, Dictionary<int, int> yAtX,
                   List<BendInfo> bends, HashSet<int> jogCols, int spineWidth)
        Build(Dungeon dungeon, int spineXMin, int spineXMax, DungeonConfig cfg, Random rng)
    {
        int spineWidth = rng.Next(2, 5); // 2, 3, or 4 tiles wide
        int baseY      = dungeon.Height / 2 - (spineWidth - 1) / 2;
        var tiles      = new List<Pt>();
        var yAtX       = new Dictionary<int, int>();
        var bends      = new List<BendInfo>();
        var jogCols    = new HashSet<int>();

        if (cfg.Snakiness == 0)
            return BuildStraight(dungeon, spineXMin, spineXMax, baseY, spineWidth, tiles, yAtX);

        // ── Snake-y spine ─────────────────────────────────────────────────────
        int minJogAmt  = spineWidth + 3;  // guarantees bend room height >= 3
        int maxJogAmt  = Math.Max(minJogAmt, cfg.Snakiness / 10);
        int minBendGap = Math.Max(10, (spineXMax - spineXMin) / 8);
        int edgeMarginY = 4;
        int minY       = edgeMarginY;
        int maxTopY    = dungeon.Height - edgeMarginY - spineWidth; // max top row so bottom stays in bounds

        int currentY   = Math.Clamp(baseY, minY + maxJogAmt, maxTopY - maxJogAmt);
        int lastBendX  = spineXMin - 1;
        Pt? firstHorizPt = null;
        Pt? lastHorizPt  = null;

        for (int x = spineXMin; x <= spineXMax; x++)
        {
            bool canBend = x - lastBendX >= minBendGap
                        && x <= spineXMax - minBendGap;

            if (canBend && rng.NextDouble() < cfg.Snakiness / 100.0)
            {
                bool goDown = rng.Next(2) == 0;
                int jogAmt  = rng.Next(minJogAmt, maxJogAmt + 1);
                int newY    = currentY + (goDown ? jogAmt : -jogAmt);

                if (newY < minY || newY > maxTopY)
                {
                    goDown = !goDown;
                    newY   = currentY + (goDown ? jogAmt : -jogAmt);
                }
                newY = Math.Clamp(newY, minY, maxTopY);

                if (Math.Abs(newY - currentY) >= minJogAmt)
                {
                    // Old-side corner: W tiles at (x, currentY..currentY+W-1)
                    for (int dy = 0; dy < spineWidth; dy++)
                    {
                        dungeon.Grid[x, currentY + dy] = TileType.SpineCorridor;
                        tiles.Add(new Pt(x, currentY + dy));
                    }
                    yAtX[x] = currentY;

                    // Vertical jog tiles: the gap between the two wide segments
                    if (goDown)
                    {
                        for (int jy = currentY + spineWidth; jy < newY; jy++)
                        {
                            dungeon.Grid[x, jy] = TileType.VertCorridor;
                            tiles.Add(new Pt(x, jy));
                        }
                    }
                    else
                    {
                        for (int jy = newY + spineWidth; jy < currentY; jy++)
                        {
                            dungeon.Grid[x, jy] = TileType.VertCorridor;
                            tiles.Add(new Pt(x, jy));
                        }
                    }

                    // New-side corner: W tiles at (x, newY..newY+W-1)
                    for (int dy = 0; dy < spineWidth; dy++)
                    {
                        dungeon.Grid[x, newY + dy] = TileType.SpineCorridor;
                        tiles.Add(new Pt(x, newY + dy));
                    }

                    bends.Add(new BendInfo(x, currentY, newY, spineWidth));
                    jogCols.Add(x);
                    lastBendX = x;
                    currentY  = newY;
                    continue;
                }
            }

            // Normal horizontal column: paint W rows
            for (int dy = 0; dy < spineWidth; dy++)
            {
                dungeon.Grid[x, currentY + dy] = TileType.SpineCorridor;
                tiles.Add(new Pt(x, currentY + dy));
            }
            yAtX[x] = currentY;
            firstHorizPt ??= new Pt(x, currentY);
            lastHorizPt    = new Pt(x, currentY);
        }

        var spine = new Corridor
        {
            Id    = 0,
            Kind  = CorridorKind.Spine,
            Start = firstHorizPt ?? tiles.First(),
            End   = lastHorizPt  ?? tiles.Last(),
            Spine = tiles,
        };
        return (spine, yAtX, bends, jogCols, spineWidth);
    }

    // ── Straight (snakiness == 0) ─────────────────────────────────────────────
    private static (Corridor, Dictionary<int, int>, List<BendInfo>, HashSet<int>, int)
        BuildStraight(Dungeon dungeon, int xMin, int xMax, int y, int spineWidth,
                      List<Pt> tiles, Dictionary<int, int> yAtX)
    {
        for (int x = xMin; x <= xMax; x++)
        {
            for (int dy = 0; dy < spineWidth; dy++)
            {
                dungeon.Grid[x, y + dy] = TileType.SpineCorridor;
                tiles.Add(new Pt(x, y + dy));
            }
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
        return (spine, yAtX, new List<BendInfo>(), new HashSet<int>(), spineWidth);
    }
}
