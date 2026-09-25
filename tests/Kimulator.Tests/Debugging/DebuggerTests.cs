using Kimulator.Core.Debugging;
using Kimulator.Kim1;

namespace Kimulator.Tests.Debugging;

public class DebuggerTests
{
    private readonly Kim1Board _board = Kim1Board.CreateWithDefaultRoms();
    private Debugger Debugger => _board.Debugger;

    public DebuggerTests()
    {
        _board.PowerOn();
        // $0200: JSR $0300 / INC $10 / JMP $0200      $0300: INX / RTS
        Load(0x0200, 0x20, 0x00, 0x03, 0xE6, 0x10, 0x4C, 0x00, 0x02);
        Load(0x0300, 0xE8, 0x60);
        _board.Cpu.PC = 0x0200;
        _board.Cpu.S = 0xFF;
    }

    [Fact]
    public void ExecuteBreakpointStopsBeforeInstruction()
    {
        Debugger.Add(0x0203, 0x0203, BreakpointKind.Execute);
        Run();
        var stop = Assert.IsType<StopInfo>(Debugger.TakeStop());
        Assert.Equal(StopReason.Breakpoint, stop.Reason);
        Assert.Equal(0x0203, _board.Cpu.PC);
        Assert.Equal(0, _board.Ram[0x10]); // INC not executed yet

        // Resuming doesn't re-hit the same breakpoint immediately, but does on the next loop.
        Debugger.PrepareResume();
        Run();
        Assert.Equal(StopReason.Breakpoint, Debugger.TakeStop()!.Reason);
        Assert.Equal(1, _board.Ram[0x10]);
    }

    [Fact]
    public void WriteWatchpointStopsAfterTheWritingInstruction()
    {
        Debugger.Add(0x0010, 0x0010, BreakpointKind.Write);
        Run();
        var stop = Debugger.TakeStop()!;
        Assert.Equal(StopReason.Watchpoint, stop.Reason);
        Assert.Contains("$0010", stop.Message);
        Assert.Equal(0x0205, _board.Cpu.PC);
        Assert.Equal(1, _board.Ram[0x10]);
    }

    [Fact]
    public void ReadWatchpointIgnoresOpcodeFetches()
    {
        Debugger.Add(0x0300, 0x0300, BreakpointKind.Read); // executed, but never read as data
        Debugger.Add(0x0010, 0x0010, BreakpointKind.Read);
        Run();
        var stop = Debugger.TakeStop()!;
        Assert.Contains("Read $0010", stop.Message);
    }

    [Fact]
    public void StepOverRunsWholeSubroutine()
    {
        Assert.True(Debugger.BeginStepOver());
        Run();
        Assert.Equal(StopReason.StepOver, Debugger.TakeStop()!.Reason);
        Assert.Equal(0x0203, _board.Cpu.PC);
        Assert.Equal(1, _board.Cpu.X);
    }

    [Fact]
    public void StepOverIsRefusedForNonJsr()
    {
        _board.Cpu.PC = 0x0203;
        Assert.False(Debugger.BeginStepOver());
    }

    [Fact]
    public void StepOutReturnsToCaller()
    {
        _board.StepInstruction(); // JSR
        Assert.Equal(0x0300, _board.Cpu.PC);
        Debugger.BeginStepOut();
        Run();
        Assert.Equal(StopReason.StepOut, Debugger.TakeStop()!.Reason);
        Assert.Equal(0x0203, _board.Cpu.PC);
    }

    [Fact]
    public void RunToStopsAtAddress()
    {
        Debugger.BeginRunTo(0x0205);
        Run();
        Assert.Equal(StopReason.RunTo, Debugger.TakeStop()!.Reason);
        Assert.Equal(0x0205, _board.Cpu.PC);
    }

    [Fact]
    public void BreakOnBrk()
    {
        Load(0x0300, 0x00); // BRK instead of INX
        Debugger.BreakOnBrk = true;
        Run();
        Assert.Equal(StopReason.Brk, Debugger.TakeStop()!.Reason);
        Assert.Equal(0x0300, _board.Cpu.PC);
    }

    [Fact]
    public void BusTraceRecordsEveryCycle()
    {
        Debugger.BusTraceEnabled = true;
        _board.StepInstruction(); // JSR abs: 6 cycles
        var trace = Debugger.GetBusTrace();
        Assert.Equal(6, trace.Length);
        Assert.Equal(new BusCycle(trace[0].Cycle, 0x0200, 0x20, BusCycleKind.Fetch), trace[0]);
        Assert.Equal(BusCycleKind.Write, trace[3].Kind); // PCH pushed
        Assert.Equal(0x01FF, trace[3].Address);
    }

    [Fact]
    public void HistoryListsExecutedInstructions()
    {
        for (int i = 0; i < 4; i++) _board.StepInstruction();
        Assert.Equal(new ushort[] { 0x0200, 0x0300, 0x0301, 0x0203 }, Debugger.GetHistory());
    }

    private void Run() => _board.RunUntil(_board.Cycles + 100_000);

    private void Load(ushort address, params byte[] bytes) => _board.LoadMemory(address, bytes);
}
