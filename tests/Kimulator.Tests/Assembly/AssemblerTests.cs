using Kimulator.Core.Assembly;
using Kimulator.Core.Cpu;
using Kimulator.Core.Debugging;
using Kimulator.Kim1;

namespace Kimulator.Tests.Assembly;

public class AssemblerTests
{
    private static AssemblyResult Asm(string source, SymbolTable? predefined = null) =>
        Assembler.Assemble(source, new AssemblerOptions { PredefinedSymbols = predefined });

    private static byte[] Bytes(AssemblyResult result)
    {
        Assert.True(result.Success, string.Join("\n", result.Errors));
        return Assert.Single(result.Segments).Data;
    }

    [Fact]
    public void AssemblesBasicProgram()
    {
        var result = Asm("""
                    .org $0200
            COUNT = 5
            start:  ldx #COUNT      ; comment
            loop    dex
                    bne loop
                    stx $10
                    stx $1234
                    jmp start
            """);
        Assert.Equal(0x0200, result.Segments[0].Address);
        Assert.Equal(new byte[] { 0xA2, 0x05, 0xCA, 0xD0, 0xFD, 0x86, 0x10, 0x8E, 0x34, 0x12, 0x4C, 0x00, 0x02 }, Bytes(result));
        Assert.True(result.Symbols.TryGetAddress("LOOP", out ushort loop)); // case-insensitive
        Assert.Equal(0x0202, loop);
        Assert.Equal(0x0202, result.LineAddresses[4]);
    }

    [Fact]
    public void ForwardReferencesUseAbsoluteAddressing()
    {
        // 'data' is unknown on the first pass, so LDA uses absolute even though data ends up < $100.
        var bytes = Bytes(Asm("""
            * = $0010
                lda data
                lda z:data
            data: .byte 1
            """));
        Assert.Equal(new byte[] { 0xAD, 0x15, 0x00, 0xA5, 0x15, 0x01 }, bytes);
    }

    [Fact]
    public void ForceAbsoluteWithPrefix() =>
        Assert.Equal(new byte[] { 0xAD, 0x10, 0x00, 0xA5, 0x10 }, Bytes(Asm("lda a:$10\nlda $10")));

    [Fact]
    public void ExpressionsAndOperators()
    {
        var bytes = Bytes(Asm("""
                .org $1234
            here:
                lda #<here
                lda #>here
                lda #%1010 | 1
                lda #'A' + 1
                lda #(2+3)*4
                .word * , here+1
            """));
        Assert.Equal(new byte[] { 0xA9, 0x34, 0xA9, 0x12, 0xA9, 0x0B, 0xA9, 0x42, 0xA9, 0x14, 0x3E, 0x12, 0x35, 0x12 }, bytes);
    }

    [Fact]
    public void DataDirectives()
    {
        var result = Asm("""
                .org $0300
                .byte "HI", $0D, 0
                .asciiz "A"
                .dbyt $1234
                .res 2
                .byte 7
                .res 2, $EA
            """);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Equal(2, result.Segments.Count); // .res without a fill leaves a hole
        Assert.Equal(new byte[] { 0x48, 0x49, 0x0D, 0x00, 0x41, 0x00, 0x12, 0x34 }, result.Segments[0].Data);
        Assert.Equal(0x030A, result.Segments[1].Address);
        Assert.Equal(new byte[] { 0x07, 0xEA, 0xEA }, result.Segments[1].Data);
    }

    [Fact]
    public void IndirectAndIndexedModes()
    {
        var bytes = Bytes(Asm("""
                lda ($20,x)
                sta ($FA),y
                jmp ($17FA)
                lda $10,x
                ldx $10,y
                lda $10,y       ; no zp,Y for LDA: absolute,Y
                asl a
                asl
                lda (1+2)*2     ; parentheses group expressions except for JMP
            """));
        Assert.Equal(new byte[] { 0xA1, 0x20, 0x91, 0xFA, 0x6C, 0xFA, 0x17, 0xB5, 0x10, 0xB6, 0x10, 0xB9, 0x10, 0x00, 0x0A, 0x0A, 0xA5, 0x06 }, bytes);
    }

    [Fact]
    public void LocalAndAnonymousLabels()
    {
        var bytes = Bytes(Asm("""
            first:
            @loop:  dex
                    bne @loop
            second:
            @loop:  iny
                    bne @loop
            :       beq :+
                    jmp :-
            :       rts
            """));
        Assert.Equal(new byte[] { 0xCA, 0xD0, 0xFD, 0xC8, 0xD0, 0xFD, 0xF0, 0x03, 0x4C, 0x06, 0x02, 0x60 }, bytes);
    }

    [Fact]
    public void MonitorSymbolsArePredefined()
    {
        var bytes = Bytes(Asm("jsr OUTCH\nsta POINTL\nlda SAD", Kim1Board.MonitorSymbols));
        Assert.Equal(new byte[] { 0x20, 0xA0, 0x1E, 0x85, 0xFA, 0xAD, 0x40, 0x17 }, bytes);
    }

    [Fact]
    public void UndocumentedOpcodesAndAliases()
    {
        var bytes = Bytes(Asm("lax $10\nsax $20\ndcp $30,x\nisb $1234\nlax #$00\nslo ($10),y"));
        Assert.Equal(new byte[] { 0xA7, 0x10, 0x87, 0x20, 0xD7, 0x30, 0xEF, 0x34, 0x12, 0xAB, 0x00, 0x13, 0x10 }, bytes);
    }

    [Theory]
    [InlineData("lda undefined", 1, "Undefined symbol: undefined")]
    [InlineData("nop\nfoo #1", 2, "Unknown instruction")]
    [InlineData("x: nop\nx: nop", 2, "defined more than once")]
    [InlineData("lda #$123", 1, "does not fit in a byte")]
    [InlineData("jmp $10,x", 1, "does not support")]
    [InlineData(".byte \"abc", 1, "closing quote")]
    public void ReportsErrorsWithLineNumbers(string source, int line, string message)
    {
        var result = Asm(source);
        Assert.False(result.Success);
        var error = result.Errors[0];
        Assert.Equal(line, error.Line);
        Assert.Contains(message, error.Message);
        Assert.Empty(result.Segments);
    }

    [Fact]
    public void BranchOutOfRange()
    {
        var result = Asm("start: .res 200\n bne start");
        Assert.Contains(result.Errors, e => e.Line == 2 && e.Message.Contains("out of range"));
    }

    [Fact]
    public void EveryOpcodeRoundTripsThroughTheDisassembler()
    {
        var memory = new byte[0x10000];
        for (int op = 0; op < 256; op++)
        {
            // Operands: $34 for zero page, $1234 for absolute, so the assembler picks the same size.
            memory[0x0400] = (byte)op;
            memory[0x0401] = 0x34;
            memory[0x0402] = 0x12;
            var original = Disassembler.Disassemble(a => memory[a], 0x0400);

            var result = Assembler.Assemble($"* = $0400\n {original.Text}");
            Assert.True(result.Success, $"${op:X2} '{original.Text}': {string.Join("; ", result.Errors)}");

            var segment = Assert.Single(result.Segments);
            var reassembled = new byte[0x10000];
            segment.Data.CopyTo(reassembled, 0x0400);
            var again = Disassembler.Disassemble(a => reassembled[a], 0x0400);
            Assert.Equal(original.Text, again.Text);
            Assert.Equal(original.Length, segment.Data.Length);
            if (!Opcodes.Get((byte)op).Undocumented) Assert.Equal((byte)op, segment.Data[0]);
        }
    }

    [Fact]
    public void AssembledProgramRunsOnTheKim()
    {
        var result = Asm("""
                    .org $0200
            ; sum 1..10 into $10
                    lda #0
                    ldx #10
            @add:   stx $11
                    clc
                    adc $11
                    dex
                    bne @add
                    sta $10
            done:   jmp done
            """);
        Assert.True(result.Success);

        var board = Kim1Board.CreateWithDefaultRoms();
        board.PowerOn();
        foreach (var s in result.Segments) board.LoadMemory(s.Address, s.Data);
        board.Cpu.PC = result.StartAddress!.Value;
        board.RunUntil(board.Cycles + 10_000);
        Assert.Equal(55, board.Ram[0x10]);
    }

    [Fact]
    public void ListingShowsAddressesAndBytes()
    {
        var listing = Asm(".org $0200\nstart: lda #1 ; one").FormatListing();
        Assert.Contains("0200  A9 01", listing);
        Assert.Contains("start: lda #1 ; one", listing);
    }
}
