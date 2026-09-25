using Kimulator.Core.Cpu;

namespace Kimulator.Core.Debugging;

[Flags]
public enum BreakpointKind
{
    Execute = 1,
    Read = 2,
    Write = 4,
}

/// <summary>A breakpoint (Execute) or watchpoint (Read/Write) over an inclusive address range.</summary>
public sealed record Breakpoint(int Id, ushort Start, ushort End, BreakpointKind Kind, bool Enabled)
{
    public override string ToString()
    {
        string range = Start == End ? $"${Start:X4}" : $"${Start:X4}-${End:X4}";
        return $"{Kind} {range}";
    }
}

public enum StopReason
{
    Paused,
    Breakpoint,
    Watchpoint,
    Step,
    StepOver,
    StepOut,
    RunTo,
    Brk,
    Jam,
}

public sealed record StopInfo(StopReason Reason, ushort Pc, string Message);

public enum BusCycleKind : byte { Read, Fetch, Write }

public readonly record struct BusCycle(long Cycle, ushort Address, byte Data, BusCycleKind Kind);

/// <summary>
/// Breakpoints, watchpoints, stepping and tracing for a 6502 machine. The machine calls the hooks
/// (<see cref="ShouldStopBefore"/>, <see cref="AfterInstruction"/>, <see cref="OnRead"/>,
/// <see cref="OnWrite"/>) from its emulation loop; everything here runs on the emulation thread.
/// </summary>
public sealed class Debugger
{
    private const byte FlagExecute = 1, FlagRead = 2, FlagWrite = 4;

    private readonly Cpu6502 _cpu;
    private readonly Func<ushort, byte> _peek;
    private readonly byte[] _flags = new byte[0x10000];
    private readonly List<Breakpoint> _breakpoints = [];
    private int _nextId = 1;
    private bool _anyExecute;
    private bool _anyWatch;

    private StopInfo? _pending;
    private bool _skipBreakpointOnce;

    // Run modes set by step over / step out / run to.
    private ushort? _runToAddress;
    private int _runToMinStack = -1;
    private StopReason _runToReason;
    private int _stepOutStack = -1;

    private readonly BusCycle[] _busTrace = new BusCycle[4096];
    private int _busTraceNext;
    private int _busTraceCount;
    private readonly ushort[] _history = new ushort[256];
    private int _historyNext;
    private int _historyCount;

    public Debugger(Cpu6502 cpu, Func<ushort, byte> peek)
    {
        _cpu = cpu;
        _peek = peek;
    }

    /// <summary>Stop before executing BRK (KIM-1 programs often use BRK to return to the monitor).</summary>
    public bool BreakOnBrk { get; set; }

    /// <summary>Stop when the CPU executes a JAM opcode.</summary>
    public bool BreakOnJam { get; set; } = true;

    /// <summary>Record every bus cycle into a ring buffer (see <see cref="GetBusTrace"/>).</summary>
    public bool BusTraceEnabled { get; set; }

    /// <summary>True while a hook has to run every bus cycle.</summary>
    public bool WatchesBus => _anyWatch || BusTraceEnabled;

    public IReadOnlyList<Breakpoint> Breakpoints => _breakpoints;

    public bool StopRequested => _pending is not null;

    // ---------------------------------------------------------------- breakpoints

    public Breakpoint Add(ushort start, ushort end, BreakpointKind kind)
    {
        if (end < start) (start, end) = (end, start);
        var bp = new Breakpoint(_nextId++, start, end, kind, true);
        _breakpoints.Add(bp);
        Rebuild();
        return bp;
    }

    /// <summary>Adds an execute breakpoint at <paramref name="address"/>, or removes it if one is there.</summary>
    public void ToggleExecute(ushort address)
    {
        var existing = _breakpoints.FirstOrDefault(b => b.Kind == BreakpointKind.Execute && b.Start == address && b.End == address);
        if (existing is not null) Remove(existing.Id);
        else Add(address, address, BreakpointKind.Execute);
    }

    public void Remove(int id)
    {
        _breakpoints.RemoveAll(b => b.Id == id);
        Rebuild();
    }

    public void SetEnabled(int id, bool enabled)
    {
        int i = _breakpoints.FindIndex(b => b.Id == id);
        if (i < 0) return;
        _breakpoints[i] = _breakpoints[i] with { Enabled = enabled };
        Rebuild();
    }

    public void ClearBreakpoints()
    {
        _breakpoints.Clear();
        Rebuild();
    }

    private void Rebuild()
    {
        Array.Clear(_flags);
        foreach (var bp in _breakpoints.Where(b => b.Enabled))
        {
            byte flag = (byte)((bp.Kind.HasFlag(BreakpointKind.Execute) ? FlagExecute : 0)
                | (bp.Kind.HasFlag(BreakpointKind.Read) ? FlagRead : 0)
                | (bp.Kind.HasFlag(BreakpointKind.Write) ? FlagWrite : 0));
            for (int a = bp.Start; a <= bp.End; a++) _flags[a] |= flag;
        }

        _anyExecute = _breakpoints.Any(b => b.Enabled && b.Kind.HasFlag(BreakpointKind.Execute));
        _anyWatch = _breakpoints.Any(b => b.Enabled && (b.Kind & (BreakpointKind.Read | BreakpointKind.Write)) != 0);
    }

    // ---------------------------------------------------------------- commands (emulation thread, machine stopped)

    /// <summary>Call before resuming so a breakpoint at the current PC doesn't stop immediately again.</summary>
    public void PrepareResume()
    {
        _pending = null;
        _skipBreakpointOnce = true;
    }

    /// <summary>Clears any step-over / step-out / run-to target.</summary>
    public void CancelRunModes()
    {
        _runToAddress = null;
        _runToMinStack = -1;
        _stepOutStack = -1;
    }

    /// <summary>Runs until the instruction after the current one; a JSR is executed as a whole.</summary>
    /// <returns>False if the current instruction is not a JSR (the caller should single-step instead).</returns>
    public bool BeginStepOver()
    {
        if (_peek(_cpu.PC) != 0x20) return false; // JSR
        CancelRunModes();
        _runToAddress = (ushort)(_cpu.PC + 3);
        _runToMinStack = _cpu.S;
        _runToReason = StopReason.StepOver;
        PrepareResume();
        return true;
    }

    /// <summary>Runs until the current subroutine (or interrupt handler) returns.</summary>
    public void BeginStepOut()
    {
        CancelRunModes();
        _stepOutStack = _cpu.S;
        PrepareResume();
    }

    public void BeginRunTo(ushort address)
    {
        CancelRunModes();
        _runToAddress = address;
        _runToReason = StopReason.RunTo;
        PrepareResume();
    }

    /// <summary>Takes the pending stop (if any), clearing it.</summary>
    public StopInfo? TakeStop()
    {
        var stop = _pending;
        _pending = null;
        return stop;
    }

    /// <summary>Requests a stop, e.g. when the user presses Pause.</summary>
    public void RequestStop(StopReason reason, string message)
    {
        CancelRunModes();
        _pending ??= new StopInfo(reason, _cpu.PC, message);
    }

    // ---------------------------------------------------------------- hooks

    /// <summary>Called before each instruction. Returns true to stop without executing it.</summary>
    public bool ShouldStopBefore()
    {
        if (_pending is not null) return true;
        if (_cpu.InterruptPending) return false; // the next step runs the interrupt sequence, not PC's instruction

        ushort pc = _cpu.PC;
        bool skip = _skipBreakpointOnce;
        _skipBreakpointOnce = false;

        if (_runToAddress == pc && (_runToMinStack < 0 || _cpu.S >= _runToMinStack))
        {
            var reason = _runToReason;
            CancelRunModes();
            return Stop(reason, pc, reason == StopReason.StepOver ? "Stepped over" : $"Reached ${pc:X4}");
        }

        if (skip) return false;

        if (_anyExecute && (_flags[pc] & FlagExecute) != 0)
            return Stop(StopReason.Breakpoint, pc, $"Breakpoint at ${pc:X4}");

        if (BreakOnBrk && _peek(pc) == 0x00)
            return Stop(StopReason.Brk, pc, $"BRK at ${pc:X4}");

        return false;
    }

    /// <summary>Called after each instruction or interrupt sequence.</summary>
    public void AfterInstruction(ushort pcBefore, byte opcode, bool wasInterrupt)
    {
        if (!wasInterrupt)
        {
            _history[_historyNext] = pcBefore;
            _historyNext = (_historyNext + 1) % _history.Length;
            if (_historyCount < _history.Length) _historyCount++;
        }

        if (_cpu.Jammed && BreakOnJam && _pending is null)
            Stop(StopReason.Jam, pcBefore, $"CPU jammed by ${opcode:X2} at ${pcBefore:X4}");

        if (_stepOutStack >= 0 && !wasInterrupt && opcode is 0x60 or 0x40 && _cpu.S > _stepOutStack)
        {
            CancelRunModes();
            Stop(StopReason.StepOut, _cpu.PC, "Stepped out");
        }
    }

    public void OnRead(ushort address, byte value, bool sync, long cycle)
    {
        if (BusTraceEnabled) Trace(new BusCycle(cycle, address, value, sync ? BusCycleKind.Fetch : BusCycleKind.Read));
        if (_anyWatch && !sync && (_flags[address] & FlagRead) != 0 && _pending is null)
            Stop(StopReason.Watchpoint, _cpu.PC, $"Read ${address:X4} = ${value:X2}");
    }

    public void OnWrite(ushort address, byte value, long cycle)
    {
        if (BusTraceEnabled) Trace(new BusCycle(cycle, address, value, BusCycleKind.Write));
        if (_anyWatch && (_flags[address] & FlagWrite) != 0 && _pending is null)
            Stop(StopReason.Watchpoint, _cpu.PC, $"Write ${address:X4} ← ${value:X2}");
    }

    // ---------------------------------------------------------------- traces

    /// <summary>Recorded bus cycles, oldest first.</summary>
    public BusCycle[] GetBusTrace()
    {
        var result = new BusCycle[_busTraceCount];
        int start = (_busTraceNext - _busTraceCount + _busTrace.Length) % _busTrace.Length;
        for (int i = 0; i < _busTraceCount; i++) result[i] = _busTrace[(start + i) % _busTrace.Length];
        return result;
    }

    public void ClearBusTrace() => _busTraceCount = 0;

    /// <summary>Addresses of recently executed instructions, oldest first.</summary>
    public ushort[] GetHistory()
    {
        var result = new ushort[_historyCount];
        int start = (_historyNext - _historyCount + _history.Length) % _history.Length;
        for (int i = 0; i < _historyCount; i++) result[i] = _history[(start + i) % _history.Length];
        return result;
    }

    private void Trace(BusCycle cycle)
    {
        _busTrace[_busTraceNext] = cycle;
        _busTraceNext = (_busTraceNext + 1) % _busTrace.Length;
        if (_busTraceCount < _busTrace.Length) _busTraceCount++;
    }

    private bool Stop(StopReason reason, ushort pc, string message)
    {
        _pending = new StopInfo(reason, pc, message);
        return true;
    }
}
