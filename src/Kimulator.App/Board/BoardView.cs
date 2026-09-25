using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Kimulator.Kim1;

namespace Kimulator.App.Board;

/// <summary>
/// Draws a board photo scaled to fit, treats the key areas of the photo as buttons, and paints the
/// LED digits on top of the photo's display window from the emulated segment duty cycles.
/// </summary>
public sealed class BoardView : Control
{
    // Duty cycle at which a segment is drawn at full brightness (the monitor lights each digit ~1/7 of the time).
    private const float FullBrightnessDuty = 0.12f;

    private static readonly Color LedColor = Color.FromRgb(255, 48, 24);
    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(24, 24, 24));
    private static readonly IBrush PressedShade = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
    private static readonly IBrush SwitchBody = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly IBrush SwitchThumb = new SolidColorBrush(Color.FromRgb(205, 205, 200));
    private static readonly IBrush HoverOutline = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));

    private readonly HashSet<Kim1Key> _pressed = [];
    private readonly Dictionary<IPointer, Kim1Key> _pointerKeys = [];
    private KeyHotspot? _hover;
    private DisplayFrame _frame = DisplayFrame.Blank;
    private bool _singleStep;

    public static readonly StyledProperty<Rect> ViewRectProperty =
        AvaloniaProperty.Register<BoardView, Rect>(nameof(ViewRect));

    static BoardView()
    {
        AffectsRender<BoardView>(ViewRectProperty);
        AffectsMeasure<BoardView>(ViewRectProperty);
    }

    public BoardView()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public required BoardLayout Layout { get; init; }
    public required Bitmap BoardImage { get; init; }

    /// <summary>Visible region of the photo, in layout (source) coordinates.</summary>
    public Rect ViewRect
    {
        get => GetValue(ViewRectProperty);
        set => SetValue(ViewRectProperty, value);
    }

    /// <summary>Raised when a key is pressed (true) or released (false) with the mouse or touch.</summary>
    public event Action<Kim1Key, bool>? KeyChanged;

    /// <summary>Raised when the SST switch is clicked.</summary>
    public event Action? SingleStepToggled;

    public bool SingleStep
    {
        get => _singleStep;
        set
        {
            if (_singleStep == value) return;
            _singleStep = value;
            InvalidateVisual();
        }
    }

    /// <summary>Shows a key as pressed or released (e.g. from the PC keyboard).</summary>
    public void SetKeyVisual(Kim1Key key, bool down)
    {
        if (down ? _pressed.Add(key) : _pressed.Remove(key))
            InvalidateVisual();
    }

    /// <summary>Updates the LED display; redraws only when the frame changed.</summary>
    public void UpdateDisplay(DisplayFrame frame)
    {
        if (ReferenceEquals(frame, _frame)) return;
        _frame = frame;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var view = ViewRect;
        if (view.Width <= 0 || view.Height <= 0) return default;
        double scale = Math.Min(
            double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width / view.Width,
            double.IsInfinity(availableSize.Height) ? double.MaxValue : availableSize.Height / view.Height);
        if (scale == double.MaxValue) scale = 0.25;
        return new Size(view.Width * scale, view.Height * scale);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));
        var view = ViewRect;
        if (view.Width <= 0) return;

        var (dest, scale) = Fit(view);
        double bitmapScale = BoardImage.PixelSize.Width / (double)Layout.SourceWidth;

        context.DrawImage(BoardImage, ToBitmap(view, bitmapScale), dest);

        using (context.PushClip(dest))
        {
            foreach (var key in Layout.Keys)
            {
                if (_pressed.Contains(key.Key))
                    DrawPressedKey(context, key, view, dest, scale, bitmapScale);
                else if (ReferenceEquals(key, _hover))
                {
                    double radius = key.Width * 0.08 * scale;
                    context.DrawRectangle(null, new Pen(HoverOutline, 1.5), Map(key.Bounds, view, dest, scale), radius, radius);
                }
            }

            DrawSingleStepSwitch(context, view, dest, scale);
            DrawDisplay(context, view, dest, scale);
        }
    }

    // ---------------------------------------------------------------- drawing

    private void DrawPressedKey(DrawingContext context, KeyHotspot key, Rect view, Rect dest, double scale, double bitmapScale)
    {
        // Redraw the key cap slightly lower and shaded so it reads as pushed in.
        var keyRect = Map(key.Bounds, view, dest, scale);
        double radius = key.Width * 0.11 * scale;
        using (context.PushClip(new RoundedRect(keyRect, radius)))
        {
            context.DrawImage(BoardImage, ToBitmap(key.Bounds, bitmapScale), keyRect.Translate(new Vector(0, key.Height * 0.04 * scale)));
            context.FillRectangle(PressedShade, keyRect);
        }
    }

    private void DrawSingleStepSwitch(DrawingContext context, Rect view, Rect dest, double scale)
    {
        var sw = Map(Layout.SingleStepSwitch.ToRect(), view, dest, scale);
        double inset = sw.Height * 0.12;
        context.DrawRectangle(SwitchBody, null, sw, inset, inset);
        double thumbWidth = sw.Width * 0.45;
        bool thumbRight = _singleStep == Layout.SingleStepOnRight;
        double x = thumbRight ? sw.Right - thumbWidth - inset : sw.X + inset;
        var thumb = new Rect(x, sw.Y + inset, thumbWidth, sw.Height - 2 * inset);
        context.DrawRectangle(SwitchThumb, null, thumb, inset * 0.6, inset * 0.6);
    }

    private void DrawDisplay(DrawingContext context, Rect view, Rect dest, double scale)
    {
        var frame = _frame;
        double slant = Layout.Display.Slant;
        double thickness = Layout.Display.SegmentThickness;
        for (int d = 0; d < Layout.Display.Digits.Count && d < LedDisplay.DigitCount; d++)
        {
            var box = Layout.Display.Digits[d].ToRect();
            for (int s = 0; s < 7; s++)
            {
                float level = Math.Clamp(frame[d, s] / FullBrightnessDuty, 0f, 1f);
                if (level < 0.03f) continue;
                var geometry = SegmentGeometry(s, box, slant, thickness, view, dest, scale);
                DrawLitSegment(context, geometry, level, box.Width * scale);
            }
        }
    }

    private static void DrawLitSegment(DrawingContext context, Geometry geometry, float level, double digitWidth)
    {
        // Soft glow (wide translucent strokes) under a bright core, sized relative to the digit.
        byte glowAlpha = (byte)(70 * level);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(LedColor, glowAlpha / 255.0), digitWidth * 0.19, lineJoin: PenLineJoin.Round), geometry);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(LedColor, glowAlpha * 1.6 / 255.0), digitWidth * 0.08, lineJoin: PenLineJoin.Round), geometry);
        var core = Color.FromRgb(255, (byte)(48 + 110 * level), (byte)(24 + 60 * level));
        context.DrawGeometry(new SolidColorBrush(core, 0.35 + 0.65 * level), null, geometry);
    }

    /// <summary>Builds a hexagonal segment (0=a .. 6=g) inside a slanted digit box.</summary>
    private static Geometry SegmentGeometry(int segment, Rect box, double slant, double tu, Rect view, Rect dest, double scale)
    {
        // tu: thickness as a fraction of width
        double tv = tu * box.Width / box.Height; // same thickness as a fraction of height
        const double gap = 0.035;
        double left = tu / 2, right = 1 - tu / 2, top = tv / 2, mid = 0.5, bottom = 1 - tv / 2;

        (double u, double v)[] points = segment switch
        {
            0 => Horizontal(top, left + gap, right - gap),
            1 => Vertical(right, top + gap, mid - gap),
            2 => Vertical(right, mid + gap, bottom - gap),
            3 => Horizontal(bottom, left + gap, right - gap),
            4 => Vertical(left, mid + gap, bottom - gap),
            5 => Vertical(left, top + gap, mid - gap),
            _ => Horizontal(mid, left + gap, right - gap),
        };

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (int i = 0; i < points.Length; i++)
            {
                var (u, v) = points[i];
                double sx = box.X + u * box.Width + (1 - v) * slant * box.Height;
                double sy = box.Y + v * box.Height;
                var p = new Point(dest.X + (sx - view.X) * scale, dest.Y + (sy - view.Y) * scale);
                if (i == 0) ctx.BeginFigure(p, isFilled: true);
                else ctx.LineTo(p);
            }

            ctx.EndFigure(true);
        }

        return geometry;

        (double, double)[] Horizontal(double v, double u0, double u1) =>
            [(u0, v), (u0 + tu / 2, v - tv / 2), (u1 - tu / 2, v - tv / 2), (u1, v), (u1 - tu / 2, v + tv / 2), (u0 + tu / 2, v + tv / 2)];

        (double, double)[] Vertical(double u, double v0, double v1) =>
            [(u, v0), (u + tu / 2, v0 + tv / 2), (u + tu / 2, v1 - tv / 2), (u, v1), (u - tu / 2, v1 - tv / 2), (u - tu / 2, v0 + tv / 2)];
    }

    // ---------------------------------------------------------------- input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = ToSource(e.GetPosition(this));
        if (point is null) return;

        if (Layout.SingleStepSwitch.ToRect().Inflate(10).Contains(point.Value))
        {
            SingleStepToggled?.Invoke();
            e.Handled = true;
            return;
        }

        var hit = HitTestKey(point.Value);
        if (hit is null) return;

        e.Pointer.Capture(this);
        _pointerKeys[e.Pointer] = hit.Key;
        SetKeyVisual(hit.Key, true);
        KeyChanged?.Invoke(hit.Key, true);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        ReleasePointer(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        ReleasePointer(e.Pointer);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = ToSource(e.GetPosition(this));
        var hover = point is null ? null : HitTestKey(point.Value);
        bool overSwitch = point is not null && Layout.SingleStepSwitch.ToRect().Inflate(10).Contains(point.Value);
        Cursor = hover is not null || overSwitch ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        if (!ReferenceEquals(hover, _hover))
        {
            _hover = hover;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover is null) return;
        _hover = null;
        InvalidateVisual();
    }

    private void ReleasePointer(IPointer pointer)
    {
        if (!_pointerKeys.Remove(pointer, out var key)) return;
        SetKeyVisual(key, false);
        KeyChanged?.Invoke(key, false);
    }

    private KeyHotspot? HitTestKey(Point source)
    {
        foreach (var key in Layout.Keys)
        {
            if (key.Bounds.Contains(source)) return key;
        }

        return null;
    }

    // ---------------------------------------------------------------- coordinates

    private (Rect Dest, double Scale) Fit(Rect view)
    {
        double scale = Math.Min(Bounds.Width / view.Width, Bounds.Height / view.Height);
        double w = view.Width * scale, h = view.Height * scale;
        return (new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h), scale);
    }

    private Point? ToSource(Point control)
    {
        var view = ViewRect;
        if (view.Width <= 0) return null;
        var (dest, scale) = Fit(view);
        if (!dest.Contains(control)) return null;
        return new Point(view.X + (control.X - dest.X) / scale, view.Y + (control.Y - dest.Y) / scale);
    }

    private static Rect Map(Rect source, Rect view, Rect dest, double scale) =>
        new(dest.X + (source.X - view.X) * scale, dest.Y + (source.Y - view.Y) * scale, source.Width * scale, source.Height * scale);

    private static Rect ToBitmap(Rect source, double bitmapScale) =>
        new(source.X * bitmapScale, source.Y * bitmapScale, source.Width * bitmapScale, source.Height * bitmapScale);
}
