namespace DungeonGenerator.Models;

public class Dungeon
{
    public int Width { get; set; }
    public int Height { get; set; }
    public TileType[,] Grid { get; set; } = new TileType[0, 0];
    public List<Room> Rooms { get; set; } = new();
    public List<Corridor> Corridors { get; set; } = new();
    public List<Door> Doors { get; set; } = new();
    public Corridor? MainSpine { get; set; }
}

public class DungeonConfig
{
    public int Width { get; set; } = 80;
    public int Height { get; set; } = 40;
    public List<RoomTypeConfig> RoomTypes { get; set; } = new();
    public int MinBranchSpacing { get; set; } = 8;
    public int MaxBranchSpacing { get; set; } = 14;
    public int MinBranchLength { get; set; } = 3;
    public int MaxBranchLength { get; set; } = 8;
    /// <summary>0 = dead straight spine; 100 = very snake-y.</summary>
    public int Snakiness { get; set; } = 0;
}

public class RoomTypeConfig
{
    public string Name { get; set; } = "";
    public int Weight { get; set; }
    public int MinWidth { get; set; } = 5;
    public int MaxWidth { get; set; } = 10;
    public int MinHeight { get; set; } = 4;
    public int MaxHeight { get; set; } = 8;
    public bool IsAnchorFore { get; set; }
    public bool IsAnchorAft { get; set; }
}
