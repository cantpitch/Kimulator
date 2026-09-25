using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Kimulator.App.Debugging;

/// <summary>
/// Hex + ASCII view of a 64 KB memory snapshot. Click a byte to select it, type two hex digits to
/// change it; bytes that changed since the previous snapshot are highlighted.
/// </summary>
public sealed class HexView : Control
{
    public const int BytesPerRow = 16;
    public const int RowCount = 0x10000 / BytesPerRow;

    private static readonly Typeface Mono = new("Consolas, Menlo, DejaVu Sans Mono, monospace");
    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(20, 20, 20));
    private static readonly IBrush AddressBrush = new SolidColorBrush(Color.FromRgb(120, 150, 190));
    private static readonly IBrush ByteBrush = new SolidColorBrush(Color.FromRgb(215, 215, 215));
    private static readonly IBrush ZeroBrush = new SolidColorBrush(Color.FromRgb(110, 110, 110));
    private static readonly IBrush ChangedBrush = new SolidColorBrush(Color.FromRgb(255, 110, 90));
    private static readonly IBrush AsciiBrush = new SolidColorBrush(Color.FromRgb(160, 170, 140));
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.FromArgb(110, 80, 140, 255));
    private static readonly IBrush PendingBrush = new SolidColorBrush(Color.FromRgb(255, 220, 90));

    private byte[]? _memory;
    private byte[]? _previous;
    private int _topRow = 0x0200 / BytesPerRow;
    private int? _pendingNibble;

    public HexView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public double FontSize { get; set; } = 14;

    public ushort SelectedAddress { get; private set; } = 0x0200;

    public int TopRow
    {
        get => _topRow;
        set
        {
            int clamped = Math.Clamp(value, 0, Math.Max(0, RowCount - VisibleRows));
            if (clamped == _topRow) return;
            _topRow = clamped;
            TopRowChanged?.Invoke(_topRow);
            InvalidateVisual();
        }
    }

    public int VisibleRows => Math.Max(1, (int)(Bounds.Height / LineHeight));

    /// <summary>Raised when the user types a new value for a byte.</summary>
    public event Action<ushort, byte>? ByteEdited;

    public event Action<int>? TopRowChanged;

    private double LineHeight => Math.Ceiling(FontSize * 1.45);

    private double CharWidth => new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, ByteBrush).Width;

    /// <summary>Shows a new snapshot; <paramref name="markChanges"/> highlights bytes that differ from the last one.</summary>
    public void Update(byte[] memory, bool markChanges)
    {
        _previous = markChanges ? _memory ?? memory : memory;
        _memory = memory;
        InvalidateVisual();
    }

    public void GoTo(ushort address)
    {
        SelectedAddress = address;
        _pendingNibble = null;
        int row = address / BytesPerRow;
        if (row < TopRow || row >= TopRow + VisibleRows) TopRow = row - 2;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));
        if (_memory is null) return;

        double lh = LineHeight, cw = CharWidth;
        double hexX = 6 + cw * 6;
        double asciiX = hexX + cw * (BytesPerRow * 3 + 2);
        for (int r = 0; r <= VisibleRows && TopRow + r < RowCount; r++)
        {
            int rowAddress = (TopRow + r) * BytesPerRow;
            double y = r * lh;
            DrawText(context, rowAddress.ToString("X4"), 6, y, AddressBrush);

            for (int i = 0; i < BytesPerRow; i++)
            {
                int address = rowAddress + i;
                byte value = _memory[address];
                double x = hexX + cw * (i * 3 + (i >= 8 ? 1 : 0));
                bool selected = address == SelectedAddress;
                if (selected)
                {
                    context.FillRectangle(SelectedBrush, new Rect(x - 2, y, cw * 2 + 4, lh));
                    context.FillRectangle(SelectedBrush, new Rect(asciiX + cw * i, y, cw, lh));
                }

                string text = selected && _pendingNibble is { } nibble ? $"{nibble:X}_" : value.ToString("X2");
                IBrush brush = selected && _pendingNibble is not null ? PendingBrush
                    : _previous is not null && _previous[address] != value ? ChangedBrush
                    : value == 0 ? ZeroBrush : ByteBrush;
                DrawText(context, text, x, y, brush);

                char c = value is >= 0x20 and < 0x7F ? (char)value : '.';
                DrawText(context, c.ToString(), asciiX + cw * i, y, AsciiBrush);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var p = e.GetPosition(this);
        double cw = CharWidth;
        double hexX = 6 + cw * 6;
        int row = TopRow + (int)(p.Y / LineHeight);
        double col = (p.X - hexX) / cw;
        if (col < 0 || row >= RowCount) return;
        int i = (int)(col / 3);
        if (col >= 8 * 3 + 1) i = (int)((col - 1) / 3); // gap after 8 bytes
        if (i >= BytesPerRow) return;
        SelectedAddress = (ushort)(row * BytesPerRow + i);
        _pendingNibble = null;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        TopRow -= (int)Math.Round(e.Delta.Y * 3);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return;

        int? digit = e.Key switch
        {
            >= Key.D0 and <= Key.D9 => e.Key - Key.D0,
            >= Key.NumPad0 and <= Key.NumPad9 => e.Key - Key.NumPad0,
            >= Key.A and <= Key.F => 10 + (e.Key - Key.A),
            _ => null,
        };

        if (digit is not null)
        {
            if (_pendingNibble is null)
            {
                _pendingNibble = digit;
            }
            else
            {
                ByteEdited?.Invoke(SelectedAddress, (byte)(_pendingNibble.Value << 4 | digit.Value));
                _pendingNibble = null;
                Move(1);
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        e.Handled = true;
        switch (e.Key)
        {
            case Key.Right: Move(1); break;
            case Key.Left: Move(-1); break;
            case Key.Down: Move(BytesPerRow); break;
            case Key.Up: Move(-BytesPerRow); break;
            case Key.PageDown: Move(BytesPerRow * (VisibleRows - 1)); break;
            case Key.PageUp: Move(-BytesPerRow * (VisibleRows - 1)); break;
            case Key.Escape: _pendingNibble = null; InvalidateVisual(); break;
            default: e.Handled = false; break;
        }
    }

    private void Move(int delta)
    {
        GoTo((ushort)(SelectedAddress + delta));
    }

    private void DrawText(DrawingContext context, string text, double x, double y, IBrush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, brush);
        context.DrawText(formatted, new Point(x, y + (LineHeight - formatted.Height) / 2));
    }
}
