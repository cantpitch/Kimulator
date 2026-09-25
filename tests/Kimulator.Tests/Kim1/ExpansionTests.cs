using System.Text;
using Kimulator.Kim1;
using Kimulator.Kim1.Cards;

namespace Kimulator.Tests.Kim1;

public class ExpansionTests
{
    private static Kim1Board Board(ExpansionConfig config)
    {
        var board = Kim1Board.CreateWithDefaultRoms();
        board.SetCards(config.Build(_ => throw new FileNotFoundException()));
        board.PowerOn();
        return board;
    }

    private static ExpansionConfig Config(bool kim4, params CardConfig?[] slots)
    {
        var config = new ExpansionConfig { Kim4 = kim4 };
        for (int i = 0; i < slots.Length; i++) config.SetSlot(i, slots[i]);
        return config;
    }

    // Switch strings in the manual's notation: switch 1..4, X = on.
    [Theory]
    [InlineData("0000", 0x0000)]
    [InlineData("00X0", 0x2000)]
    [InlineData("0XX0", 0x6000)]
    [InlineData("X0XX", 0xB000)]
    [InlineData("XXX0", 0xE000)]
    [InlineData("XXXX", 0xF000)]
    public void Kim2SwitchesMatchManualTable4(string switches, int address) =>
        Assert.Equal(address, RamCard.Kim2Address(Parse(switches)));

    [Theory]
    [InlineData("000", 0x0000)]
    [InlineData("00X", 0x2000)]
    [InlineData("X0X", 0xA000)]
    [InlineData("XXX", 0xE000)]
    public void Kim3SwitchesMatchManualTable5(string switches, int address) =>
        Assert.Equal(address, RamCard.Kim3Address(Parse(switches)));

    [Fact]
    public void BareKim1MirrorsItsMemoryEvery8K()
    {
        var board = Board(new ExpansionConfig());
        board.Poke(0x0010, 0x5A);
        Assert.Equal(0x5A, board.Peek(0x2010));
        Assert.Equal(0x5A, board.Peek(0xE010));
    }

    [Fact]
    public void Kim3OnExpansionConnectorAddsRam()
    {
        var board = Board(Config(false, CardConfig.Default(CardType.Kim3)));
        Assert.Equal(0, board.LoadMemory(0x2000, [1, 2, 3]));
        Assert.Equal(0, board.LoadMemory(0x3FFF, [9]));
        Assert.Equal(0x02, board.Peek(0x2001));
        Assert.Equal(0x09, board.Peek(0x3FFF));
        Assert.Equal(0x00, board.Peek(0x0001)); // KIM-1 RAM untouched
    }

    [Fact]
    public void Kim4StopsMirrorsButKeepsVectors()
    {
        var board = Board(Config(true));
        board.Poke(0x0010, 0x5A);
        Assert.NotEqual(0x5A, board.Peek(0x2010)); // no mirror any more: nothing answers
        Assert.Equal(board.Peek(0x1FFC), board.Peek(0xFFFC)); // reset vector still from the KIM-1
        board.RunUntil(100_000); // boots through the vector
        Assert.Equal(0x1C00, board.Cpu.PC & 0xFC00); // running the monitor ROM
    }

    [Fact]
    public void Kim5RomsAppearAtE000()
    {
        var board = Board(Config(true, CardConfig.Default(CardType.Kim5)));
        Assert.Equal(0xA9, board.Peek(0xE000));      // 6540-007 starts LDA #$00
        Assert.Equal((byte)'C', board.Peek(0xF000)); // 6540-009 starts with its message text
        Assert.Equal(0xFF, board.Peek(0xF800));      // socket U4 is empty
        Assert.Equal(1, board.LoadMemory(0xE000, [0])); // ROM is not writable
    }

    [Fact]
    public void ResidentAssemblerStartsOnTheTty()
    {
        var board = Board(Config(true, CardConfig.Default(CardType.Kim3), CardConfig.Default(CardType.Kim5)));
        var output = new StringBuilder();
        board.Tty.BaudRate = 2400;
        board.Tty.CharacterReceived += b => output.Append((char)(b & 0x7F));
        board.TtyMode = true;
        board.PowerOn();
        board.RunUntil(board.Cycles + 10_000);
        board.Tty.Send(TtyInterface.Rubout);
        board.RunUntil(board.Cycles + 1_000_000);
        Assert.Contains("KIM", output.ToString());

        // $F100 sets the editor's I/O vectors to the KIM monitor's TTY routines and starts the editor.
        output.Clear();
        board.Cpu.PC = 0xF100;
        board.RunUntil(board.Cycles + 2_000_000);
        Assert.Contains("BASE=", output.ToString().Replace("\0", ""));
    }

    [Fact]
    public void ReportsConfigurationProblems()
    {
        Assert.Empty(Config(true, CardConfig.Default(CardType.Kim3), CardConfig.Default(CardType.Kim5)).Problems());

        var low = CardConfig.Default(CardType.Kim2);
        low.Switches = 0;
        Assert.Contains(Config(false, low).Problems(), p => p.Contains("$2000 or above"));

        var top = CardConfig.Default(CardType.Kim2);
        top.Switches = 0b1111; // $F000
        Assert.Contains(Config(false, top).Problems(), p => p.Contains("vectors"));
        Assert.Empty(Config(true, top).Problems()); // the KIM-4 keeps $FFF8-$FFFF on the KIM-1

        Assert.Contains(Config(false, CardConfig.Default(CardType.Kim5)).Problems(), p => p.Contains("KIM-4"));
        Assert.Contains(Config(true, CardConfig.Default(CardType.Kim2), CardConfig.Default(CardType.Kim3)).Problems(), p => p.Contains("both answer"));
    }

    [Fact]
    public void SaveStatesIncludeCardMemory()
    {
        var config = Config(true, CardConfig.Default(CardType.Kim3));
        var board = Board(config);
        board.LoadMemory(0x2100, [0xDE, 0xAD]);
        using var state = new MemoryStream();
        board.SaveState(state);

        var restored = Board(config);
        state.Position = 0;
        restored.LoadState(state);
        Assert.Equal(0xAD, restored.Peek(0x2101));

        var different = Board(new ExpansionConfig());
        state.Position = 0;
        Assert.Throws<InvalidDataException>(() => different.LoadState(state));
    }

    private static int Parse(string switches) =>
        switches.Select((c, i) => c == 'X' ? 1 << i : 0).Sum();
}
