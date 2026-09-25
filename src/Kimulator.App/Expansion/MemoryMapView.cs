using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Kimulator.Kim1.Cards;

namespace Kimulator.App.Expansion;

/// <summary>The 64K address space as a 16×16 grid of 256-byte pages, coloured by what answers there.</summary>
public sealed class MemoryMapView : Control
{
    private enum PageKind { Empty, Kim1, Mirror, Card, Conflict }

    private static readonly Dictionary<PageKind, IBrush> Brushes = new()
    {
        [PageKind.Empty] = new SolidColorBrush(Color.FromRgb(36, 36, 36)),
        [PageKind.Kim1] = new SolidColorBrush(Color.FromRgb(70, 120, 190)),
        [PageKind.Mirror] = new SolidColorBrush(Color.FromRgb(45, 62, 88)),
        [PageKind.Card] = new SolidColorBrush(Color.FromRgb(70, 160, 90)),
        [PageKind.Conflict] = new SolidColorBrush(Color.FromRgb(210, 70, 60)),
    };

    private static readonly Typeface Mono = new("Consolas, Menlo, DejaVu Sans Mono, monospace");
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(170, 170, 170));

    private readonly PageKind[] _pages = new PageKind[256];

    public MemoryMapView()
    {
        Height = 16 * 14 + 30;
    }

    public void Update(ExpansionConfig config)
    {
        var regions = config.MemoryMap();
        for (int p = 0; p < 256; p++)
        {
            var range = new AddressRange((ushort)(p << 8), (ushort)((p << 8) | 0xFF));
            int cards = regions.Count(r => !r.OnKim1 && r.Range.Overlaps(range));
            bool kim1 = regions.Any(r => r.OnKim1 && r.Range.Overlaps(range));
            ushort mirrorOf = (ushort)((p << 8) & 0x1FFF);
            bool mirror = !config.Kim4 && p >= 0x20
                && regions.Any(r => r.OnKim1 && r.Range.Overlaps(new AddressRange(mirrorOf, (ushort)(mirrorOf | 0xFF))));

            _pages[p] = cards > 1 || (cards == 1 && kim1) ? PageKind.Conflict
                : cards == 1 ? PageKind.Card
                : kim1 ? PageKind.Kim1
                : mirror ? PageKind.Mirror
                : PageKind.Empty;
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        double labelWidth = 44, top = 2;
        double cell = Math.Min((Bounds.Width - labelWidth) / 16, 14);
        for (int row = 0; row < 16; row++)
        {
            var label = new FormattedText($"{row:X}000", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 11, TextBrush);
            context.DrawText(label, new Point(0, top + row * cell + (cell - label.Height) / 2));
            for (int col = 0; col < 16; col++)
            {
                var kind = _pages[row * 16 + col];
                context.FillRectangle(Brushes[kind], new Rect(labelWidth + col * cell, top + row * cell, cell - 1, cell - 1));
            }
        }

        // Legend
        double y = top + 16 * cell + 8, x = 0;
        foreach (var (kind, text) in new[] { (PageKind.Kim1, "KIM-1"), (PageKind.Mirror, "KIM-1 mirror"), (PageKind.Card, "card"), (PageKind.Conflict, "conflict"), (PageKind.Empty, "nothing") })
        {
            context.FillRectangle(Brushes[kind], new Rect(x, y + 2, 10, 10));
            var t = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 11, TextBrush);
            context.DrawText(t, new Point(x + 14, y));
            x += 14 + t.Width + 12;
        }
    }
}
