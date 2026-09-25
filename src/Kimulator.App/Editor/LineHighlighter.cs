using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace Kimulator.App.Editor;

/// <summary>Tints whole editor lines: errors, breakpoints and the line holding the current PC.</summary>
public sealed class LineHighlighter : IBackgroundRenderer
{
    public static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromArgb(70, 230, 60, 60));
    public static readonly IBrush PcBrush = new SolidColorBrush(Color.FromArgb(90, 230, 190, 40));
    public static readonly IBrush BreakpointBrush = new SolidColorBrush(Color.FromRgb(210, 50, 50));

    public HashSet<int> ErrorLines { get; } = [];
    public HashSet<int> BreakpointLines { get; } = [];
    public int? PcLine { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid) return;
        foreach (var visualLine in textView.VisualLines)
        {
            int line = visualLine.FirstDocumentLine.LineNumber;
            double y = visualLine.VisualTop - textView.VerticalOffset;
            var rect = new Rect(0, y, textView.Bounds.Width, visualLine.Height);

            if (ErrorLines.Contains(line)) drawingContext.FillRectangle(ErrorBrush, rect);
            if (PcLine == line) drawingContext.FillRectangle(PcBrush, rect);
            if (BreakpointLines.Contains(line)) drawingContext.FillRectangle(BreakpointBrush, new Rect(0, y, 4, visualLine.Height));
        }
    }
}
