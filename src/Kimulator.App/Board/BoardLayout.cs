using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Platform;
using Kimulator.Kim1;

namespace Kimulator.App.Board;

/// <summary>
/// Where things are on a board photo. All coordinates are pixels in the original photo
/// (<see cref="SourceWidth"/> × <see cref="SourceHeight"/>), so the bitmap can be swapped for
/// another resolution without touching the layout.
/// </summary>
public sealed class BoardLayout
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public List<KeyHotspot> Keys { get; init; } = [];
    public LayoutRect SingleStepSwitch { get; init; }
    public DisplayLayout Display { get; init; } = new();
    public LayoutRect CompactView { get; init; }

    public Rect FullView => new(0, 0, SourceWidth, SourceHeight);

    public static BoardLayout Load(Uri uri)
    {
        using var stream = AssetLoader.Open(uri);
        return JsonSerializer.Deserialize<BoardLayout>(stream, JsonOptions)
               ?? throw new InvalidOperationException($"Could not read board layout {uri}.");
    }
}

public sealed class KeyHotspot
{
    public Kim1Key Key { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }

    public Rect Bounds => new(X, Y, Width, Height);
}

public sealed class DisplayLayout
{
    /// <summary>Horizontal shift of the top of a digit relative to its bottom, as a fraction of digit height.</summary>
    public double Slant { get; init; }

    public List<LayoutRect> Digits { get; init; } = [];
}

public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public Rect ToRect() => new(X, Y, Width, Height);
}
