using System.Text.Json;
using DungeonGenerator.Generation;
using DungeonGenerator.Models;
using DungeonGenerator.Output;

// ── Default room-type configuration ──────────────────────────────────────────
const string DefaultConfig = """
[
  { "name": "Bridge",       "weight": 0,  "minWidth": 11, "maxWidth": 17, "minHeight": 9,  "maxHeight": 13, "isAnchorFore": true  },
  { "name": "Engineering",  "weight": 0,  "minWidth": 11, "maxWidth": 17, "minHeight": 9,  "maxHeight": 13, "isAnchorAft":  true  },
  { "name": "MedBay",       "weight": 10, "minWidth": 8,  "maxWidth": 12, "minHeight": 7,  "maxHeight": 10 },
  { "name": "Armory",       "weight": 8,  "minWidth": 7,  "maxWidth": 11, "minHeight": 7,  "maxHeight": 9  },
  { "name": "CrewQuarters", "weight": 12, "minWidth": 8,  "maxWidth": 13, "minHeight": 7,  "maxHeight": 11 },
  { "name": "Storage",      "weight": 15, "minWidth": 7,  "maxWidth": 10, "minHeight": 6,  "maxHeight": 9  },
  { "name": "Lab",          "weight": 8,  "minWidth": 8,  "maxWidth": 12, "minHeight": 7,  "maxHeight": 10 }
]
""";

// ── CLI parsing ───────────────────────────────────────────────────────────────
int width       = 80;
int height      = 40;
int snakiness   = 0;
string? cfgPath = null;
string? outJson = null;
string? outAscii= null;
bool verbose    = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--width"        when i + 1 < args.Length: width     = int.Parse(args[++i]); break;
        case "--height"       when i + 1 < args.Length: height    = int.Parse(args[++i]); break;
        case "--snakiness"    when i + 1 < args.Length: snakiness = int.Parse(args[++i]); break;
        case "--config"       when i + 1 < args.Length: cfgPath   = args[++i]; break;
        case "--out-json"     when i + 1 < args.Length: outJson   = args[++i]; break;
        case "--out-ascii"    when i + 1 < args.Length: outAscii  = args[++i]; break;
        case "--verbose-json": verbose = true; break;
        case "--help": PrintHelp(); return 0;
    }
}

// ── Load config ───────────────────────────────────────────────────────────────
string configJson = cfgPath is not null ? File.ReadAllText(cfgPath) : DefaultConfig;
var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var roomTypes = JsonSerializer.Deserialize<List<RoomTypeConfig>>(configJson, jsonOpts)
    ?? throw new InvalidOperationException("Failed to parse room type config.");

var cfg = new DungeonConfig
{
    Width             = width,
    Height            = height,
    Snakiness         = Math.Clamp(snakiness, 0, 100),
    MinBranchSpacing  = 6,
    MaxBranchSpacing  = 12,
    RoomTypes         = roomTypes,
};

// ── Generate ──────────────────────────────────────────────────────────────────
var dungeon = Generator.Generate(cfg);

// ── Output ────────────────────────────────────────────────────────────────────
string asciiMap = AsciiRenderer.Render(dungeon);
string jsonData = JsonExporter.Export(dungeon, verbose);

if (outAscii is not null)
    File.WriteAllText(outAscii, asciiMap);
else
    Console.Write(asciiMap);

if (outJson is not null)
    File.WriteAllText(outJson, jsonData);
else
    Console.WriteLine(jsonData);

return 0;

static void PrintHelp()
{
    Console.WriteLine("""
        dungeon-generator — sci-fi starship dungeon layout generator

        USAGE:
          dungeon-generator [OPTIONS]

        OPTIONS:
          --width <int>       Map width in tiles  (default: 80)
          --height <int>      Map height in tiles (default: 40)
          --config <path>     Room-type config JSON file (default: built-in)
          --out-json <path>   Write JSON metadata to file (default: stdout)
          --out-ascii <path>  Write ASCII tile map to file (default: stdout)
          --snakiness <0-100> Spine snake-yness (0=straight, 100=max bends, default: 0)
          --verbose-json      Include full corridor tile lists in JSON
          --help              Show this help

        EXAMPLES:
          dungeon-generator
          dungeon-generator --width 120 --height 60 --out-json d.json --out-ascii d.txt
          dungeon-generator --config myrooms.json --out-ascii map.txt
        """);
}
