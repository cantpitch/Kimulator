namespace Kimulator.Core.Formats;

/// <summary>A contiguous block of bytes destined for <see cref="Address"/>.</summary>
public sealed record MemorySegment(ushort Address, byte[] Data)
{
    public int End => Address + Data.Length; // exclusive
}

public static class MemorySegments
{
    /// <summary>Merges adjacent segments (as produced by record-based formats) into contiguous blocks.</summary>
    public static IReadOnlyList<MemorySegment> Coalesce(IEnumerable<MemorySegment> segments)
    {
        var result = new List<MemorySegment>();
        foreach (var segment in segments.Where(s => s.Data.Length > 0))
        {
            if (result.Count > 0 && result[^1].End == segment.Address)
                result[^1] = new MemorySegment(result[^1].Address, [.. result[^1].Data, .. segment.Data]);
            else
                result.Add(segment);
        }

        return result;
    }
}
