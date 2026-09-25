using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kimulator.Core.Cpu;
using Kimulator.Core.Debugging;
using Kimulator.Kim1;

namespace Kimulator.App.Debugging;

/// <summary>Row in the breakpoint list.</summary>
public sealed record BreakpointItem(int Id, string Description, bool Enabled);

/// <summary>
/// Debugger for the emulated KIM-1: disassembly with breakpoints, registers, stack, memory editor,
/// watchpoints and bus/instruction traces. Execution control goes through <see cref="Core.Machine.MachineRunner"/>.
/// </summary>
public partial class DebuggerWindow : Window
{
    private readonly EmulatorSession _session;
    private readonly SymbolTable _symbols = Kim1Board.MonitorSymbols;
    private readonly DispatcherTimer? _liveTimer;
    private DebugSnapshot? _snapshot;
    private bool _refreshing;
    private bool _updatingUi;

    public DebuggerWindow() : this(null!) { } // designer

    public DebuggerWindow(EmulatorSession session)
    {
        InitializeComponent();
        _session = session;
        Disassembly.Symbols = _symbols;
        Disassembly.BreakpointToggleRequested += address => _ = ToggleBreakpointAsync(address);
        Memory.ByteEdited += async (address, value) =>
        {
            await _session.PokeAsync(address, value);
            await RefreshAsync(follow: false, markChanges: false);
        };
        Memory.TopRowChanged += row => { if (!_updatingUi) MemoryScroll.Value = row; };
        MemoryScroll.Maximum = HexView.RowCount;
        MemoryScroll.Scroll += (_, e) => Memory.TopRow = (int)e.NewValue;

        foreach (var box in new[] { RegA, RegX, RegY, RegS, RegPC })
        {
            box.KeyDown += async (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                await ApplyRegistersAsync();
            };
        }

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        if (session is null) return;
        session.Stopped += OnStopped;
        session.Resumed += OnResumed;

        // While running, keep registers and memory roughly live.
        _liveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, async (_, _) =>
        {
            if (!_session.IsStopped && IsVisible) await RefreshAsync(follow: false, markChanges: false);
        });
        _liveTimer.Start();

        Opened += async (_, _) =>
        {
            UpdateRunState();
            await RefreshAsync(follow: true, markChanges: false);
            Memory.GoTo(0x0200);
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _liveTimer?.Stop();
        if (_session is not null)
        {
            _session.Stopped -= OnStopped;
            _session.Resumed -= OnResumed;
        }

        base.OnClosed(e);
    }

    private async void OnStopped(StopInfo stop)
    {
        UpdateRunState();
        await RefreshAsync(follow: true, markChanges: true);
        if (stop.Reason is StopReason.Watchpoint or StopReason.Breakpoint or StopReason.Brk or StopReason.Jam) Activate();
    }

    private void OnResumed() => UpdateRunState();

    // ---------------------------------------------------------------- refresh

    private async Task RefreshAsync(bool follow, bool markChanges)
    {
        if (_refreshing || _session is null) return;
        _refreshing = true;
        try
        {
            var snapshot = await _session.CaptureAsync();
            _snapshot = snapshot;
            _updatingUi = true;

            Disassembly.Update(snapshot);
            if (follow && _session.IsStopped) Disassembly.EnsureVisible(snapshot.PC);

            Memory.Update(snapshot.Memory, markChanges);
            MemoryScroll.ViewportSize = Memory.VisibleRows;
            MemoryScroll.Value = Memory.TopRow;

            ShowRegisters(snapshot);
            ShowBreakpoints(snapshot);
        }
        finally
        {
            _updatingUi = false;
            _refreshing = false;
        }
    }

    private void ShowRegisters(DebugSnapshot s)
    {
        bool editing = _session.IsStopped;
        foreach (var (box, value) in new[] { (RegA, s.A.ToString("X2")), (RegX, s.X.ToString("X2")), (RegY, s.Y.ToString("X2")), (RegS, s.S.ToString("X2")), (RegPC, s.PC.ToString("X4")) })
        {
            box.IsReadOnly = !editing;
            if (!box.IsFocused || !editing) box.Text = value;
        }

        RegP.Text = $"{s.P:X2}  {FlagString(s.P)}";
        foreach (var flag in new[] { FlagN, FlagV, FlagD, FlagI, FlagZ, FlagC })
        {
            flag.IsChecked = (s.P & int.Parse((string)flag.Tag!, CultureInfo.InvariantCulture)) != 0;
            flag.IsEnabled = editing;
        }

        CyclesText.Text = $"Cycles: {s.Cycles:N0}" + (s.Jammed ? "   (CPU jammed)" : s.InterruptPending ? "   (interrupt pending)" : "");
        RegisterHint.IsVisible = !editing;

        var stack = new StringBuilder();
        for (int a = s.S + 1, n = 0; a <= 0xFF && n < 16; a++, n++)
            stack.AppendLine($"01{a:X2}  {s.Memory[0x100 + a]:X2}");
        StackText.Text = stack.Length == 0 ? "(empty)" : stack.ToString().TrimEnd();
    }

    private static string FlagString(byte p)
    {
        const string names = "NV-BDIZC";
        var sb = new StringBuilder(8);
        for (int bit = 7; bit >= 0; bit--) sb.Append((p & (1 << bit)) != 0 ? names[7 - bit] : '.');
        return sb.ToString();
    }

    private void ShowBreakpoints(DebugSnapshot s)
    {
        BreakpointList.ItemsSource = s.Breakpoints.Select(b => new BreakpointItem(b.Id, Describe(b), b.Enabled)).ToList();
        BreakOnBrkBox.IsChecked = s.BreakOnBrk;
        BreakOnJamBox.IsChecked = s.BreakOnJam;
        BusTraceBox.IsChecked = s.BusTraceEnabled;
    }

    private string Describe(Breakpoint b)
    {
        string range = b.Start == b.End ? $"${b.Start:X4}" : $"${b.Start:X4}-${b.End:X4}";
        string label = _symbols.NameOf(b.Start) is { } name ? $" ({name})" : "";
        string kind = b.Kind switch
        {
            BreakpointKind.Execute => "Execute   ",
            BreakpointKind.Read => "Read      ",
            BreakpointKind.Write => "Write     ",
            _ => "Read/write",
        };
        return $"{kind} {range}{label}";
    }

    private void UpdateRunState()
    {
        bool stopped = _session.IsStopped;
        RunPauseButton.Content = stopped ? "▶ Continue" : "⏸ Pause";
        StepButton.IsEnabled = StepOverButton.IsEnabled = StepOutButton.IsEnabled = stopped;
        StatusText.Text = stopped ? $"Stopped — {_session.LastStop!.Message}" : "Running";
    }

    // ---------------------------------------------------------------- execution control

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key, e.KeyModifiers)
        {
            case (Key.F5, KeyModifiers.None): OnRunPause(null, e); break;
            case (Key.F11, KeyModifiers.None): OnStep(null, e); break;
            case (Key.F10, KeyModifiers.None): OnStepOver(null, e); break;
            case (Key.F11, KeyModifiers.Shift): OnStepOut(null, e); break;
            case (Key.F10, KeyModifiers.Control): OnRunToCursor(null, e); break;
            case (Key.F9, KeyModifiers.None): OnToggleBreakpoint(null, e); break;
            default: return;
        }

        e.Handled = true;
    }

    private void OnRunPause(object? sender, RoutedEventArgs e)
    {
        if (_session.IsStopped) _session.Runner.Resume();
        else _session.Runner.Pause();
    }

    private void OnStep(object? sender, RoutedEventArgs e)
    {
        if (_session.IsStopped) _session.Runner.StepInstruction();
    }

    private void OnStepOver(object? sender, RoutedEventArgs e)
    {
        if (_session.IsStopped) _session.Runner.StepOver();
    }

    private void OnStepOut(object? sender, RoutedEventArgs e)
    {
        if (_session.IsStopped) _session.Runner.StepOut();
    }

    private void OnRunToCursor(object? sender, RoutedEventArgs e)
    {
        if (Disassembly.SelectedAddress is { } address) _session.Runner.RunTo(address);
        else StatusText.Text = "Select a line in the disassembly first.";
    }

    private void OnToggleBreakpoint(object? sender, RoutedEventArgs e)
    {
        if (Disassembly.SelectedAddress is { } address) _ = ToggleBreakpointAsync(address);
        else StatusText.Text = "Select a line in the disassembly first (or click its gutter).";
    }

    private async Task ToggleBreakpointAsync(ushort address)
    {
        await _session.WithDebuggerAsync(d => d.ToggleExecute(address));
        await RefreshAsync(follow: false, markChanges: false);
    }

    private void OnFollowPc(object? sender, RoutedEventArgs e)
    {
        if (_snapshot is null) return;
        Disassembly.ScrollTo(_snapshot.PC);
    }

    private void OnGotoKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (ParseAddress(GotoBox.Text) is { } address)
        {
            Disassembly.ScrollTo(address, linesAbove: 0);
            Disassembly.Select(address);
            Disassembly.Focus();
        }
    }

    private void OnMemoryGotoKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (ParseAddress(MemoryGotoBox.Text) is { } address)
        {
            Memory.GoTo(address);
            Memory.Focus();
        }
    }

    // ---------------------------------------------------------------- registers

    private async Task ApplyRegistersAsync()
    {
        if (_snapshot is null || !_session.IsStopped) return;
        byte a = ParseByte(RegA.Text, _snapshot.A), x = ParseByte(RegX.Text, _snapshot.X),
             y = ParseByte(RegY.Text, _snapshot.Y), s = ParseByte(RegS.Text, _snapshot.S);
        ushort pc = ushort.TryParse(RegPC.Text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var p) ? p : _snapshot.PC;
        await _session.SetRegistersAsync(a, x, y, s, _snapshot.P, pc);
        await RefreshAsync(follow: true, markChanges: false);
    }

    private async void OnFlagClicked(object? sender, RoutedEventArgs e)
    {
        if (_snapshot is null || !_session.IsStopped || sender is not CheckBox { Tag: string tag } box) return;
        int bit = int.Parse(tag, CultureInfo.InvariantCulture);
        byte flags = box.IsChecked == true ? (byte)(_snapshot.P | bit) : (byte)(_snapshot.P & ~bit);
        await _session.SetRegistersAsync(_snapshot.A, _snapshot.X, _snapshot.Y, _snapshot.S, flags, _snapshot.PC);
        await RefreshAsync(follow: false, markChanges: false);
    }

    private static byte ParseByte(string? text, byte fallback) =>
        byte.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    // ---------------------------------------------------------------- breakpoints

    private void OnBreakpointAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OnAddBreakpoint(sender, e);
    }

    private async void OnAddBreakpoint(object? sender, RoutedEventArgs e)
    {
        var text = BreakpointAddressBox.Text?.Trim() ?? "";
        var parts = text.Split('-', 2);
        ushort? start = ParseAddress(parts[0]);
        ushort? end = parts.Length == 2 ? ParseAddress(parts[1]) : start;
        if (start is null || end is null)
        {
            BreakpointError.Text = "Enter an address (hex), a range like 0200-02FF, or a monitor label such as SCAND.";
            BreakpointError.IsVisible = true;
            return;
        }

        BreakpointError.IsVisible = false;
        var kind = BreakpointKindBox.SelectedIndex switch
        {
            1 => BreakpointKind.Read,
            2 => BreakpointKind.Write,
            3 => BreakpointKind.Read | BreakpointKind.Write,
            _ => BreakpointKind.Execute,
        };
        await _session.WithDebuggerAsync(d => d.Add(start.Value, end.Value, kind));
        BreakpointAddressBox.Text = "";
        await RefreshAsync(follow: false, markChanges: false);
    }

    private async void OnRemoveBreakpoint(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int id }) return;
        await _session.WithDebuggerAsync(d => d.Remove(id));
        await RefreshAsync(follow: false, markChanges: false);
    }

    private async void OnBreakpointEnabledClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: int id } box) return;
        bool enabled = box.IsChecked == true;
        await _session.WithDebuggerAsync(d => d.SetEnabled(id, enabled));
        await RefreshAsync(follow: false, markChanges: false);
    }

    private async void OnBreakOptionClicked(object? sender, RoutedEventArgs e)
    {
        bool brk = BreakOnBrkBox.IsChecked == true, jam = BreakOnJamBox.IsChecked == true;
        await _session.WithDebuggerAsync(d =>
        {
            d.BreakOnBrk = brk;
            d.BreakOnJam = jam;
        });
    }

    // ---------------------------------------------------------------- trace

    private async void OnBusTraceClicked(object? sender, RoutedEventArgs e)
    {
        bool on = BusTraceBox.IsChecked == true;
        await _session.WithDebuggerAsync(d =>
        {
            d.BusTraceEnabled = on;
            if (on) d.ClearBusTrace();
        });
    }

    private async void OnRefreshTrace(object? sender, RoutedEventArgs e)
    {
        var (bus, history) = await _session.WithDebuggerAsync(d => (d.GetBusTrace(), d.GetHistory()));
        var snapshot = await _session.CaptureAsync();

        var sb = new StringBuilder("   cycle  op  addr  data\n");
        foreach (var c in bus.TakeLast(1024))
        {
            char kind = c.Kind switch { BusCycleKind.Fetch => 'F', BusCycleKind.Write => 'W', _ => 'R' };
            string note = c.Kind == BusCycleKind.Fetch ? "  " + Opcodes.Get(c.Data).Mnemonic : "";
            sb.Append(CultureInfo.InvariantCulture, $"{c.Cycle,10}  {kind}   {c.Address:X4}  {c.Data:X2}{note}\n");
        }

        BusTraceText.Text = bus.Length == 0 ? "No bus cycles recorded. Tick \"Record bus cycles\" and run." : sb.ToString();
        BusTraceText.CaretIndex = BusTraceText.Text.Length;

        var hs = new StringBuilder("Recent instructions (oldest first)\n");
        foreach (ushort pc in history)
        {
            var instruction = Disassembler.Disassemble(snapshot.Read, pc, _symbols);
            string label = _symbols.NameOf(pc) is { } name ? name : "";
            hs.Append(CultureInfo.InvariantCulture, $"{pc:X4}  {label,-7} {instruction.Text}\n");
        }

        HistoryText.Text = hs.ToString();
        HistoryText.CaretIndex = HistoryText.Text.Length;
    }

    // ---------------------------------------------------------------- helpers

    private ushort? ParseAddress(string? text)
    {
        text = text?.Trim().TrimStart('$');
        if (string.IsNullOrEmpty(text)) return null;
        if (_symbols.TryGetAddress(text, out ushort symbol)) return symbol;
        return ushort.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort value) ? value : null;
    }
}
