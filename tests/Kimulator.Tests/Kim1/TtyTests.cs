using System.Text;
using Kimulator.Core.Formats;
using Kimulator.Kim1;

namespace Kimulator.Tests.Kim1;

/// <summary>Drives the monitor's teletype mode through the bit-level serial interface.</summary>
public class TtyTests
{
    private readonly Kim1Board _board = Kim1Board.CreateWithDefaultRoms();
    private readonly StringBuilder _output = new();

    private void Boot(int baud)
    {
        _board.Tty.BaudRate = baud;
        _board.Tty.CharacterReceived += b => _output.Append((char)(b & 0x7F));
        _board.TtyMode = true;
        _board.PowerOn();
        RunMs(10);
        _board.Tty.Send(TtyInterface.Rubout); // lets the monitor measure the bit time
        RunUntilOutputEndsWith("0000 00 ", 5000); // ~30 characters: 3 s at 110 baud
    }

    [Theory]
    [InlineData(110)]
    [InlineData(300)]
    [InlineData(1200)]
    [InlineData(2400)]
    public void MonitorCalibratesAndPrintsBanner(int baud)
    {
        Boot(baud);
        Assert.Contains("KIM", Visible());
        Assert.EndsWith("0000 00 ", Visible());
    }

    [Fact]
    public void OpenCellDepositAndHardwareEcho()
    {
        Boot(2400);
        Type("0200 ");
        RunUntilOutputEndsWith("0200 00 ", 1000);

        Type("A9.");
        RunUntilOutputEndsWith("0201 00 ", 1000);
        Assert.Equal(0xA9, _board.Ram[0x200]);

        // What was typed shows up in the output via the hardware echo.
        Assert.Contains("A9.", Visible());
    }

    [Fact]
    public void DumpProducesValidPaperTape()
    {
        Boot(2400);
        for (int i = 0; i < 40; i++) _board.Poke((ushort)(0x0100 + i), (byte)(i * 7));
        _board.Poke(0x17F7, 0x28); // EAL: end (exclusive) = $0128
        _board.Poke(0x17F8, 0x01); // EAH

        _output.Clear();
        Type("0100 Q");
        // The final record ";00" + record count + checksum ends the dump; the monitor then waits for input.
        RunUntil(() => System.Text.RegularExpressions.Regex.IsMatch(Visible(), ";00[0-9A-F]{8}"), 5000);

        var tape = Visible();
        var segments = PaperTape.Parse(tape[tape.IndexOf(';')..]);
        var loaded = Assert.Single(segments);
        Assert.Equal(0x0100, loaded.Address);
        Assert.Equal(48, loaded.Data.Length); // the monitor dumps whole 24-byte records
        for (int i = 0; i < 40; i++) Assert.Equal((byte)(i * 7), loaded.Data[i]);
    }

    [Fact]
    public void LoadCommandReadsPaperTape()
    {
        Boot(2400);
        byte[] program = Enumerable.Range(0, 30).Select(i => (byte)(0xA0 + i)).ToArray();
        string tape = PaperTape.Write(0x0300, program);

        Type("L" + tape);
        // After the last record the monitor prints X-OFF "KIM" and shows the record count as the open cell.
        RunUntil(() => _board.Tty.PendingInput == 0 && Visible().Contains(";0000020002 KIM", StringComparison.Ordinal), 10000);

        Assert.Equal(program, _board.Ram[0x300..0x31E]);
        Assert.DoesNotContain("ERR", Visible());
    }

    [Fact]
    public void KeyboardModeIgnoresTtyInput()
    {
        _board.Tty.CharacterReceived += b => _output.Append((char)b);
        _board.PowerOn();
        RunMs(50);
        _board.Tty.Send(TtyInterface.Rubout);
        RunMs(100);
        Assert.Equal(0, _board.Tty.PendingInput);
        Assert.DoesNotContain("KIM", Visible());
    }

    // ------------------------------------------------------------------ helpers

    private string Visible() => _output.ToString().Replace("\0", "").Replace("\x7F", "").Replace("\x13", "");

    private void Type(string text) => _board.Tty.Send(Encoding.ASCII.GetBytes(text));

    private void RunMs(int ms) => _board.RunUntil(_board.Cycles + ms * 1000L);

    private void RunUntilOutputEndsWith(string suffix, int timeoutMs) =>
        RunUntil(() => _board.Tty.PendingInput == 0 && Visible().EndsWith(suffix, StringComparison.Ordinal), timeoutMs);

    private void RunUntil(Func<bool> condition, int timeoutMs)
    {
        for (int ms = 0; ms < timeoutMs; ms++)
        {
            if (condition()) return;
            RunMs(1);
        }

        Assert.Fail($"Timed out. Output so far:\n{Visible()}");
    }
}
