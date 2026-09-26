using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Kimulator.Kim1.Cards;

namespace Kimulator.App.Video;

/// <summary>The monitor on a K-1008's video output: its 320 × 200 dots, drawn with sharp edges.</summary>
public sealed class VisibleMemoryView : Control
{
    private static readonly IBrush Bezel = new SolidColorBrush(Color.FromRgb(8, 8, 8));

    private readonly WriteableBitmap _bitmap = new(new PixelSize(VisibleMemoryCard.Width, VisibleMemoryCard.Height),
        new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
    private readonly byte[] _shown = new byte[VisibleMemoryCard.DisplayBytes];
    private readonly int[] _row = new int[VisibleMemoryCard.Width];
    private uint _phosphor = 0xFFF0F0F0;
    private bool _stale = true;

    public VisibleMemoryView()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    /// <summary>Colour of a lit dot (ARGB).</summary>
    public uint Phosphor
    {
        get => _phosphor;
        set
        {
            _phosphor = value;
            _stale = true;
        }
    }

    /// <summary>Stretch the picture to a 4:3 monitor (true, as it looked) or show square dots.</summary>
    public bool MonitorShape
    {
        get;
        set
        {
            field = value;
            InvalidateVisual();
        }
    } = true;

    /// <summary>The picture as shown, for saving.</summary>
    public Bitmap Picture => _bitmap;

    /// <summary>Copies the card's RAM to the picture if it changed. <paramref name="memory"/> null = no card (blank).</summary>
    public void Update(byte[]? memory)
    {
        var source = memory.AsSpan(0, memory is null ? 0 : VisibleMemoryCard.DisplayBytes);
        if (!_stale && (memory is null ? _shown.AsSpan().IndexOfAnyExcept((byte)0) < 0 : source.SequenceEqual(_shown))) return;
        _stale = false;
        if (memory is null) Array.Clear(_shown);
        else source.CopyTo(_shown);

        int on = unchecked((int)_phosphor), off = unchecked((int)0xFF000000);
        using (var frame = _bitmap.Lock())
        {
            for (int y = 0; y < VisibleMemoryCard.Height; y++)
            {
                for (int b = 0; b < VisibleMemoryCard.BytesPerLine; b++)
                {
                    byte value = _shown[y * VisibleMemoryCard.BytesPerLine + b];
                    for (int bit = 0; bit < 8; bit++)
                        _row[b * 8 + bit] = (value & (0x80 >> bit)) != 0 ? on : off;
                }

                Marshal.Copy(_row, 0, frame.Address + y * frame.RowBytes, _row.Length);
            }
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Bezel, new Rect(Bounds.Size));
        double aspect = MonitorShape ? 4.0 / 3.0 : (double)VisibleMemoryCard.Width / VisibleMemoryCard.Height;
        double margin = 12;
        double w = Math.Max(0, Bounds.Width - 2 * margin), h = Math.Max(0, Bounds.Height - 2 * margin);
        if (w / h > aspect) w = h * aspect;
        else h = w / aspect;
        var dest = new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
        context.DrawImage(_bitmap, new Rect(0, 0, VisibleMemoryCard.Width, VisibleMemoryCard.Height), dest);
    }
}
