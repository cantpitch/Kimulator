using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Kimulator.App.Expansion;

/// <summary>
/// Shows a card photo scaled to fit, with its DIP switches drawn over the real ones in their current
/// positions. Clicking a switch flips it (like the real thing).
/// </summary>
public sealed class CardView : Control
{
    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(22, 22, 22));
    private static readonly IBrush Slot = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
    private static readonly IBrush Thumb = new SolidColorBrush(Color.FromRgb(240, 240, 235));
    private static readonly IBrush Outline = new SolidColorBrush(Color.FromArgb(200, 255, 220, 80));
    private static readonly Typeface Label = new("Inter, Segoe UI, sans-serif", FontStyle.Normal, FontWeight.SemiBold);

    private CardArt? _art;
    private int[] _switches = [];

    public CardView()
    {
        ClipToBounds = true;
    }

    /// <summary>Raised when a switch is clicked: bank index, switch index (0-based).</summary>
    public event Action<int, int>? SwitchToggled;

    /// <summary>Shows <paramref name="art"/> with the given switch settings (bit 0 = switch 1, per bank).</summary>
    public void Show(CardArt? art, params int[] switches)
    {
        _art = art;
        _switches = switches;
        Cursor = Cursor.Default;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));
        if (_art is null) return;

        var (dest, scale) = Fit();
        var bitmap = _art.Bitmap;
        context.DrawImage(bitmap, new Rect(bitmap.Size), dest);

        for (int b = 0; b < _art.Banks.Count; b++)
        {
            var bank = _art.Banks[b];
            var r = DrawnRect(bank, dest, scale);
            context.DrawRectangle(new SolidColorBrush(Color.FromUInt32(bank.Color)), new Pen(Outline, 1.5), r, 3, 3);
            int value = b < _switches.Length ? _switches[b] : 0;
            for (int s = 0; s < bank.Switches; s++)
            {
                var (slot, thumb) = SwitchGeometry(bank, r, s, (value >> s & 1) == 1);
                context.DrawRectangle(Slot, null, slot, 2, 2);
                context.DrawRectangle(Thumb, null, thumb, 1.5, 1.5);
            }

            var label = new FormattedText(bank.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Label, 12, Outline);
            context.DrawText(label, new Point(r.X + (r.Width - label.Width) / 2, r.Bottom + 3));
        }
    }

    /// <summary>The slot and thumb rectangles of switch <paramref name="index"/> in a drawn bank.</summary>
    private static (Rect Slot, Rect Thumb) SwitchGeometry(DipBank bank, Rect r, int index, bool on)
    {
        int n = bank.Switches;
        int position = bank.FirstAtStart ? index : n - 1 - index;
        double pad = 3;
        if (bank.Vertical)
        {
            double pitch = (r.Height - 2 * pad) / n;
            var slot = new Rect(r.X + pad + 2, r.Y + pad + position * pitch + pitch * 0.18, r.Width - 2 * pad - 4, pitch * 0.64);
            bool left = on == bank.OnAtStart;
            var thumb = new Rect(left ? slot.X : slot.Right - slot.Width * 0.45, slot.Y, slot.Width * 0.45, slot.Height);
            return (slot, thumb);
        }
        else
        {
            double pitch = (r.Width - 2 * pad) / n;
            var slot = new Rect(r.X + pad + position * pitch + pitch * 0.18, r.Y + pad + 2, pitch * 0.64, r.Height - 2 * pad - 4);
            bool top = on == bank.OnAtStart;
            var thumb = new Rect(slot.X, top ? slot.Y : slot.Bottom - slot.Height * 0.45, slot.Width, slot.Height * 0.45);
            return (slot, thumb);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (HitTest(e.GetPosition(this)) is { } hit)
        {
            SwitchToggled?.Invoke(hit.Bank, hit.Switch);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Cursor = HitTest(e.GetPosition(this)) is not null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    private (int Bank, int Switch)? HitTest(Point p)
    {
        if (_art is null) return null;
        var (dest, scale) = Fit();
        for (int b = 0; b < _art.Banks.Count; b++)
        {
            var bank = _art.Banks[b];
            var r = DrawnRect(bank, dest, scale);
            if (!r.Inflate(6).Contains(p)) continue;
            double t = bank.Vertical ? (p.Y - r.Y) / r.Height : (p.X - r.X) / r.Width;
            int position = Math.Clamp((int)(t * bank.Switches), 0, bank.Switches - 1);
            return (b, bank.FirstAtStart ? position : bank.Switches - 1 - position);
        }

        return null;
    }

    /// <summary>Where a bank is drawn: over the real switch, but at least big enough to read and click.</summary>
    private static Rect DrawnRect(DipBank bank, Rect dest, double scale)
    {
        const double minSide = 34;
        var r = Map(bank.Bounds, dest, scale);
        if (r.Width >= minSide && r.Height >= minSide) return r;
        double w = Math.Max(r.Width, minSide), h = Math.Max(r.Height, minSide);
        return new Rect(r.Center.X - w / 2, r.Center.Y - h / 2, w, h);
    }

    private (Rect Dest, double Scale) Fit()
    {
        double scale = Math.Min(Bounds.Width / _art!.SourceWidth, Bounds.Height / _art.SourceHeight);
        double w = _art.SourceWidth * scale, h = _art.SourceHeight * scale;
        return (new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h), scale);
    }

    private static Rect Map(Rect source, Rect dest, double scale) =>
        new(dest.X + source.X * scale, dest.Y + source.Y * scale, source.Width * scale, source.Height * scale);
}
