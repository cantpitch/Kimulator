using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Kimulator.App.Board;
using Kimulator.App.Debugging;
using Kimulator.App.Dialogs;
using Kimulator.App.Terminal;
using Kimulator.Kim1;

namespace Kimulator.App;

public partial class MainWindow : Window
{
    private static readonly int[] BaudRates = [110, 300, 600, 1200, 2400, 4800];
    private static readonly string QuickSavePath = Path.Combine(AppSettings.Directory, "quicksave.kimstate");

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly EmulatorSession _session;
    private readonly BoardView _board;
    private readonly DispatcherTimer _frameTimer;
    private readonly HashSet<Key> _heldKeys = [];
    private TerminalWindow? _terminal;
    private DebuggerWindow? _debugger;
    private long _lastStatusCycles;
    private DateTime _lastStatusTime = DateTime.UtcNow;
    private ushort _lastSaveStart = 0x0200, _lastSaveEnd = 0x03FF, _lastBinaryAddress = 0x0200;

    public MainWindow()
    {
        InitializeComponent();
        _session = new EmulatorSession(_settings);

        var layout = BoardLayout.Load(new Uri("avares://Kimulator.App/Assets/kim1-layout.json"));
        using var imageStream = AssetLoader.Open(new Uri("avares://Kimulator.App/Assets/kim1-board.jpg"));
        _board = new BoardView
        {
            Layout = layout,
            BoardImage = new Bitmap(imageStream),
            ViewRect = _settings.CompactView ? layout.CompactView.ToRect() : layout.FullView,
        };
        _board.KeyChanged += (key, down) => _session.SetKey(key, down);
        _board.SingleStepToggled += () => SetSingleStep(!_board.SingleStep);
        BoardHost.Content = _board;

        ApplySettingsToMenus();
        RestoreWindowPlacement();

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => ReleaseHeldKeys();

        _session.Runner.Faulted += ex => Dispatcher.UIThread.Post(() => ShowStatus($"Emulation stopped: {ex.Message}"));
        _session.Stopped += stop =>
        {
            ShowStatus($"Stopped — {stop.Message}. Debugger: F5 continues.");
            if (stop.Reason is not (Core.Debugging.StopReason.Paused or Core.Debugging.StopReason.Step)) ShowDebugger();
        };
        _session.Resumed += () => ShowStatus("");

        _frameTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, OnFrame);
        _frameTimer.Start();

        if (Environment.GetCommandLineArgs().Contains("--compact"))
            Opened += (_, _) => Dispatcher.UIThread.Post(() => SetView(compact: true), DispatcherPriority.Loaded);
        if (_settings.TerminalOpen || _settings.TtyMode)
            Opened += (_, _) => ShowTerminal();
        if (Environment.GetCommandLineArgs().Contains("--debugger"))
            Opened += (_, _) => ShowDebugger();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _settings.TerminalOpen = _terminal is not null;
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            _settings.WindowX = Position.X;
            _settings.WindowY = Position.Y;
        }

        _settings.Save();
        _terminal?.Close();
        _debugger?.Close();
        base.OnClosing(e);
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
        _session.PumpTerminal();

        var now = DateTime.UtcNow;
        double seconds = (now - _lastStatusTime).TotalSeconds;
        if (seconds < 0.5) return;
        long cycles = _session.Board.Cycles;
        double mhz = (cycles - _lastStatusCycles) / seconds / 1e6;
        _lastStatusCycles = cycles;
        _lastStatusTime = now;
        string flags = (_board.SingleStep ? "  ·  SST" : "") + (_session.TtyMode ? "  ·  TTY" : "");
        SpeedText.Text = $"{mhz:0.000} MHz{flags}";
    }

    private void ShowStatus(string message) => StatusText.Text = message;

    // ---------------------------------------------------------------- settings

    private void ApplySettingsToMenus()
    {
        FullViewMenu.IsChecked = !_settings.CompactView;
        CompactViewMenu.IsChecked = _settings.CompactView;
        PresetVectorsMenu.IsChecked = _settings.PresetInterruptVectors;
        AutoCalibrateMenu.IsChecked = _settings.AutoCalibrateTty;
        TtyModeMenu.IsChecked = _settings.TtyMode;

        foreach (var item in SpeedMenu.Items.OfType<MenuItem>())
        {
            double speed = double.Parse((string)item.Tag!, CultureInfo.InvariantCulture);
            item.IsChecked = speed == _settings.Speed;
        }

        ApplySpeed(_settings.Speed);

        foreach (int baud in BaudRates)
        {
            var item = new MenuItem { Header = $"{baud} baud", ToggleType = MenuItemToggleType.Radio, GroupName = "Baud", Tag = baud, IsChecked = baud == _settings.BaudRate };
            item.Click += OnBaud;
            BaudMenu.Items.Add(item);
        }

        // The terminal window can change the rate too; reflect it whenever the menu opens.
        BaudMenu.SubmenuOpened += (_, _) =>
        {
            foreach (var item in BaudMenu.Items.OfType<MenuItem>())
                item.IsChecked = (int)item.Tag! == _settings.BaudRate;
        };
    }

    private void RestoreWindowPlacement()
    {
        if (_settings.WindowWidth is > 200 and < 10000 && _settings.WindowHeight is > 200 and < 10000)
        {
            Width = _settings.WindowWidth.Value;
            Height = _settings.WindowHeight.Value;
        }

        if (_settings.WindowX is { } x && _settings.WindowY is { } y)
        {
            var point = new PixelPoint(x, y);
            if (Screens.All.Any(s => s.WorkingArea.Contains(point)))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = point;
            }
        }
    }

    // ---------------------------------------------------------------- keyboard

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            switch (e.Key)
            {
                case Key.D1: SetView(compact: false); break;
                case Key.D2: SetView(compact: true); break;
                case Key.O: OnLoadProgram(null, e); break;
                case Key.M: OnSaveMemory(null, e); break;
                case Key.S: OnSaveState(null, e); break;
                case Key.L: OnLoadState(null, e); break;
                case Key.T: ShowTerminal(); break;
                case Key.D: ShowDebugger(); break;
                default: e.Handled = false; break;
            }

            return;
        }

        if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return;

        switch (e.Key)
        {
            case Key.F9: SetSingleStep(!_board.SingleStep); e.Handled = true; return;
            case Key.F6: OnQuickSave(null, e); e.Handled = true; return;
            case Key.F7: OnQuickLoad(null, e); e.Handled = true; return;
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

    // ---------------------------------------------------------------- File menu

    private async void OnLoadProgram(object? sender, RoutedEventArgs e)
    {
        var file = await FileDialogs.OpenAsync(this, _settings, "Load program into memory", FileDialogs.ProgramTypes);
        if (file is null) return;
        var (name, content) = file.Value;

        try
        {
            var format = ProgramFiles.Detect(name, content);
            IReadOnlyList<Kimulator.Core.Formats.MemorySegment> segments;
            if (format == ProgramFiles.Format.Binary)
            {
                var range = await new AddressRangeDialog("Load binary", $"Load {name} ({content.Length} bytes) at:", _lastBinaryAddress, null)
                    .ShowDialog<AddressRange?>(this);
                if (range is null) return;
                _lastBinaryAddress = range.Start;
                segments = [new(range.Start, content)];
            }
            else
            {
                segments = ProgramFiles.ParseRecords(format, content);
            }

            if (segments.Count == 0)
            {
                ShowStatus($"{name} contains no data.");
                return;
            }

            int skipped = await _session.LoadProgramAsync(segments);
            int total = segments.Sum(s => s.Data.Length);
            string ranges = string.Join(", ", segments.Select(s => $"${s.Address:X4}–${s.End - 1:X4}"));
            ShowStatus($"Loaded {name}: {total} bytes at {ranges}. Press GO to run from ${segments[0].Address:X4}.");
            if (skipped > 0)
                await MessageDialog.ShowAsync(this, "Some bytes were not loaded",
                    $"{skipped} of {total} bytes fall outside writable memory (RAM is $0000–$03FF and $1780–$17FF on a stock KIM-1) and were skipped.");
        }
        catch (FormatException ex)
        {
            await MessageDialog.ShowAsync(this, "Could not read program", ex.Message);
        }
    }

    private async void OnSaveMemory(object? sender, RoutedEventArgs e)
    {
        var range = await new AddressRangeDialog("Save memory", "Memory range to save:", _lastSaveStart, _lastSaveEnd)
            .ShowDialog<AddressRange?>(this);
        if (range?.End is not { } end) return;
        _lastSaveStart = range.Start;
        _lastSaveEnd = end;

        byte[] data = await _session.ReadMemoryAsync(range.Start, end - range.Start + 1);
        string? saved = await FileDialogs.SaveAsync(this, _settings, "Save memory", $"kim-{range.Start:X4}.ptp",
            [FileDialogs.PaperTape, FileDialogs.IntelHex, FileDialogs.Binary],
            fileName => ProgramFiles.Serialize(ProgramFiles.Detect(fileName, []), range.Start, data));
        if (saved is not null) ShowStatus($"Saved ${range.Start:X4}–${end:X4} to {saved}.");
    }

    private async void OnSaveState(object? sender, RoutedEventArgs e)
    {
        byte[] state = await _session.SaveStateAsync();
        string? saved = await FileDialogs.SaveAsync(this, _settings, "Save state", "kim1.kimstate", FileDialogs.SaveStateTypes, state);
        if (saved is not null) ShowStatus($"State saved to {saved}.");
    }

    private async void OnLoadState(object? sender, RoutedEventArgs e)
    {
        var file = await FileDialogs.OpenAsync(this, _settings, "Load state", FileDialogs.SaveStateTypes);
        if (file is null) return;
        await RestoreStateAsync(file.Value.Content, file.Value.Name);
    }

    private async void OnQuickSave(object? sender, RoutedEventArgs e)
    {
        byte[] state = await _session.SaveStateAsync();
        Directory.CreateDirectory(AppSettings.Directory);
        await File.WriteAllBytesAsync(QuickSavePath, state);
        ShowStatus("Quick-saved (F7 to restore).");
    }

    private async void OnQuickLoad(object? sender, RoutedEventArgs e)
    {
        if (!File.Exists(QuickSavePath))
        {
            ShowStatus("No quick save yet (F6 saves one).");
            return;
        }

        await RestoreStateAsync(await File.ReadAllBytesAsync(QuickSavePath), "quick save");
    }

    private async Task RestoreStateAsync(byte[] state, string name)
    {
        try
        {
            var (singleStep, tty) = await _session.LoadStateAsync(state);
            _board.SingleStep = singleStep;
            SingleStepMenu.IsChecked = singleStep;
            TtyModeMenu.IsChecked = tty;
            _settings.TtyMode = tty;
            _terminal?.OnTtyModeChanged();
            ShowStatus($"Restored {name}.");
        }
        catch (InvalidDataException ex)
        {
            await MessageDialog.ShowAsync(this, "Could not load state", ex.Message);
        }
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------- Machine menu

    private void SetSingleStep(bool on)
    {
        _board.SingleStep = on;
        SingleStepMenu.IsChecked = on;
        _session.SetSingleStep(on);
    }

    private void OnReset(object? sender, RoutedEventArgs e) => _session.TapKey(Kim1Key.Reset);

    private void OnStop(object? sender, RoutedEventArgs e) => _session.TapKey(Kim1Key.Stop);

    private void OnToggleSingleStep(object? sender, RoutedEventArgs e) => SetSingleStep(SingleStepMenu.IsChecked);

    private void OnToggleTtyMode(object? sender, RoutedEventArgs e) => _ = SetTtyModeAsync(TtyModeMenu.IsChecked);

    private Task SetTtyModeAsync(bool on)
    {
        TtyModeMenu.IsChecked = on;
        _settings.TtyMode = on;
        _session.SetTtyMode(on);
        if (on) ShowTerminal();
        _terminal?.OnTtyModeChanged();
        ShowStatus(on ? "TTY mode: the monitor now talks to the terminal window." : "Keyboard mode.");
        return Task.CompletedTask;
    }

    private void OnBaud(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: int baud }) return;
        _settings.BaudRate = baud;
        _session.SetBaudRate(baud);
    }

    private void OnPowerCycle(object? sender, RoutedEventArgs e) => _session.PowerCycle();

    private void OnTogglePresetVectors(object? sender, RoutedEventArgs e) =>
        _session.PresetInterruptVectors = _settings.PresetInterruptVectors = PresetVectorsMenu.IsChecked;

    private void OnToggleAutoCalibrate(object? sender, RoutedEventArgs e) =>
        _session.AutoCalibrateTty = _settings.AutoCalibrateTty = AutoCalibrateMenu.IsChecked;

    private void OnSpeed(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag }) return;
        _settings.Speed = double.Parse(tag, CultureInfo.InvariantCulture);
        ApplySpeed(_settings.Speed);
    }

    private void ApplySpeed(double speed)
    {
        _session.Runner.Unthrottled = speed == 0;
        if (speed > 0) _session.Runner.Speed = speed;
    }

    // ---------------------------------------------------------------- View menu

    private void OnFullView(object? sender, RoutedEventArgs e) => SetView(compact: false);

    private void OnCompactView(object? sender, RoutedEventArgs e) => SetView(compact: true);

    private void SetView(bool compact)
    {
        _settings.CompactView = compact;
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

    private void OnShowTerminal(object? sender, RoutedEventArgs e) => ShowTerminal();

    private void ShowTerminal()
    {
        if (_terminal is not null)
        {
            _terminal.Activate();
            return;
        }

        _terminal = new TerminalWindow(_session, _settings, SetTtyModeAsync);
        _terminal.Closed += (_, _) => _terminal = null;
        _terminal.Show(this);
    }

    private void OnShowDebugger(object? sender, RoutedEventArgs e) => ShowDebugger();

    private void ShowDebugger()
    {
        if (_debugger is not null)
        {
            _debugger.Activate();
            return;
        }

        _debugger = new DebuggerWindow(_session);
        _debugger.Closed += (_, _) => _debugger = null;
        _debugger.Show(this);
    }

    private async void OnShowKeys(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Keyboard shortcuts",
            Width = 400,
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
