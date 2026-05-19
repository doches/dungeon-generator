namespace DungeonGenerator.Models;

public enum TileType : byte
{
    Void          = 0,
    Wall          = 1,
    Floor         = 2,
    HorizCorridor = 3,
    VertCorridor  = 4,
    Door          = 5,
    SpineCorridor = 6,
}
