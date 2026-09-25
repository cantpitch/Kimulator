using Kimulator.Kim1;

namespace Kimulator.Tests.Kim1;

/// <summary>Runs the real monitor ROM headless and drives it through the keypad, like a user would.</summary>
public class Kim1BoardTests
{
    // Monitor TABLE ($1FE7) with bit 7 masked off: segment patterns for 0-F.
    private static readonly byte[] HexSegments =
        [0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F, 0x77, 0x7C, 0x39, 0x5E, 0x79, 0x71];

    private readonly Kim1Board _board = Kim1Board.CreateWithDefaultRoms();

    public Kim1BoardTests() => _board.PowerOn();

    [Fact]
    public void ResetVectorComesFromMirroredMonitorRom()
    {
        Assert.Equal(0x22, _board.Peek(0xFFFC));
        Assert.Equal(0x1C, _board.Peek(0xFFFD));
        Assert.Equal(_board.Peek(0x1FFC), _board.Peek(0xFFFC));
    }

    [Fact]
    public void MonitorBootsAndShowsAddressAndData()
    {
        RunMs(100);
        Assert.Equal("0000 00", ReadDisplay());
    }

    [Fact]
    public void KeypadEntersAddressAndData()
    {
        RunMs(50);
        Press(Kim1Key.Address, Kim1Key.D0, Kim1Key.D2, Kim1Key.D0, Kim1Key.D0);
        Assert.Equal("0200 00", ReadDisplay());

        Press(Kim1Key.Data, Kim1Key.DA, Kim1Key.D9);
        Assert.Equal("0200 A9", ReadDisplay());
        Assert.Equal(0xA9, _board.Ram[0x200]);

        Press(Kim1Key.Plus);
        Assert.Equal("0201 00", ReadDisplay());
    }

    [Fact]
    public void GoRunsProgramAndStopReturnsToMonitor()
    {
        RunMs(50);
        // NMI vector -> monitor SAVE so ST works (users do this by hand on a real KIM-1).
        _board.Poke(0x17FA, 0x00);
        _board.Poke(0x17FB, 0x1C);
        // LDA #$42 / STA $10 / JMP $0204
        EnterProgram(0x0200, 0xA9, 0x42, 0x85, 0x10, 0x4C, 0x04, 0x02);

        Press(Kim1Key.Address, Kim1Key.D0, Kim1Key.D2, Kim1Key.D0, Kim1Key.D0, Kim1Key.Go);
        Assert.Equal(0x42, _board.Ram[0x10]);
        Assert.Equal(string.Empty, ReadDisplay().Trim()); // user program doesn't scan the display

        Press(Kim1Key.Stop);
        Assert.Equal("0204 4C", ReadDisplay());
    }

    [Fact]
    public void SingleStepExecutesOneInstructionPerGo()
    {
        RunMs(50);
        _board.Poke(0x17FA, 0x00);
        _board.Poke(0x17FB, 0x1C);
        EnterProgram(0x0200, 0xA9, 0x42, 0x85, 0x10, 0x4C, 0x04, 0x02);
        _board.SingleStep = true;

        Press(Kim1Key.Address, Kim1Key.D0, Kim1Key.D2, Kim1Key.D0, Kim1Key.D0, Kim1Key.Go);
        Assert.Equal("0202 85", ReadDisplay());
        Assert.Equal(0x42, _board.Ram[0xF3]); // monitor saved A

        Press(Kim1Key.Go);
        Assert.Equal("0204 4C", ReadDisplay());
        Assert.Equal(0x42, _board.Ram[0x10]);
    }

    [Fact]
    public void ResetKeyRestartsMonitor()
    {
        RunMs(50);
        Press(Kim1Key.Address, Kim1Key.D1, Kim1Key.D2, Kim1Key.D3, Kim1Key.D4);
        Assert.StartsWith("1234", ReadDisplay()); // data digits show open bus: $1234 is unpopulated
        Press(Kim1Key.Reset);
        Assert.StartsWith("1234", ReadDisplay()); // RS keeps POINT (RAM is not cleared)
    }

    [Fact]
    public void RiotTimerCountsAndFlagsTimeout()
    {
        var riot = _board.Riot003;
        riot.WriteIo(0x05, 2); // ÷8, IRQ disabled, count 2
        for (int i = 0; i < 8; i++) riot.Tick();
        Assert.Equal(1, riot.ReadIo(0x06));
        for (int i = 0; i < 16; i++) riot.Tick();
        Assert.Equal(0x80, riot.ReadIo(0x07));
    }

    // ------------------------------------------------------------------ helpers

    private void RunMs(int ms) => _board.RunUntil(_board.Cycles + ms * 1000L);

    private void Press(params Kim1Key[] keys)
    {
        foreach (var key in keys)
        {
            _board.SetKey(key, true);
            RunMs(40);
            _board.SetKey(key, false);
            RunMs(60);
        }
    }

    private void EnterProgram(ushort start, params byte[] bytes)
    {
        Press(Kim1Key.Address);
        foreach (int shift in new[] { 12, 8, 4, 0 })
            Press((Kim1Key)((start >> shift) & 0xF));
        Press(Kim1Key.Data);
        for (int i = 0; i < bytes.Length; i++)
        {
            Press((Kim1Key)(bytes[i] >> 4), (Kim1Key)(bytes[i] & 0xF));
            if (i < bytes.Length - 1) Press(Kim1Key.Plus);
        }

        for (int i = 0; i < bytes.Length; i++)
            Assert.Equal(bytes[i], _board.Ram[start + i]);
    }

    private string ReadDisplay()
    {
        var frame = _board.Display.Latest;
        var chars = new char[7];
        int c = 0;
        for (int d = 0; d < LedDisplay.DigitCount; d++)
        {
            if (d == 4) chars[c++] = ' ';
            byte seg = frame.SegmentsOf(d);
            int hex = Array.IndexOf(HexSegments, seg);
            chars[c++] = seg == 0 ? ' ' : hex >= 0 ? "0123456789ABCDEF"[hex] : '?';
        }

        return new string(chars);
    }
}
