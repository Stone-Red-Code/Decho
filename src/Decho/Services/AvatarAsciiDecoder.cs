namespace Decho.Services;

internal sealed record DecodedAvatar(int Width, int Height, byte[] Bgra);

/// <summary>
/// Turns the half-block text EchoHub servers store as avatars back into pixels.
/// Format: {F:RRGGBB} foreground, {B:RRGGBB} background, {X} reset. Each cell holds two vertical pixels.
/// </summary>
internal static class AvatarAsciiDecoder
{
    private const int MaxDimension = 512;
    private const int ColorTagLength = 10;
    private const char FullBlock = '█';
    private const char UpperHalfBlock = '▀';
    private const char LowerHalfBlock = '▄';

    /// <summary>Returns null when the text is empty or not half-block art.</summary>
    public static DecodedAvatar? Decode(string? ascii)
    {
        if (string.IsNullOrWhiteSpace(ascii))
        {
            return null;
        }

        string[] lines = ascii.Split('\n');
        List<List<(uint Top, uint Bottom)>> rows = new List<List<(uint Top, uint Bottom)>>(lines.Length);

        foreach (string line in lines)
        {
            List<(uint Top, uint Bottom)>? row = ParseLine(line.TrimEnd('\r'));
            if (row is null)
            {
                return null;
            }

            if (row.Count > 0)
            {
                rows.Add(row);
            }
        }

        if (rows.Count == 0)
        {
            return null;
        }

        int width = rows.Max(row => row.Count);
        int height = rows.Count * 2;
        if (width > MaxDimension || height > MaxDimension)
        {
            return null;
        }

        byte[] bgra = new byte[width * height * 4];
        for (int y = 0; y < rows.Count; y++)
        {
            for (int x = 0; x < rows[y].Count; x++)
            {
                WritePixel(bgra, width, x, y * 2, rows[y][x].Top);
                WritePixel(bgra, width, x, (y * 2) + 1, rows[y][x].Bottom);
            }
        }

        return new DecodedAvatar(width, height, bgra);
    }

    private static List<(uint Top, uint Bottom)>? ParseLine(string line)
    {
        List<(uint Top, uint Bottom)> row = [];
        uint? foreground = null;
        uint? background = null;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '{')
            {
                if (string.CompareOrdinal(line, i, "{X}", 0, 3) == 0)
                {
                    foreground = null;
                    background = null;
                    i += 2;
                    continue;
                }

                if (!TryParseColorTag(line, i, out bool isForeground, out uint color))
                {
                    return null;
                }

                if (isForeground)
                {
                    foreground = color;
                }
                else
                {
                    background = color;
                }

                i += ColorTagLength - 1;
                continue;
            }

            switch (c)
            {
                case FullBlock when foreground is uint full:
                    row.Add((full, full));
                    break;
                case UpperHalfBlock when foreground is uint top && background is uint bottom:
                    row.Add((top, bottom));
                    break;
                case LowerHalfBlock when foreground is uint lower && background is uint upper:
                    row.Add((upper, lower));
                    break;
                default:
                    return null;
            }
        }

        return row;
    }

    private static bool TryParseColorTag(string line, int start, out bool isForeground, out uint color)
    {
        isForeground = false;
        color = 0;

        if (start + ColorTagLength > line.Length || line[start + 2] != ':' || line[start + ColorTagLength - 1] != '}')
        {
            return false;
        }

        char kind = line[start + 1];
        if (kind is not ('F' or 'B'))
        {
            return false;
        }

        isForeground = kind == 'F';
        return uint.TryParse(
            line.AsSpan(start + 3, 6),
            System.Globalization.NumberStyles.AllowHexSpecifier,
            System.Globalization.CultureInfo.InvariantCulture,
            out color);
    }

    private static void WritePixel(byte[] bgra, int width, int x, int y, uint rgb)
    {
        int offset = ((y * width) + x) * 4;
        bgra[offset] = (byte)rgb;
        bgra[offset + 1] = (byte)(rgb >> 8);
        bgra[offset + 2] = (byte)(rgb >> 16);
        bgra[offset + 3] = 255;
    }
}
