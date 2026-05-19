using System.Text;
using DungeonGenerator.Models;

namespace DungeonGenerator.Output;

public static class AsciiRenderer
{
    private static readonly char[] GlyphMap =
    [
        ' ',  // Void
        '#',  // Wall
        '.',  // Floor
        '-',  // HorizCorridor
        '|',  // VertCorridor
        '+',  // Door
        '-',  // SpineCorridor (same glyph as horiz)
    ];

    public static string Render(Dungeon dungeon)
    {
        var sb = new StringBuilder(dungeon.Height * (dungeon.Width + 1));
        for (int y = 0; y < dungeon.Height; y++)
        {
            for (int x = 0; x < dungeon.Width; x++)
                sb.Append(GlyphMap[(int)dungeon.Grid[x, y]]);
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
