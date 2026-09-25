using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Kimulator.App.Board;
using Kimulator.Kim1;

namespace Kimulator.App;

public partial class MainWindow : Window
{
    private readonly EmulatorSession _session = new();
    private readonly BoardView _board;
    private readonly DispatcherTimer _frameTimer;
    private readonly HashSet<Key> _heldKeys = [];
    private long _lastStatusCycles;
    private DateTime _lastStatusTime = DateTime.UtcNow;

    public MainWindow()
    {
        InitializeComponent();

        var layout = BoardLayout.Load(new Uri("avares://Kimulator.App/Assets/kim1-layout.json"));
        using var imageStream = AssetLoader.Open(new Uri("avares://Kimulator.App/Assets/kim1-board.jpg"));
        _board = new BoardView
        {
            Layout = layout,
            BoardImage = new Bitmap(imageStream),
            ViewRect = layout.FullView,
        };
        _board.KeyChanged += (key, down) => _session.SetKey(key, down);
        _board.SingleStepToggled += ToggleSingleStep;
        BoardHost.Content = _board;

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => ReleaseHeldKeys();

        _session.Runner.Faulted += ex => Dispatcher.UIThread.Post(() => StatusText.Text = $"Emulation stopped: {ex.Message}");

        _frameTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, OnFrame);
        _frameTimer.Start();

        if (Environment.GetCommandLineArgs().Contains("--compact"))
            Opened += (_, _) => Dispatcher.UIThread.Post(() => SetView(compact: true), DispatcherPriority.Loaded);
    }

    protected override void OnClosed(EventArgs e)
    {
        _frameTimer.Stop();
        _session.Dispose();
        base.OnClosed(e);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        _board.UpdateDisplay(_session.Display);

        var now = DateTime.UtcNow;
        double seconds = (now - _lastStatusTime).TotalSeconds;
        if (seconds < 0.5) return;
        long cycles = _session.Board.Cycles;
        double mhz = (cycles - _lastStatusCycles) / seconds / 1e6;
        _lastStatusCycles = cycles;
        _lastStatusTime = now;
        string mode = _session.Board.SingleStep ? "  ·  SST on" : string.Empty;
        StatusText.Text = $"{mhz:0.000} MHz{mode}";
    }

    // ---------------------------------------------------------------- keyboard

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            if (e.Key == Key.D1) { SetView(compact: false); e.Handled = true; }
            else if (e.Key == Key.D2) { SetView(compact: true); e.Handled = true; }
            return;
        }

        if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return;

        if (e.Key == Key.F9)
        {
            ToggleSingleStep();
            e.Handled = true;
            return;
        }

        var kimKey = KeyboardMap.ToKimKey(e.Key);
        if (kimKey is null) return;
        e.Handled = true;
        if (!_heldKeys.Add(e.Key)) return; // auto-repeat
        _board.SetKeyVisual(kimKey.Value, true);
        _session.SetKey(kimKey.Value, true);
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (!_heldKeys.Remove(e.Key)) return;
        var kimKey = KeyboardMap.ToKimKey(e.Key);
        if (kimKey is null) return;
        e.Handled = true;
        _board.SetKeyVisual(kimKey.Value, false);
        _session.SetKey(kimKey.Value, false);
    }

    /// <summary>Don't leave keys stuck down when the window loses focus mid-press.</summary>
    private void ReleaseHeldKeys()
    {
        foreach (var key in _heldKeys)
        {
            if (KeyboardMap.ToKimKey(key) is { } kimKey)
            {
                _board.SetKeyVisual(kimKey, false);
                _session.SetKey(kimKey, false);
            }
        }

        _heldKeys.Clear();
    }

    // ---------------------------------------------------------------- menu

    private void ToggleSingleStep()
    {
        bool on = !_board.SingleStep;
        _board.SingleStep = on;
        SingleStepMenu.IsChecked = on;
        _session.SetSingleStep(on);
    }

    private void TapKey(Kim1Key key)
    {
        _session.SetKey(key, true);
        _session.SetKey(key, false); // the session enforces a minimum hold
    }

    private void OnReset(object? sender, RoutedEventArgs e) => TapKey(Kim1Key.Reset);

    private void OnStop(object? sender, RoutedEventArgs e) => TapKey(Kim1Key.Stop);

    private void OnToggleSingleStep(object? sender, RoutedEventArgs e)
    {
        // The menu has already flipped IsChecked; sync everything else to it.
        bool on = SingleStepMenu.IsChecked;
        _board.SingleStep = on;
        _session.SetSingleStep(on);
    }

    private void OnPowerCycle(object? sender, RoutedEventArgs e) => _session.PowerCycle();

    private void OnTogglePresetVectors(object? sender, RoutedEventArgs e) =>
        _session.PresetInterruptVectors = PresetVectorsMenu.IsChecked;

    private void OnSpeed(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag }) return;
        double speed = double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);
        _session.Runner.Unthrottled = speed == 0;
        if (speed > 0) _session.Runner.Speed = speed;
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private void OnFullView(object? sender, RoutedEventArgs e) => SetView(compact: false);

    private void OnCompactView(object? sender, RoutedEventArgs e) => SetView(compact: true);

    private void SetView(bool compact)
    {
        var view = compact ? _board.Layout.CompactView.ToRect() : _board.Layout.FullView;
        FullViewMenu.IsChecked = !compact;
        CompactViewMenu.IsChecked = compact;
        if (_board.ViewRect == view) return;

        // Keep the height, adjust the width to the new aspect ratio.
        double chromeWidth = Bounds.Width - BoardHost.Bounds.Width;
        double boardHeight = BoardHost.Bounds.Height;
        _board.ViewRect = view;
        if (WindowState == WindowState.Normal && boardHeight > 0)
            Width = chromeWidth + boardHeight * view.Width / view.Height;
    }

    private async void OnShowKeys(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Keyboard shortcuts",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new TextBlock
            {
                Margin = new Thickness(16),
                FontFamily = new Avalonia.Media.FontFamily("Consolas, Menlo, monospace"),
                Text = KeyboardMap.HelpText,
            },
        };
        await dialog.ShowDialog(this);
    }
}
