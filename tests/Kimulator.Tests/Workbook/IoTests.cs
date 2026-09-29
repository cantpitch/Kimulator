using Kimulator.Kim1;

namespace Kimulator.Tests.Workbook;

/// <summary>Workbook part 3: the monitor's display and keypad routines and the 6530s.</summary>
public class IoTests
{
    [Fact]
    public void P26_ShowValue()
    {
        var k = new WorkbookRig("p26-show-value");
        k.Go();
        k.RunMs(100);
        Assert.Equal("C0DE 42", k.Display());
    }

    [Fact]
    public void P27_HexEntry()
    {
        var k = new WorkbookRig("p27-hex-entry");
        k.Go();
        k.RunMs(50);
        Assert.Equal("0000 00", k.Display());
        k.Press(Kim1Key.DA);
        k.Press(Kim1Key.Go);               // ignored
        k.Press(Kim1Key.D7, holdMs: 400);  // held: still one digit
        Assert.Equal("0000 A7", k.Display());
        foreach (var key in new[] { Kim1Key.D1, Kim1Key.D2, Kim1Key.D3, Kim1Key.D4, Kim1Key.D5, Kim1Key.D6 })
            k.Press(key);
        Assert.Equal("1234 56", k.Display());
    }

    [Fact]
    public void P28_KeyCounter()
    {
        var k = new WorkbookRig("p28-key-counter");
        k.Go();
        for (int i = 0; i < 11; i++) k.Press(Kim1Key.D3);
        k.Press(Kim1Key.Plus, holdMs: 300);
        Assert.Equal("0012 12", k.Display());
    }

    [Fact]
    public void P28_KeyCounterCarriesInDecimal()
    {
        var k = new WorkbookRig("p28-key-counter");
        k.Go();
        k.RunMs(10);
        k[0xFA] = 0x99; // as if 99 keys had been counted
        k.Press(Kim1Key.DF);
        Assert.Equal("0100 0F", k.Display());
    }

    [Fact]
    public void P29_HelloLeds()
    {
        var k = new WorkbookRig("p29-hello-leds");
        k.Go();
        k.RunMs(100);
        Assert.Equal(new byte[] { 0x76, 0x79, 0x38, 0x38, 0x3F, 0x00 }, k.Segments());
    }

    [Fact]
    public void P30_Marquee()
    {
        var k = new WorkbookRig("p30-marquee");
        k.Go();
        k.RunMs(150);
        Assert.Equal(new byte[] { 0x76, 0x79, 0x38, 0x38, 0x3F, 0x00 }, k.Segments()); // "HELLO "
        k.RunMs(250);
        Assert.Equal(new byte[] { 0x79, 0x38, 0x38, 0x3F, 0x00, 0x7D }, k.Segments()); // "ELLO 6"
        k.RunMs(250 * 10);
        Assert.Equal(new byte[] { 0x00, 0x76, 0x79, 0x38, 0x38, 0x3F }, k.Segments()); // wrapped: " HELLO"
    }

    [Theory]
    [InlineData(Kim1Key.D0, "0000 00")]
    [InlineData(Kim1Key.D6, "0000 06")]
    [InlineData(Kim1Key.D7, "0000 07")]
    [InlineData(Kim1Key.DD, "0000 0D")]
    [InlineData(Kim1Key.Data, "0000 11")]
    [InlineData(Kim1Key.Pc, "0000 14")]
    public void P31_ReadKeypad(Kim1Key key, string display)
    {
        var k = new WorkbookRig("p31-read-keypad");
        k.Go();
        k.Press(key);
        Assert.Equal(display, k.Display());
    }

    [Fact]
    public void P32_EggTimer()
    {
        var k = new WorkbookRig("p32-egg-timer");
        Assert.True(k.Assembly.Symbols.TryGetAddress("MINS", out ushort mins));
        k.Poke(mins, 0x01, 0x02); // 1:02
        k.Go();
        k.RunMs(500);
        Assert.Equal("0102 00", k.Display());
        k.RunMs(1000);
        Assert.Equal("0101 00", k.Display());
        k.RunMs(1000);
        Assert.Equal("0100 00", k.Display());
        k.RunMs(1000);
        Assert.Equal("0059 00", k.Display()); // borrow from the minutes, in BCD
        k.RunMs(58_000);
        Assert.Equal("0001 00", k.Display());
        k.RunMs(1100);
        Assert.Equal("0000 EE", k.Display());
    }

    [Fact]
    public void P33_IrqStopwatch()
    {
        var k = new WorkbookRig("p33-irq-stopwatch", timerIrqJumper: true);
        k.Go();
        k.RunMs(500);
        Assert.Equal("0000 00", k.Display());

        k.Press(Kim1Key.Go, holdMs: 50, releaseMs: 0);   // start
        k.RunMs(62_450);
        k.Press(Kim1Key.Go, holdMs: 50, releaseMs: 200); // stop 62.5 s after starting
        string shown = k.Display();
        Assert.StartsWith("0102 ", shown);
        int hundredths = int.Parse(shown[5..]);
        Assert.InRange(hundredths, 40, 70); // 50, less the 0.16% short tick
        k.RunMs(1000);
        Assert.Equal(shown, k.Display()); // stopped
    }

    [Fact]
    public void P34_Tone()
    {
        var k = new WorkbookRig("p34-tone");
        k.Go();
        k.RunMs(10);
        double hz = k.CountPa0Edges(1000) / 2.0;
        Assert.InRange(hz, 436, 444);
    }

    [Fact]
    public void P35_Tune()
    {
        var k = new WorkbookRig("p35-tune");
        k.Go();
        double c4 = k.CountPa0Edges(180) / 2.0 / 0.18;
        Assert.InRange(c4, 257, 267);
        long cycles = k.RunToBrkFromHere(10_000_000);
        Assert.InRange((cycles + 180_000) / 1e6, 3.95, 4.05); // 64 units of 1/16 s
    }

    [Theory]
    [InlineData(Kim1Key.D0, 261.63)]
    [InlineData(Kim1Key.D5, 440.0)]
    [InlineData(Kim1Key.D7, 523.25)]
    [InlineData(Kim1Key.DA, 698.46)]
    [InlineData(Kim1Key.DD, 987.77)]
    [InlineData(Kim1Key.DF, 1174.66)]
    public void P36_Organ(Kim1Key key, double expected)
    {
        var k = new WorkbookRig("p36-organ");
        k.Go();
        k.RunMs(10);
        Assert.Equal(0, k.CountPa0Edges(100));
        k.Board.SetKey(key, true);
        k.RunMs(10);
        double hz = k.CountPa0Edges(500) / 2.0 / 0.5;
        Assert.InRange(hz, expected * 0.985, expected * 1.015);
        k.Board.SetKey(key, false);
        k.RunMs(10);
        Assert.Equal(0, k.CountPa0Edges(100));
    }
}
