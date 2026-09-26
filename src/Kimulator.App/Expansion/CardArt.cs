using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Kimulator.Kim1.Cards;

namespace Kimulator.App.Expansion;

/// <summary>
/// A DIP switch bank on a card photo. Coordinates are pixels of the original photo.
/// <see cref="Vertical"/>: switches are stacked top-to-bottom (and slide sideways); otherwise side by side (sliding up/down).
/// <see cref="FirstAtStart"/>: switch 1 is at the top (vertical) or left (horizontal) end.
/// <see cref="OnAtStart"/>: "on" is toward the left (vertical banks) or the top (horizontal banks).
/// </summary>
public sealed record DipBank(string Label, Rect Bounds, bool Vertical, bool FirstAtStart, bool OnAtStart, int Switches = 4, uint Color = 0xFF265CAA);

/// <summary>A card photo and where its switches are.</summary>
public sealed record CardArt(string Asset, int SourceWidth, int SourceHeight, IReadOnlyList<DipBank> Banks)
{
    private Bitmap? _bitmap;
    private Bitmap? _thumbnail;

    public Bitmap Bitmap => _bitmap ??= new Bitmap(AssetLoader.Open(new Uri($"avares://Kimulator/Assets/cards/{Asset}")));

    /// <summary>A small copy (48 pixels high) for the card indicator on the main window.</summary>
    public Bitmap Thumbnail => _thumbnail ??= Bitmap.CreateScaledBitmap(
        new PixelSize((int)Math.Round(48.0 * SourceWidth / SourceHeight), 48), BitmapInterpolationMode.HighQuality);

    // Measured from the photos in images/. Orientation of the switches follows the MOS manual's figure
    // (KIM-2/3) and the "OPEN" legend printed on the KIM-5's switches.
    public static CardArt Kim2 { get; } = new("kim2.jpg", 1024, 768,
        [new DipBank("Address", new Rect(836, 535, 39, 49), Vertical: false, FirstAtStart: true, OnAtStart: true)]);

    public static CardArt Kim3 { get; } = new("kim3.jpg", 1107, 785,
        [new DipBank("Address", new Rect(945, 600, 44, 49), Vertical: true, FirstAtStart: false, OnAtStart: true)]);

    public static CardArt Kim5 { get; } = new("kim5.jpg", 2560, 1920,
    [
        new DipBank("S1", new Rect(1940, 1317, 105, 120), Vertical: true, FirstAtStart: true, OnAtStart: false, Color: 0xFFC0392B),
        new DipBank("S2", new Rect(2130, 1317, 110, 120), Vertical: true, FirstAtStart: true, OnAtStart: false, Color: 0xFFC0392B),
    ]);

    // MTU's photo; the switch in socket S1 has six positions (the address pairs), "ON" to the right.
    public static CardArt K1008 { get; } = new("k1008.jpg", 2193, 1072,
        [new DipBank("S1", new Rect(412, 690, 73, 125), Vertical: true, FirstAtStart: true, OnAtStart: false, Switches: 6, Color: 0xFF2A2A2A)]);

    public static CardArt Kim4 { get; } = new("kim4.jpg", 1024, 858, []);

    public static CardArt Kim4WithKim1 { get; } = new("kim4-kim1.jpg", 1024, 652, []);

    public static CardArt For(CardType type) => type switch
    {
        CardType.Kim2 => Kim2,
        CardType.Kim3 => Kim3,
        CardType.K1008 => K1008,
        _ => Kim5,
    };
}
