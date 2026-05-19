namespace DungeonGenerator.Models;

public class Door
{
    public int Id { get; set; }
    public Pt Position { get; set; } = new(0, 0);
    public int? RoomAId { get; set; }
    public int? RoomBId { get; set; }
    public int? CorridorId { get; set; }
}
