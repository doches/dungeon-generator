namespace DungeonGenerator.Models;

/// <summary>Which wall of RoomA the door is on (from that room's interior perspective).</summary>
public enum DoorFace { North, South, East, West }

public class Door
{
    public int Id { get; set; }
    public Pt Position { get; set; } = new(0, 0);
    public int? RoomAId { get; set; }
    public int? RoomBId { get; set; }
    public int? CorridorId { get; set; }
    public bool IsWide { get; set; }
    /// <summary>Wall of RoomA the door sits on. Null for wide corridor-divider doors.</summary>
    public DoorFace? Face { get; set; }
}
