using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Kimulator.Core.Debugging;

namespace Kimulator.App.Debugging;

/// <summary>
/// Scrollable disassembly of a memory snapshot with a breakpoint gutter and a current-PC marker.
/// Only the visible lines are decoded and drawn.
/// </summary>
public sealed class DisassemblyView : Control
{
    private const double GutterWidth = 30;

    private static readonly Typeface Mono = new("Consolas, Menlo, DejaVu Sans Mono, monospace");
    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(20, 20, 20));
    private static readonly IBrush GutterBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly IBrush PcLineBrush = new SolidColorBrush(Color.FromArgb(90, 230, 190, 40));
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.FromArgb(70, 80, 140, 255));
    private static readonly IBrush BreakpointBrush = new SolidColorBrush(Color.FromRgb(220, 50, 50));
    private static readonly IBrush PcArrowBrush = new SolidColorBrush(Color.FromRgb(240, 200, 40));
    private static readonly IBrush AddressBrush = new SolidColorBrush(Color.FromRgb(120, 150, 190));
    private static readonly IBrush BytesBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(110, 200, 140));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220));
    private static readonly IBrush UndocumentedBrush = new SolidColorBrush(Color.FromRgb(200, 140, 220));

    private DebugSnapshot? _snapshot;
    private HashSet<ushort> _breakpoints = [];
    private List<DisassembledInstruction> _lines = [];

    public DisassemblyView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public double FontSize { get; set; } = 14;

    public SymbolTable? Symbols { get; set; }

    /// <summary>First address shown.</summary>
    public ushort TopAddress { get; private set; } = 0x0200;

    /// <summary>The line the user selected (for run-to-cursor and F9).</summary>
    public ushort? SelectedAddress { get; private set; }

    /// <summary>Raised when the gutter is clicked.</summary>
    public event Action<ushort>? BreakpointToggleRequested;

    private double LineHeight => Math.Ceiling(FontSize * 1.45);

    private int VisibleLines => Math.Max(1, (int)(Bounds.Height / LineHeight));

    public void Update(DebugSnapshot snapshot)
    {
        _snapshot = snapshot;
        _breakpoints = snapshot.Breakpoints
            .Where(b => b.Enabled && b.Kind.HasFlag(BreakpointKind.Execute) && b.Start == b.End)
            .Select(b => b.Start)
            .ToHashSet();
        InvalidateVisual();
    }

    public void ScrollTo(ushort address, int linesAbove = 4)
    {
        if (_snapshot is null)
        {
            TopAddress = address;
            return;
        }

        TopAddress = linesAbove > 0 ? Disassembler.FindStartBefore(_snapshot.Read, address, linesAbove) : address;
        InvalidateVisual();
    }

    /// <summary>Scrolls so <paramref name="address"/> is visible (keeping the view if it already is).</summary>
    public void EnsureVisible(ushort address)
    {
        var lines = Decode(VisibleLines);
        if (lines.Count > 2 && lines.Take(lines.Count - 2).Any(l => l.Address == address)) return;
        ScrollTo(address);
    }

    public void Select(ushort address)
    {
        SelectedAddress = address;
        EnsureVisible(address);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));
        context.FillRectangle(GutterBrush, new Rect(0, 0, GutterWidth, Bounds.Height));
        if (_snapshot is null) return;

        _lines = Decode(VisibleLines + 1);
        double lh = LineHeight;
        double charWidth = Measure("0").Width;
        for (int i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            double y = i * lh;
            var row = new Rect(GutterWidth, y, Bounds.Width - GutterWidth, lh);
            if (line.Address == _snapshot.PC) context.FillRectangle(PcLineBrush, row);
            if (line.Address == SelectedAddress) context.FillRectangle(SelectedBrush, row);

            double cy = y + lh / 2;
            if (_breakpoints.Contains(line.Address))
                context.DrawEllipse(BreakpointBrush, null, new Point(10, cy), 6, 6);
            if (line.Address == _snapshot.PC)
                context.DrawGeometry(PcArrowBrush, null, Arrow(new Point(17, cy)));

            double x = GutterWidth + 6;
            DrawText(context, line.Address.ToString("X4"), x, y, AddressBrush);
            x += charWidth * 6;
            DrawText(context, string.Join(' ', line.Bytes.Select(b => b.ToString("X2"))), x, y, BytesBrush);
            x += charWidth * 10;
            if (Symbols?.NameOf(line.Address) is { } label)
                DrawText(context, label, x, y, LabelBrush);
            x += charWidth * 9;
            DrawText(context, line.Text, x, y, line.Info.Undocumented ? UndocumentedBrush : TextBrush);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var p = e.GetPosition(this);
        int index = (int)(p.Y / LineHeight);
        if (index < 0 || index >= _lines.Count) return;
        ushort address = _lines[index].Address;
        if (p.X < GutterWidth) BreakpointToggleRequested?.Invoke(address);
        else
        {
            SelectedAddress = address;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ScrollLines(e.Delta.Y > 0 ? -3 : 3);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_snapshot is null || e.KeyModifiers != KeyModifiers.None) return;
        switch (e.Key)
        {
            case Key.Down: MoveSelection(1); break;
            case Key.Up: MoveSelection(-1); break;
            case Key.PageDown: ScrollLines(VisibleLines - 1); break;
            case Key.PageUp: ScrollLines(-(VisibleLines - 1)); break;
            default: return;
        }

        e.Handled = true;
    }

    private void MoveSelection(int delta)
    {
        if (_snapshot is null) return;
        ushort current = SelectedAddress ?? TopAddress;
        ushort next = delta > 0
            ? Disassembler.Disassemble(_snapshot.Read, current).Next
            : Disassembler.FindStartBefore(_snapshot.Read, current, 1);
        Select(next);
    }

    private void ScrollLines(int lines)
    {
        if (_snapshot is null) return;
        if (lines > 0)
        {
            var decoded = Decode(lines + 1);
            TopAddress = decoded[Math.Min(lines, decoded.Count - 1)].Address;
        }
        else
        {
            TopAddress = Disassembler.FindStartBefore(_snapshot.Read, TopAddress, -lines);
        }

        InvalidateVisual();
    }

    private List<DisassembledInstruction> Decode(int count) =>
        _snapshot is null ? [] : Disassembler.DisassembleRange(_snapshot.Read, TopAddress, count, Symbols);

    private void DrawText(DrawingContext context, string text, double x, double y, IBrush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, brush);
        context.DrawText(formatted, new Point(x, y + (LineHeight - formatted.Height) / 2));
    }

    private FormattedText Measure(string text) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, TextBrush);

    private static Geometry Arrow(Point tip)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(new Point(tip.X + 8, tip.Y), true);
        ctx.LineTo(new Point(tip.X, tip.Y - 5));
        ctx.LineTo(new Point(tip.X, tip.Y + 5));
        ctx.EndFigure(true);
        return geometry;
    }
}
