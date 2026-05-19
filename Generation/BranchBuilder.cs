using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

public static class BranchBuilder
{
    public static List<Corridor> Build(
        Dungeon dungeon, Corridor spine, DungeonConfig cfg, Random rng,
        IReadOnlyDictionary<int, int> yAtX, IReadOnlySet<int> jogCols)
    {
        var branches   = new List<Corridor>();
        var usedColumns = new HashSet<int>();
        int minJogClear = Math.Max(3, cfg.MinBranchSpacing / 2);

        int x      = spine.Start.X + rng.Next(cfg.MinBranchSpacing, cfg.MaxBranchSpacing);
        int nextId = 1;

        while (x < spine.End.X - cfg.MinBranchSpacing)
        {
            bool nearJog = jogCols.Any(jx => Math.Abs(x - jx) < minJogClear);

            if (!usedColumns.Contains(x) && !nearJog && yAtX.ContainsKey(x))
            {
                int spineY = yAtX[x];
                bool goNorth = rng.Next(2) == 0;
                int length   = rng.Next(cfg.MinBranchLength, cfg.MaxBranchLength + 1);
                int endY;
                var branchTiles = new List<Pt>();

                if (goNorth)
                {
                    endY = spineY - length;
                    if (endY < 2) { endY = 2; length = spineY - endY; }
                    for (int dy = 1; dy <= length; dy++)
                    {
                        dungeon.Grid[x, spineY - dy] = TileType.VertCorridor;
                        branchTiles.Add(new Pt(x, spineY - dy));
                    }
                }
                else
                {
                    endY = spineY + length;
                    if (endY > dungeon.Height - 3) { endY = dungeon.Height - 3; length = endY - spineY; }
                    for (int dy = 1; dy <= length; dy++)
                    {
                        dungeon.Grid[x, spineY + dy] = TileType.VertCorridor;
                        branchTiles.Add(new Pt(x, spineY + dy));
                    }
                }

                if (length > 0)
                {
                    branches.Add(new Corridor
                    {
                        Id              = nextId++,
                        Kind            = CorridorKind.Branch,
                        Start           = new Pt(x, spineY),
                        End             = new Pt(x, endY),
                        Spine           = branchTiles,
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
