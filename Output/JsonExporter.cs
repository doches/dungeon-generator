using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonGenerator.Models;

namespace DungeonGenerator.Output;

public static class JsonExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Export(Dungeon dungeon, bool verboseSpine = false)
    {
        var dto = new DungeonDto
        {
            GridWidth  = dungeon.Width,
            GridHeight = dungeon.Height,
            Rooms      = dungeon.Rooms.Select(r => ToRoomDto(r, dungeon)).ToList(),
            Corridors  = dungeon.Corridors.Select(c => ToCorridorDto(c, verboseSpine)).ToList(),
            Doors      = dungeon.Doors.Select(ToDoorDto).ToList(),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    private static RoomDto ToRoomDto(Room r, Dungeon dungeon) => new()
    {
        Id            = r.Id,
        Type          = r.Type,
        IsAnchor      = r.IsAnchor ? true : null,
        Bounds        = ToBoundsDto(r.Bounds),
        Tiles         = r.Tiles.Select(ToRectDto).ToList(),
        DoorPositions = r.DoorPositions.Select(ToPtDto).ToList(),
        Connections   = r.ConnectedIds.Count > 0 ? r.ConnectedIds : null,
    };

    private static CorridorDto ToCorridorDto(Corridor c, bool verbose) => new()
    {
        Id               = c.Id,
        Kind             = c.Kind.ToString(),
        Start            = ToPtDto(c.Start),
        End              = ToPtDto(c.End),
        ParentCorridorId = c.ParentCorridorId,
        AttachedRoomId   = c.AttachedRoomId,
        Spine            = verbose ? c.Spine.Select(ToPtDto).ToList() : null,
    };

    private static DoorDto ToDoorDto(Door d) => new()
    {
        Id         = d.Id,
        Position   = ToPtDto(d.Position),
        RoomAId    = d.RoomAId,
        RoomBId    = d.RoomBId,
        CorridorId = d.CorridorId,
        IsWide     = d.IsWide ? true : null,
    };

    private static PtDto    ToPtDto(Pt p)         => new(p.X, p.Y);
    private static RectDto  ToRectDto(Rect r)     => new(r.X, r.Y, r.Width, r.Height);
    private static BoundsDto ToBoundsDto(BoundingBox b) => new(b.X, b.Y, b.Width, b.Height);
}

// DTO records for clean JSON shape

record DungeonDto
{
    public int GridWidth  { get; init; }
    public int GridHeight { get; init; }
    public List<RoomDto>     Rooms     { get; init; } = new();
    public List<CorridorDto> Corridors { get; init; } = new();
    public List<DoorDto>     Doors     { get; init; } = new();
}

record RoomDto
{
    public int              Id            { get; init; }
    public string           Type          { get; init; } = "";
    public bool?            IsAnchor      { get; init; }
    public BoundsDto        Bounds        { get; init; } = new(0,0,0,0);
    public List<RectDto>    Tiles         { get; init; } = new();
    public List<PtDto>      DoorPositions { get; init; } = new();
    public List<int>?       Connections   { get; init; }
}

record CorridorDto
{
    public int         Id               { get; init; }
    public string      Kind             { get; init; } = "";
    public PtDto       Start            { get; init; } = new(0,0);
    public PtDto       End              { get; init; } = new(0,0);
    public int?        ParentCorridorId { get; init; }
    public int?        AttachedRoomId   { get; init; }
    public List<PtDto>? Spine           { get; init; }
}

record DoorDto
{
    public int    Id         { get; init; }
    public PtDto  Position   { get; init; } = new(0,0);
    public int?   RoomAId    { get; init; }
    public int?   RoomBId    { get; init; }
    public int?   CorridorId { get; init; }
    public bool?  IsWide     { get; init; }
}

record PtDto(int X, int Y);
record RectDto(int X, int Y, int Width, int Height);
record BoundsDto(int X, int Y, int Width, int Height);
