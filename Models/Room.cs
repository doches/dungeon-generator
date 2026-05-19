namespace DungeonGenerator.Models;

public record Pt(int X, int Y);
public record Rect(int X, int Y, int Width, int Height);
public record BoundingBox(int X, int Y, int Width, int Height);

public class Room
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public List<Rect> Tiles { get; set; } = new();
    public BoundingBox Bounds { get; set; } = new(0, 0, 0, 0);
    public List<Pt> DoorPositions { get; set; } = new();
    public List<int> ConnectedIds { get; set; } = new();
    public bool IsAnchor { get; set; }
    public Pt AttachPoint { get; set; } = new(0, 0);
}
