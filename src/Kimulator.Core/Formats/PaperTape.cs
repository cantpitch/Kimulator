using System.Globalization;
using System.Text;

namespace Kimulator.Core.Formats;

/// <summary>
/// MOS Technology paper tape format, as read and written by the KIM-1 monitor (TTY "L" and "Q"):
/// <code>
/// ;NNAAAADD..DDCCCC     NN = byte count, AAAA = address, CCCC = 16-bit sum of all bytes from NN on
/// ;00RRRRCCCC           last record: RRRR = number of data records
/// </code>
/// </summary>
public static class PaperTape
{
    public const int DefaultBytesPerRecord = 24; // what the KIM-1 monitor writes

    public static IReadOnlyList<MemorySegment> Parse(string text)
    {
        var segments = new List<MemorySegment>();
        int dataRecords = 0;
        int pos = 0;
        int line = 1;
        while (true)
        {
            // Everything outside a record (CR, LF, NULs, leader) is ignored, like the monitor does.
            while (pos < text.Length && text[pos] != ';')
            {
                if (text[pos] == '\n') line++;
                pos++;
            }

            if (pos >= text.Length)
                throw new FormatException("Paper tape ends without a final ;00 record.");

            pos++;
            int sum = 0;
            int count = ReadByte(ref sum);
            int hi = ReadByte(ref sum);
            int lo = ReadByte(ref sum);

            var data = new byte[count];
            for (int i = 0; i < count; i++)
                data[i] = (byte)ReadByte(ref sum);

            int ignored = 0;
            int checksum = ReadByte(ref ignored) << 8 | ReadByte(ref ignored);
            if (checksum != (sum & 0xFFFF))
                throw new FormatException($"Checksum error in record on line {line}: expected {sum & 0xFFFF:X4}, found {checksum:X4}.");

            if (count == 0)
            {
                int expected = hi << 8 | lo;
                if (expected != dataRecords)
                    throw new FormatException($"Final record says {expected} records, but the tape has {dataRecords}.");
                return MemorySegments.Coalesce(segments);
            }

            dataRecords++;
            segments.Add(new MemorySegment((ushort)(hi << 8 | lo), data));
        }

        int ReadByte(ref int sum)
        {
            if (pos + 2 > text.Length)
                throw new FormatException($"Paper tape record on line {line} is truncated.");
            if (!byte.TryParse(text.AsSpan(pos, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte b))
                throw new FormatException($"Invalid hex '{text.Substring(pos, 2)}' on line {line}.");
            pos += 2;
            sum += b;
            return b;
        }
    }

    /// <summary>Writes <paramref name="data"/> as a tape that loads at <paramref name="address"/>.</summary>
    public static string Write(ushort address, ReadOnlySpan<byte> data, int bytesPerRecord = DefaultBytesPerRecord)
    {
        if (bytesPerRecord is < 1 or > 255) throw new ArgumentOutOfRangeException(nameof(bytesPerRecord));

        var sb = new StringBuilder();
        int records = 0;
        for (int offset = 0; offset < data.Length; offset += bytesPerRecord)
        {
            var chunk = data.Slice(offset, Math.Min(bytesPerRecord, data.Length - offset));
            int addr = (address + offset) & 0xFFFF;
            WriteRecord(sb, (byte)chunk.Length, (byte)(addr >> 8), (byte)addr, chunk);
            records++;
        }

        WriteRecord(sb, 0, (byte)(records >> 8), (byte)records, []);
        return sb.ToString();
    }

    private static void WriteRecord(StringBuilder sb, byte count, byte hi, byte lo, ReadOnlySpan<byte> data)
    {
        int sum = count + hi + lo;
        sb.Append(';').Append(count.ToString("X2")).Append(hi.ToString("X2")).Append(lo.ToString("X2"));
        foreach (byte b in data)
        {
            sb.Append(b.ToString("X2"));
            sum += b;
        }

        sb.Append((sum & 0xFFFF).ToString("X4")).Append("\r\n");
    }
}
