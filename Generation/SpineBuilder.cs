using DungeonGenerator.Models;

namespace DungeonGenerator.Generation;

public static class SpineBuilder
{
    public static Corridor Build(Dungeon dungeon, int spineXMin, int spineXMax)
    {
        int y = dungeon.Height / 2;
        var spine = new List<Pt>();

        for (int x = spineXMin; x <= spineXMax; x++)
        {
            dungeon.Grid[x, y] = TileType.SpineCorridor;
            spine.Add(new Pt(x, y));
        }

        return new Corridor
        {
            Id = 0,
            Kind = CorridorKind.Spine,
            Start = new Pt(spineXMin, y),
            End = new Pt(spineXMax, y),
            Spine = spine,
        };
    }
}
