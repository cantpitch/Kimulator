using System.Text;
using Kimulator.Core.Formats;

namespace Kimulator.App;

/// <summary>Chooses a program file format from its extension (or content) and converts to/from memory.</summary>
public static class ProgramFiles
{
    public enum Format { PaperTape, IntelHex, Binary }

    public static Format Detect(string fileName, ReadOnlySpan<byte> content)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is ".ptp" or ".pap") return Format.PaperTape;
        if (ext is ".hex" or ".ihx") return Format.IntelHex;
        if (ext == ".bin") return Format.Binary;

        // Unknown extension: text that starts like a record is a record format.
        int i = 0;
        while (i < content.Length && content[i] is (byte)'\r' or (byte)'\n' or (byte)' ' or 0) i++;
        if (i < content.Length && content[i] == ';') return Format.PaperTape;
        if (i < content.Length && content[i] == ':') return Format.IntelHex;
        return Format.Binary;
    }

    /// <summary>Parses a record-based file. Binary files have no address, so callers handle those separately.</summary>
    public static IReadOnlyList<MemorySegment> ParseRecords(Format format, byte[] content)
    {
        string text = Encoding.ASCII.GetString(content);
        return format switch
        {
            Format.PaperTape => PaperTape.Parse(text),
            Format.IntelHex => IntelHex.Parse(text),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    public static byte[] Serialize(Format format, ushort start, byte[] data) => format switch
    {
        Format.PaperTape => Encoding.ASCII.GetBytes(PaperTape.Write(start, data)),
        Format.IntelHex => Encoding.ASCII.GetBytes(IntelHex.Write(start, data)),
        _ => data,
    };
}
