using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

public static class BranchBuilder
{
    public static List<Corridor> Build(
        Dungeon dungeon, Corridor spine, DungeonConfig cfg, Random rng,
        IReadOnlyDictionary<int, int> yAtX, IReadOnlySet<int> jogCols)
    {
        var branches    = new List<Corridor>();
        var usedColumns = new HashSet<int>();
        int minJogClear = Math.Max(3, cfg.MinBranchSpacing / 2);

        int x      = spine.Start.X + rng.Next(cfg.MinBranchSpacing, cfg.MaxBranchSpacing);
        int nextId = 1;

        while (x < spine.End.X - cfg.MinBranchSpacing)
        {
            bool nearJog = jogCols.Any(jx => Math.Abs(x - jx) < minJogClear);

            if (!usedColumns.Contains(x) && !nearJog && yAtX.ContainsKey(x))
            {
                int spineTopY    = yAtX[x];
                int spineBottomY = spineTopY + dungeon.SpineWidth - 1;
                bool goNorth     = rng.Next(2) == 0;
                int length       = rng.Next(cfg.MinBranchLength, cfg.MaxBranchLength + 1);
                int endY;
                var branchTiles  = new List<Pt>();

                if (goNorth)
                {
                    endY = spineTopY - length;
                    if (endY < 2) { endY = 2; length = spineTopY - endY; }
                    for (int dy = 1; dy <= length; dy++)
                    {
                        dungeon.Grid[x, spineTopY - dy] = TileType.VertCorridor;
                        branchTiles.Add(new Pt(x, spineTopY - dy));
                    }
                }
                else
                {
                    endY = spineBottomY + length;
                    if (endY > dungeon.Height - 3) { endY = dungeon.Height - 3; length = endY - spineBottomY; }
                    for (int dy = 1; dy <= length; dy++)
                    {
                        dungeon.Grid[x, spineBottomY + dy] = TileType.VertCorridor;
                        branchTiles.Add(new Pt(x, spineBottomY + dy));
                    }
                }

                if (length > 0)
                {
                    int branchStartY = goNorth ? spineTopY : spineBottomY;
                    branches.Add(new Corridor
                    {
                        Id               = nextId++,
                        Kind             = CorridorKind.Branch,
                        Start            = new Pt(x, branchStartY),
                        End              = new Pt(x, endY),
                        Spine            = branchTiles,
                        ParentCorridorId = spine.Id,
                    });
                    usedColumns.Add(x);
                }
            }

            x += rng.Next(cfg.MinBranchSpacing, cfg.MaxBranchSpacing);
        }

        return branches;
    }
}
