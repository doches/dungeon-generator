namespace DungeonGenerator.Models;

public enum CorridorKind { Spine, Branch }

public class Corridor
{
    public int Id { get; set; }
    public CorridorKind Kind { get; set; }
    public Pt Start { get; set; } = new(0, 0);
    public Pt End { get; set; } = new(0, 0);
    public List<Pt> Spine { get; set; } = new();
    public int? ParentCorridorId { get; set; }
    public int? AttachedRoomId { get; set; }
}
