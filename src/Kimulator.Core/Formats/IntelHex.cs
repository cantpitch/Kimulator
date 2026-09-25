using System.Globalization;
using System.Text;

namespace Kimulator.Core.Formats;

/// <summary>Intel HEX (record types 00 data and 01 EOF; extended-address records are rejected for a 64 KB machine).</summary>
public static class IntelHex
{
    public static IReadOnlyList<MemorySegment> Parse(string text)
    {
        var segments = new List<MemorySegment>();
        int lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line[0] != ':' || line.Length < 11 || line.Length % 2 == 0)
                throw new FormatException($"Line {lineNumber} is not an Intel HEX record.");

            var bytes = Convert.FromHexString(line.AsSpan(1));
            int count = bytes[0];
            if (bytes.Length != count + 5)
                throw new FormatException($"Line {lineNumber}: length does not match byte count.");
            if (bytes.Aggregate(0, (sum, b) => sum + b) % 256 != 0)
                throw new FormatException($"Line {lineNumber}: checksum error.");

            ushort address = (ushort)(bytes[1] << 8 | bytes[2]);
            switch (bytes[3])
            {
                case 0x00:
                    segments.Add(new MemorySegment(address, bytes[4..(4 + count)]));
                    break;
                case 0x01:
                    return MemorySegments.Coalesce(segments);
                case 0x02 or 0x04:
                    if (bytes[4..(4 + count)].Any(b => b != 0))
                        throw new FormatException($"Line {lineNumber}: addresses above 64 KB are not supported.");
                    break;
                default:
                    break; // start address records etc. carry nothing to load
            }
        }

        return MemorySegments.Coalesce(segments);
    }

    public static string Write(ushort address, ReadOnlySpan<byte> data, int bytesPerRecord = 16)
    {
        var sb = new StringBuilder();
        for (int offset = 0; offset < data.Length; offset += bytesPerRecord)
        {
            var chunk = data.Slice(offset, Math.Min(bytesPerRecord, data.Length - offset));
            int addr = (address + offset) & 0xFFFF;
            byte[] record = [(byte)chunk.Length, (byte)(addr >> 8), (byte)addr, 0x00, .. chunk];
            AppendRecord(sb, record);
        }

        AppendRecord(sb, [0x00, 0x00, 0x00, 0x01]);
        return sb.ToString();
    }

    private static void AppendRecord(StringBuilder sb, byte[] record)
    {
        int sum = record.Aggregate(0, (s, b) => s + b);
        sb.Append(':').Append(Convert.ToHexString(record)).Append(((-sum) & 0xFF).ToString("X2", CultureInfo.InvariantCulture)).Append("\r\n");
    }
}
