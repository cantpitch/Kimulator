using System.Text;
using Kimulator.Core.Debugging;
using Kimulator.Core.Formats;

namespace Kimulator.Core.Assembly;

public sealed record AssemblyError(int Line, string Message)
{
    public override string ToString() => $"Line {Line}: {Message}";
}

/// <summary>One source line in the listing: where it landed and what it produced.</summary>
public sealed record ListingLine(int Line, ushort? Address, byte[] Bytes, string Source);

public sealed class AssemblerOptions
{
    /// <summary>Address used until the first <c>.org</c>.</summary>
    public ushort DefaultOrigin { get; init; } = 0x0200;

    /// <summary>Symbols available to the program (e.g. the KIM-1 monitor's). Program symbols override them.</summary>
    public SymbolTable? PredefinedSymbols { get; init; }
}

public sealed class AssemblyResult
{
    public required IReadOnlyList<AssemblyError> Errors { get; init; }

    /// <summary>Contiguous blocks of emitted bytes, in address order.</summary>
    public required IReadOnlyList<MemorySegment> Segments { get; init; }

    /// <summary>Labels and constants defined by the program.</summary>
    public required SymbolTable Symbols { get; init; }

    public required IReadOnlyList<ListingLine> Listing { get; init; }

    /// <summary>Source line (1-based) → address of the instruction or data on it.</summary>
    public required IReadOnlyDictionary<int, ushort> LineAddresses { get; init; }

    public bool Success => Errors.Count == 0;

    public int ByteCount => Segments.Sum(s => s.Data.Length);

    /// <summary>Address of the first emitted byte, if any.</summary>
    public ushort? StartAddress => Listing.FirstOrDefault(l => l.Bytes.Length > 0)?.Address;

    public string FormatListing()
    {
        var sb = new StringBuilder();
        foreach (var line in Listing)
        {
            string address = line.Address is { } a && (line.Bytes.Length > 0 || line.Source.TrimStart().Length > 0) ? a.ToString("X4") : "    ";
            string bytes = string.Join(' ', line.Bytes.Take(3).Select(b => b.ToString("X2")));
            if (line.Bytes.Length > 3) bytes += "+";
            sb.Append($"{line.Line,5}  {address}  {bytes,-10}  {line.Source}\n");
        }

        return sb.ToString();
    }
}
