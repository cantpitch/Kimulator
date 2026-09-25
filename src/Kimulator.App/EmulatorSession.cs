using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Threading;
using Kimulator.App.Debugging;
using Kimulator.Core.Debugging;
using Kimulator.Core.Formats;
using Kimulator.Core.Machine;
using Kimulator.Core.Terminal;
using Kimulator.Kim1;

namespace Kimulator.App;

/// <summary>
/// Owns the emulated KIM-1 and the thread that runs it. All board access from the UI goes through
/// <see cref="MachineRunner.Post"/> / <see cref="MachineRunner.InvokeAsync{T}"/> so the board is only
/// ever touched by the emulation thread.
/// </summary>
public sealed class EmulatorSession : IDisposable
{
    /// <summary>Keys are held at least this long so a quick click still survives the monitor's debounce.</summary>
    private static readonly TimeSpan MinimumKeyHold = TimeSpan.FromMilliseconds(60);

    /// <summary>Delay after reset before the calibration RUBOUT, in cycles (20 ms).</summary>
    private const long CalibrationDelayCycles = 20_000;

    private readonly Dictionary<Kim1Key, long> _pressedAt = [];
    private readonly ConcurrentQueue<byte> _ttyOutput = new();

    public EmulatorSession(AppSettings settings)
    {
        Board = Kim1Board.CreateWithDefaultRoms();
        Board.TtyMode = TtyMode = settings.TtyMode;
        Board.Tty.BaudRate = settings.BaudRate;
        Board.Tty.CharacterReceived += _ttyOutput.Enqueue;
        PresetInterruptVectors = settings.PresetInterruptVectors;
        AutoCalibrateTty = settings.AutoCalibrateTty;
        Runner = new MachineRunner(Board);
        Runner.Stopped += stop => Dispatcher.UIThread.Post(() =>
        {
            LastStop = stop;
            Stopped?.Invoke(stop);
        });
        Runner.Resumed += () => Dispatcher.UIThread.Post(() =>
        {
            LastStop = null;
            Resumed?.Invoke();
        });
        PowerCycle();
        Runner.Start();
    }

    public Kim1Board Board { get; }

    public MachineRunner Runner { get; }

    /// <summary>Everything the KIM has printed on the teletype. UI thread only.</summary>
    public TeletypeBuffer Transcript { get; } = new();

    /// <summary>
    /// Convenience (not on a real KIM-1): point the NMI and IRQ/BRK vectors at $17FA/$17FE to the
    /// monitor's SAVE routine at power-on so ST, SST and BRK work without entering them by hand.
    /// </summary>
    public bool PresetInterruptVectors { get; set; }

    /// <summary>Send a RUBOUT after every reset in TTY mode so the monitor measures the baud rate.</summary>
    public bool AutoCalibrateTty { get; set; }

    /// <summary>The TTY/KB jumper. UI-side copy; the board's value changes on the emulation thread.</summary>
    public bool TtyMode { get; private set; }

    public DisplayFrame Display => Board.Display.Latest;

    /// <summary>Raised on the UI thread after <see cref="PumpTerminal"/> added text to <see cref="Transcript"/>.</summary>
    public event Action? TranscriptChanged;

    /// <summary>Raised on the UI thread when execution stops (breakpoint, step, pause).</summary>
    public event Action<StopInfo>? Stopped;

    /// <summary>Raised on the UI thread when execution continues.</summary>
    public event Action? Resumed;

    /// <summary>Why execution is stopped, or null while running. UI thread.</summary>
    public StopInfo? LastStop { get; private set; }

    public bool IsStopped => LastStop is not null;

    /// <summary>Labels shown by the debugger: the loaded program's (if any) plus the monitor's.</summary>
    public SymbolTable Symbols { get; private set; } = Kim1Board.MonitorSymbols;

    /// <summary>Raised on the UI thread when <see cref="Symbols"/> changes.</summary>
    public event Action? SymbolsChanged;

    /// <summary>Makes a program's labels visible in the debugger (program labels win over monitor labels).</summary>
    public void SetProgramSymbols(SymbolTable? program)
    {
        var combined = new SymbolTable();
        if (program is not null) combined.Merge(program);
        combined.Merge(Kim1Board.MonitorSymbols);
        Symbols = combined;
        SymbolsChanged?.Invoke();
    }

    /// <summary>Starts executing at <paramref name="address"/> (resuming if stopped).</summary>
    public async Task RunFromAsync(ushort address)
    {
        await Runner.InvokeAsync(() =>
        {
            Board.Cpu.PC = address;
            return true;
        });
        if (IsStopped) Runner.Resume();
    }

    // ---------------------------------------------------------------- debugging

    public Task<DebugSnapshot> CaptureAsync() => Runner.InvokeAsync(() =>
    {
        var cpu = Board.Cpu;
        var memory = new byte[0x10000];
        for (int a = 0; a < memory.Length; a++) memory[a] = Board.Peek((ushort)a);
        var dbg = Board.Debugger;
        return new DebugSnapshot(cpu.A, cpu.X, cpu.Y, cpu.S, cpu.P, cpu.PC, Board.Cycles, cpu.Jammed, cpu.InterruptPending,
            memory, [.. dbg.Breakpoints], dbg.BreakOnBrk, dbg.BreakOnJam, dbg.BusTraceEnabled);
    });

    /// <summary>Runs <paramref name="action"/> against the debugger on the emulation thread.</summary>
    public Task WithDebuggerAsync(Action<Core.Debugging.Debugger> action) =>
        Runner.InvokeAsync(() => { action(Board.Debugger); return true; });

    public Task<T> WithDebuggerAsync<T>(Func<Core.Debugging.Debugger, T> func) => Runner.InvokeAsync(() => func(Board.Debugger));

    public Task SetRegistersAsync(byte a, byte x, byte y, byte s, byte p, ushort pc) => Runner.InvokeAsync(() =>
    {
        var cpu = Board.Cpu;
        cpu.A = a;
        cpu.X = x;
        cpu.Y = y;
        cpu.S = s;
        cpu.P = p;
        cpu.PC = pc;
        return true;
    });

    public Task PokeAsync(ushort address, byte value) => Runner.InvokeAsync(() =>
    {
        Board.Poke(address, value);
        return true;
    });

    public void PowerCycle() => Runner.Post(() =>
    {
        Board.PowerOn();
        if (PresetInterruptVectors)
        {
            Board.Poke(0x17FA, 0x00);
            Board.Poke(0x17FB, 0x1C);
            Board.Poke(0x17FE, 0x00);
            Board.Poke(0x17FF, 0x1C);
        }

        AfterReset();
    });

    public void SetKey(Kim1Key key, bool down)
    {
        if (down)
        {
            _pressedAt[key] = Stopwatch.GetTimestamp();
            Runner.Post(() => Board.SetKey(key, true));
            return;
        }

        var held = _pressedAt.Remove(key, out long start) ? Stopwatch.GetElapsedTime(start) : MinimumKeyHold;
        if (held >= MinimumKeyHold)
            Release();
        else
            DispatcherTimer.RunOnce(Release, MinimumKeyHold - held);

        void Release() => Runner.Post(() =>
        {
            Board.SetKey(key, false);
            if (key == Kim1Key.Reset) AfterReset();
        });
    }

    /// <summary>Taps a key (press + release with the minimum hold).</summary>
    public void TapKey(Kim1Key key)
    {
        SetKey(key, true);
        SetKey(key, false);
    }

    public void SetSingleStep(bool on) => Runner.Post(() => Board.SingleStep = on);

    /// <summary>Moves the TTY/KB jumper and resets, since the monitor only checks it on (re)start.</summary>
    public void SetTtyMode(bool on)
    {
        TtyMode = on;
        Runner.Post(() => Board.TtyMode = on);
        TapKey(Kim1Key.Reset);
    }

    /// <summary>Changes the line speed; in TTY mode the KIM is reset so the monitor re-measures it.</summary>
    public void SetBaudRate(int baud)
    {
        Runner.Post(() => Board.Tty.BaudRate = baud);
        if (TtyMode) TapKey(Kim1Key.Reset);
    }

    public void SendToTty(byte[] data) => Runner.Post(() => Board.Tty.Send(data));

    public void ClearTtyInput() => Runner.Post(() => Board.Tty.ClearInput());

    public Task<int> PendingTtyInputAsync() => Runner.InvokeAsync(() => Board.Tty.PendingInput);

    /// <summary>Moves characters the KIM printed into <see cref="Transcript"/>. Call on the UI thread.</summary>
    public void PumpTerminal()
    {
        bool any = false;
        while (_ttyOutput.TryDequeue(out byte b))
        {
            Transcript.Write(b);
            any = true;
        }

        if (any) TranscriptChanged?.Invoke();
    }

    /// <summary>
    /// Stores segments into memory and makes <paramref name="entry"/> (default: the first segment) the
    /// monitor's open cell. Returns bytes that could not be stored.
    /// </summary>
    public Task<int> LoadProgramAsync(IReadOnlyList<MemorySegment> segments, ushort? entry = null) => Runner.InvokeAsync(() =>
    {
        int skipped = segments.Sum(s => Board.LoadMemory(s.Address, s.Data));
        if ((entry ?? segments.FirstOrDefault()?.Address) is { } open) Board.SetOpenCell(open);
        return skipped;
    });

    public Task<byte[]> ReadMemoryAsync(ushort start, int length) => Runner.InvokeAsync(() =>
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++) data[i] = Board.Peek((ushort)(start + i));
        return data;
    });

    public Task<byte[]> SaveStateAsync() => Runner.InvokeAsync(() =>
    {
        using var stream = new MemoryStream();
        Board.SaveState(stream);
        return stream.ToArray();
    });

    /// <summary>Restores a state; returns the switch positions it contained so the UI can follow.</summary>
    public async Task<(bool SingleStep, bool TtyMode)> LoadStateAsync(byte[] state)
    {
        var result = await Runner.InvokeAsync(() =>
        {
            Board.LoadState(new MemoryStream(state));
            return (Board.SingleStep, Board.TtyMode);
        });
        TtyMode = result.TtyMode;
        return result;
    }

    public void Dispose() => Runner.Dispose();

    /// <summary>Runs on the emulation thread after RS or power-on.</summary>
    private void AfterReset()
    {
        if (Board.TtyMode && AutoCalibrateTty)
            Board.Tty.SendCalibrationRubout(CalibrationDelayCycles);
    }
}
