using Kimulator.Core.Debugging;
using Kimulator.Kim1;

namespace Kimulator.Tests.Debugging;

public class DisassemblerTests
{
    private static readonly byte[] Memory = new byte[0x10000];

    private static byte Read(ushort a) => Memory[a];

    [Theory]
    [InlineData(new byte[] { 0xA9, 0x42 }, "LDA #$42")]
    [InlineData(new byte[] { 0x85, 0x10 }, "STA $10")]
    [InlineData(new byte[] { 0xBD, 0x34, 0x12 }, "LDA $1234,X")]
    [InlineData(new byte[] { 0xB6, 0x80 }, "LDX $80,Y")]
    [InlineData(new byte[] { 0x6C, 0xFA, 0x17 }, "JMP ($17FA)")]
    [InlineData(new byte[] { 0xA1, 0x20 }, "LDA ($20,X)")]
    [InlineData(new byte[] { 0x91, 0xFA }, "STA ($FA),Y")]
    [InlineData(new byte[] { 0x0A }, "ASL A")]
    [InlineData(new byte[] { 0x00 }, "BRK")]
    [InlineData(new byte[] { 0xA7, 0x10 }, "LAX $10")]
    [InlineData(new byte[] { 0x02 }, "JAM")]
    public void FormatsAddressingModes(byte[] bytes, string expected)
    {
        bytes.CopyTo(Memory, 0x0200);
        var instruction = Disassembler.Disassemble(Read, 0x0200);
        Assert.Equal(expected, instruction.Text);
        Assert.Equal(bytes.Length, instruction.Length);
    }

    [Fact]
    public void RelativeBranchesShowTarget()
    {
        Memory[0x0210] = 0xD0; // BNE -4
        Memory[0x0211] = 0xFC;
        Assert.Equal("BNE $020E", Disassembler.Disassemble(Read, 0x0210).Text);
    }

    [Fact]
    public void UsesMonitorSymbols()
    {
        var board = Kim1Board.CreateWithDefaultRoms();
        var symbols = Kim1Board.MonitorSymbols;
        Assert.Equal("SAVE", symbols.NameOf(0x1C00));

        var lines = Disassembler.DisassembleRange(board.Peek, 0x1C00, 3, symbols);
        Assert.Equal(["STA ACC", "PLA", "STA PREG"], lines.Select(l => l.Text));

        // NMIT: JMP (NMIV)
        Assert.Equal("JMP (NMIV)", Disassembler.Disassemble(board.Peek, 0x1C1C, symbols).Text);
    }

    [Fact]
    public void FindsInstructionBoundaryBeforeTarget()
    {
        var board = Kim1Board.CreateWithDefaultRoms();
        // Walking forward from the found start must land exactly on START ($1C4F).
        ushort start = Disassembler.FindStartBefore(board.Peek, 0x1C4F, 5);
        Assert.True(start < 0x1C4F);
        var lines = Disassembler.DisassembleRange(board.Peek, start, 10);
        Assert.Contains(lines, l => l.Address == 0x1C4F);
    }
}
