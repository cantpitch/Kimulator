using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Kimulator.Kim1.Cards;

namespace Kimulator.App.Video;

/// <summary>
/// The monitor on the K-1008's video output. It shows the card's RAM 60 times a second, like the
/// card's own 60.1 Hz scan, whatever the emulated processor is doing.
/// </summary>
public partial class VisibleMemoryWindow : Window
{
    private static readonly (string Name, uint Color)[] Phosphors =
    [
        ("White", 0xFFF0F0F0),
        ("Green", 0xFF40FF70),
        ("Amber", 0xFFFFB000),
    ];

    private readonly EmulatorSession _session;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer;
    private bool _updating;

    public VisibleMemoryWindow() : this(null!, new AppSettings()) { } // designer

    public VisibleMemoryWindow(EmulatorSession session, AppSettings settings)
    {
        InitializeComponent();
        _session = session;
        _settings = settings;

        _updating = true;
        PhosphorBox.ItemsSource = Phosphors.Select(p => p.Name + " phosphor").ToList();
        int phosphor = Math.Max(0, Array.FindIndex(Phosphors, p => p.Name == settings.VisibleMemoryPhosphor));
        PhosphorBox.SelectedIndex = phosphor;
        Screen.Phosphor = Phosphors[phosphor].Color;
        ShapeBox.IsChecked = Screen.MonitorShape = settings.VisibleMemoryMonitorShape;
        _updating = false;

        if (session is not null) session.ExpansionChanged += OnCardsChanged;
        OnCardsChanged();

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Screen.Update(SelectedCard?.Memory));
        _timer.Start();
    }

    private VisibleMemoryCard? SelectedCard =>
        _session?.VisibleMemories.ElementAtOrDefault(Math.Max(0, CardBox.SelectedIndex));

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        if (_session is not null) _session.ExpansionChanged -= OnCardsChanged;
        base.OnClosed(e);
    }

    private void OnCardsChanged()
    {
        var cards = _session?.VisibleMemories ?? [];
        _updating = true;
        int selected = CardBox.SelectedIndex;
        CardBox.ItemsSource = cards.Select((c, i) => $"K-1008 #{i + 1} at " + (c.BaseAddress is { } a ? $"${a:X4}" : "no address")).ToList();
        CardBox.SelectedIndex = cards.Count == 0 ? -1 : Math.Clamp(selected, 0, cards.Count - 1);
        CardBox.IsVisible = cards.Count > 1;
        _updating = false;
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        InfoText.Text = SelectedCard switch
        {
            null => "No K-1008 is installed. Add one in Machine › Expansion cards…",
            { BaseAddress: { } a } => $"320 × 200 dots at ${a:X4}–${a + VisibleMemoryCard.DisplayBytes - 1:X4}: 40 bytes a line from the top, "
                + $"leftmost dot in bit 7, 1 = lit. ${a + VisibleMemoryCard.DisplayBytes:X4}–${a + VisibleMemoryCard.Size - 1:X4} is ordinary RAM (not shown).",
            _ => "This K-1008's address switches are set wrongly, so the processor can't reach its memory. Fix them in Machine › Expansion cards…",
        };
    }

    private void OnCardChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        UpdateInfo();
    }

    private void OnPhosphorChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updating || PhosphorBox.SelectedIndex < 0) return;
        var (name, color) = Phosphors[PhosphorBox.SelectedIndex];
        _settings.VisibleMemoryPhosphor = name;
        Screen.Phosphor = color;
    }

    private void OnShapeChanged(object? sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _settings.VisibleMemoryMonitorShape = Screen.MonitorShape = ShapeBox.IsChecked == true;
    }

    private async void OnSavePicture(object? sender, RoutedEventArgs e)
    {
        using var stream = new MemoryStream();
        Screen.Picture.Save(stream, new PngBitmapEncoderOptions());
        await FileDialogs.SaveAsync(this, _settings, "Save picture", "visible-memory.png", FileDialogs.PngTypes, stream.ToArray());
    }
}
