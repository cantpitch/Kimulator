using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Kimulator.App.Audio;
using Kimulator.App.Board;
using Kimulator.App.Debugging;
using Kimulator.App.Dialogs;
using Kimulator.App.Editor;
using Kimulator.App.Expansion;
using Kimulator.App.Terminal;
using Kimulator.App.Video;
using Kimulator.Kim1;
using CardConfig = Kimulator.Kim1.Cards.CardConfig;
using CardType = Kimulator.Kim1.Cards.CardType;
using ExpansionConfig = Kimulator.Kim1.Cards.ExpansionConfig;

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
    private AssemblerWindow? _assembler;
    private CassetteWindow? _cassette;
    private ExpansionWindow? _expansion;
    private VisibleMemoryWindow? _visibleMemory;
    private long _lastStatusCycles;
    private DateTime _lastStatusTime = DateTime.UtcNow;
    private ushort _lastSaveStart = 0x0200, _lastSaveEnd = 0x03FF, _lastBinaryAddress = 0x0200;

    public MainWindow()
    {
        InitializeComponent();
        _session = new EmulatorSession(_settings);

        var layout = BoardLayout.Load(new Uri("avares://Kimulator/Assets/kim1-layout.json"));
        using var imageStream = AssetLoader.Open(new Uri("avares://Kimulator/Assets/kim1-board.jpg"));
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
        RefreshCardBar();
        int visibleMemories = _session.VisibleMemories.Count;
        _session.ExpansionChanged += () =>
        {
            RefreshCardBar();
            // A K-1008 was just installed: switch its monitor on.
            if (visibleMemories == 0 && _session.VisibleMemories.Count > 0) ShowVisibleMemory();
            visibleMemories = _session.VisibleMemories.Count;
        };

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
        if (_settings.AssemblerOpen || Environment.GetCommandLineArgs().Contains("--assembler"))
            Opened += (_, _) => ShowAssembler();
        if (_settings.CassetteOpen || Environment.GetCommandLineArgs().Contains("--cassette"))
            Opened += (_, _) => ShowCassette();
        if (Environment.GetCommandLineArgs().Contains("--expansion"))
            Opened += (_, _) => ShowExpansion();
        if ((_settings.VisibleMemoryOpen && _session.VisibleMemories.Count > 0) || Environment.GetCommandLineArgs().Contains("--visible-memory"))
            Opened += (_, _) => ShowVisibleMemory();
        Opened += (_, _) => DispatcherTimer.RunOnce(() =>
        {
            if (_session.ExpansionError is { } cardError) ShowStatus(cardError);
            else if (_session.Audio.Error is { } error) ShowStatus($"Sound is unavailable: {error}");
        }, TimeSpan.FromSeconds(1));
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _settings.TerminalOpen = _terminal is not null;
        _settings.AssemblerOpen = _assembler is not null;
        _settings.CassetteOpen = _cassette is not null;
        _settings.VisibleMemoryOpen = _visibleMemory is not null;
        _visibleMemory?.Close();
        _cassette?.Close();
        _expansion?.Close();
        // Close child windows first: the assembler stores unsaved text in the settings as it closes.
        _terminal?.Close();
        _debugger?.Close();
        _assembler?.Close();
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            _settings.WindowX = Position.X;
            _settings.WindowY = Position.Y;
        }

        _settings.Save();
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

        KeyClickMenu.IsChecked = _settings.KeyClick;
        CardBarMenu.IsChecked = _settings.ShowCardBar;
        foreach (var item in SoundMenu.Items.OfType<MenuItem>())
        {
            if (item.Tag is not string tag) continue;
            if (item.GroupName == "Sound") item.IsChecked = tag == _settings.SoundSource.ToString();
            else item.IsChecked = Math.Abs(double.Parse(tag, CultureInfo.InvariantCulture) - _settings.Volume) < 0.01;
        }

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
                case Key.E: ShowAssembler(); break;
                case Key.K: ShowCassette(); break;
                case Key.G: ShowVisibleMemory(); break;
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

    private void OnShowAssembler(object? sender, RoutedEventArgs e) => ShowAssembler();

    private void OnShowCassette(object? sender, RoutedEventArgs e) => ShowCassette();

    private void OnShowExpansion(object? sender, RoutedEventArgs e) => ShowExpansion();

    private void ShowExpansion(int? slot = null)
    {
        if (_expansion is null)
        {
            _expansion = new ExpansionWindow(_session, _settings, ShowVisibleMemory);
            _expansion.Closed += (_, _) => _expansion = null;
            _expansion.Show(this);
        }
        else
        {
            _expansion.Activate();
        }

        if (slot is { } s) _expansion.SelectSlot(s);
    }

    private void OnShowVisibleMemory(object? sender, RoutedEventArgs e) => ShowVisibleMemory();

    private void ShowVisibleMemory()
    {
        if (_visibleMemory is not null)
        {
            _visibleMemory.Activate();
            return;
        }

        _visibleMemory = new VisibleMemoryWindow(_session, _settings);
        _visibleMemory.Closed += (_, _) => _visibleMemory = null;
        _visibleMemory.Show(this);
    }

    // ---------------------------------------------------------------- installed cards

    private void OnToggleCardBar(object? sender, RoutedEventArgs e)
    {
        _settings.ShowCardBar = CardBarMenu.IsChecked;
        RefreshCardBar();
    }

    /// <summary>Rebuilds the strip under the board that shows which cards are installed; each opens its settings.</summary>
    private void RefreshCardBar()
    {
        CardBar.IsVisible = _settings.ShowCardBar;
        CardChips.Children.Clear();
        var config = _session.Expansion;

        if (config.Kim4)
            CardChips.Children.Add(CardChip(CardArt.Kim4, "KIM-4", "KIM-4 motherboard: six card slots and full address decoding.", () => ShowExpansion()));

        foreach (var (slot, card) in config.InstalledCards)
        {
            string name = CardConfig.DisplayName(card.Type).Split("  ")[0];
            string where = config.Kim4 ? $"KIM-4 slot {slot + 1}" : "KIM-1 expansion connector";
            string address = DescribeRanges(ExpansionConfig.RangesOf(card).ToList());
            string tip = $"{CardConfig.DisplayName(card.Type).Replace("  ", " ")}\n{where}\nAnswers at {address}";
            Action open = card.Type == CardType.K1008 ? ShowVisibleMemory : () => ShowExpansion(slot);
            if (card.Type == CardType.K1008) tip += "\nClick to show its display.";
            CardChips.Children.Add(CardChip(CardArt.For(card.Type), $"{name}  {address}", tip, open));
        }

        if (config.Problems() is { Count: > 0 } problems)
        {
            var chip = CardChip(null, problems.Count == 1 ? "⚠ 1 card problem" : $"⚠ {problems.Count} card problems",
                string.Join("\n\n", problems), () => ShowExpansion());
            chip.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF0, 0x8A, 0x5D));
            CardChips.Children.Add(chip);
        }

        if (CardChips.Children.Count == 0)
            CardChips.Children.Add(CardChip(null, "No expansion cards", "A bare KIM-1. Click to add KIM-2/3/4/5 or K-1008 cards.", () => ShowExpansion()));
    }

    private static string DescribeRanges(IReadOnlyList<Kimulator.Kim1.Cards.AddressRange> ranges)
    {
        if (ranges.Count == 0) return "no address";
        var merged = new List<Kimulator.Kim1.Cards.AddressRange>();
        foreach (var r in ranges.OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && merged[^1].End + 1 == r.Start) merged[^1] = new(merged[^1].Start, r.End);
            else merged.Add(r);
        }

        return string.Join(", ", merged);
    }

    private static Button CardChip(CardArt? art, string text, string tip, Action click)
    {
        var content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        if (art is not null)
            content.Children.Add(new Image { Source = art.Thumbnail, Height = 22, Stretch = Avalonia.Media.Stretch.Uniform });
        content.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });

        var chip = new Button
        {
            Content = content,
            Padding = new Thickness(4, 2, 8, 2),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x20, 0x20, 0x20)),
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xC8, 0xC8, 0xC8)),
            CornerRadius = new CornerRadius(4),
            Focusable = false, // keep keyboard focus on the board: the PC keys are the KIM keypad
        };
        ToolTip.SetTip(chip, tip);
        chip.Click += (_, _) => click();
        return chip;
    }

    private void ShowCassette()
    {
        if (_cassette is not null)
        {
            _cassette.Activate();
            return;
        }

        _cassette = new CassetteWindow(_session, _settings);
        _cassette.Closed += (_, _) => _cassette = null;
        _cassette.Show(this);
    }

    private void OnSoundSource(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !Enum.TryParse<SoundSource>(tag, out var source)) return;
        _settings.SoundSource = source;
        _session.SetSound(source, (float)_settings.Volume);
    }

    private void OnToggleKeyClick(object? sender, RoutedEventArgs e) =>
        _session.KeyClickEnabled = _settings.KeyClick = KeyClickMenu.IsChecked;

    private void OnVolume(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag }) return;
        _settings.Volume = double.Parse(tag, CultureInfo.InvariantCulture);
        _session.SetSound(_settings.SoundSource, (float)_settings.Volume);
    }

    private void ShowAssembler()
    {
        if (_assembler is not null)
        {
            _assembler.Activate();
            return;
        }

        _assembler = new AssemblerWindow(_session, _settings);
        _assembler.Closed += (_, _) => _assembler = null;
        _assembler.Show(this);
    }

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
