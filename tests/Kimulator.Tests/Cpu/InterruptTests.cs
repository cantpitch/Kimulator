using Kimulator.Core.Cpu;

namespace Kimulator.Tests.Cpu;

/// <summary>Interrupt timing and stack behaviour, which the single-instruction suites don't cover.</summary>
public class InterruptTests
{
    private const byte Nop = 0xEA;
    private readonly RecordingBus _bus = new() { Record = false };
    private readonly Cpu6502 _cpu;

    public InterruptTests()
    {
        Array.Fill(_bus.Memory, Nop);
        SetVector(Cpu6502.IrqVector, 0x0300);
        SetVector(Cpu6502.NmiVector, 0x0400);
        _cpu = new Cpu6502(_bus) { PC = 0x0200, S = 0xFF, P = (byte)StatusFlags.InterruptDisable };
    }

    [Fact]
    public void IrqIsDelayedOneInstructionAfterCli()
    {
        _bus.Memory[0x0200] = 0x58; // CLI
        _cpu.IrqLine = true;

        _cpu.Step(); // CLI
        Assert.False(_cpu.InterruptPending);
        _cpu.Step(); // NOP runs before the IRQ is taken
        Assert.Equal(0x0202, _cpu.PC);
        Assert.True(_cpu.InterruptPending);

        Assert.Equal(7, _cpu.Step());
        Assert.Equal(0x0300, _cpu.PC);
        Assert.Equal(0x02, _bus.Memory[0x01FF]); // PCH
        Assert.Equal(0x02, _bus.Memory[0x01FE]); // PCL
        Assert.Equal(0, _bus.Memory[0x01FD] & (byte)StatusFlags.Break);
        Assert.True((_cpu.P & (byte)StatusFlags.InterruptDisable) != 0);
    }

    [Fact]
    public void IrqIsIgnoredWhileInterruptsDisabled()
    {
        _cpu.IrqLine = true;
        for (int i = 0; i < 5; i++) _cpu.Step();
        Assert.Equal(0x0205, _cpu.PC);
    }

    [Fact]
    public void NmiIsEdgeTriggered()
    {
        _cpu.NmiLine = true;
        _cpu.Step();
        Assert.True(_cpu.InterruptPending);
        _cpu.Step();
        Assert.Equal(0x0400, _cpu.PC);

        // Holding the line does not trigger again.
        for (int i = 0; i < 5; i++) _cpu.Step();
        Assert.Equal(0x0405, _cpu.PC);

        // A new edge does.
        _cpu.NmiLine = false;
        _cpu.Step();
        _cpu.NmiLine = true;
        _cpu.Step();
        _cpu.Step();
        Assert.Equal(0x0400, _cpu.PC);
    }

    [Fact]
    public void BrkPushesBreakFlagAndSkipsPaddingByte()
    {
        _bus.Memory[0x0200] = 0x00; // BRK
        Assert.Equal(7, _cpu.Step());
        Assert.Equal(0x0300, _cpu.PC);
        Assert.Equal(0x02, _bus.Memory[0x01FF]);
        Assert.Equal(0x02, _bus.Memory[0x01FE]); // return address skips the padding byte
        Assert.NotEqual(0, _bus.Memory[0x01FD] & (byte)StatusFlags.Break);
    }

    [Fact]
    public void ResetLoadsVectorAndSetsInterruptDisable()
    {
        SetVector(Cpu6502.ResetVector, 0x1C22);
        _cpu.P = 0;
        _cpu.S = 0x00;
        _cpu.Reset();
        Assert.Equal(0x1C22, _cpu.PC);
        Assert.Equal(0xFD, _cpu.S);
        Assert.True((_cpu.P & (byte)StatusFlags.InterruptDisable) != 0);
    }

    private void SetVector(ushort vector, ushort target)
    {
        _bus.Memory[vector] = (byte)target;
        _bus.Memory[vector + 1] = (byte)(target >> 8);
    }
}
