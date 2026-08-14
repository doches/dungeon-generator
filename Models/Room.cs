namespace DungeonGenerator.Models;

public record Pt(int X, int Y);

public class Room
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    // Top-left corner and dimensions of the rectangular room slot
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Assigned by the room editor when a pre-built template is placed here.</summary>
    public string? TemplateId { get; set; }
    public List<Pt> DoorPositions { get; set; } = new();
    public List<int> ConnectedIds { get; set; } = new();
    public bool IsAnchor { get; set; }
    public Pt AttachPoint { get; set; } = new(0, 0);
}
