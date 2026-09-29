using Kimulator.Core.Assembly;
using Kimulator.Kim1;
using Kimulator.Kim1.Cards;

namespace Kimulator.Tests.Workbook;

/// <summary>
/// Assembles one answer from docs/workbook/answers and runs it on a headless KIM-1,
/// the way a reader would: monitor booted, vectors preset, program started at its start label.
/// </summary>
internal sealed class WorkbookRig
{
    // Monitor TABLE ($1FE7) with bit 7 masked off: segment patterns for 0-F.
    private static readonly byte[] HexSegments =
        [0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F, 0x77, 0x7C, 0x39, 0x5E, 0x79, 0x71];

    public WorkbookRig(string answer, bool visibleMemory = false, bool timerIrqJumper = false)
    {
        string path = Path.Combine(TestPaths.RepoRoot, "docs", "workbook", "answers", answer + ".s");
        Assembly = Assembler.Assemble(File.ReadAllText(path), new AssemblerOptions { PredefinedSymbols = Kim1Board.MonitorSymbols });
        Assert.True(Assembly.Success, string.Join('\n', Assembly.Errors));

        Board = Kim1Board.CreateWithDefaultRoms();
        if (visibleMemory)
        {
            Screen = new VisibleMemoryCard(VisibleMemoryCard.SwitchesFor(0x2000));
            Board.AddCard(Screen);
        }

        Board.Riot003TimerIrqJumper = timerIrqJumper;
        Board.PowerOn();
        Board.Poke(0x17FA, 0x00); // what "Preset NMI/IRQ vectors" does: ST and BRK go to SAVE
        Board.Poke(0x17FB, 0x1C);
        Board.Poke(0x17FE, 0x00);
        Board.Poke(0x17FF, 0x1C);
        RunMs(20); // let the monitor initialise (stack, ports)

        foreach (var segment in Assembly.Segments)
            Assert.Equal(0, Board.LoadMemory(segment.Address, segment.Data));
    }

    public Kim1Board Board { get; }

    public AssemblyResult Assembly { get; }

    public VisibleMemoryCard? Screen { get; }

    public ushort Start => Assembly.Symbols.TryGetAddress("start", out ushort a) ? a : Assembly.StartAddress!.Value;

    public byte this[int address]
    {
        get => Board.Peek((ushort)address);
        set => Board.Poke((ushort)address, value);
    }

    public void Poke(int address, params byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++) Board.Poke((ushort)(address + i), bytes[i]);
    }

    public byte[] Bytes(int address, int count) =>
        Enumerable.Range(address, count).Select(a => Board.Peek((ushort)a)).ToArray();

    public int Word(int address) => this[address] | this[address + 1] << 8;

    /// <summary>Jumps to the program's start.</summary>
    public void Go() => Board.Cpu.PC = Start;

    /// <summary>Starts the program and runs it until it reaches a BRK. Returns the cycles it took.</summary>
    public long RunToBrk(long maxCycles = 5_000_000)
    {
        Go();
        return RunToBrkFromHere(maxCycles);
    }

    /// <summary>Calls one subroutine of the program: runs JSR address / BRK from spare 6530 RAM.</summary>
    public void Call(ushort address, long maxCycles = 5_000_000)
    {
        Poke(0x1780, 0x20, (byte)address, (byte)(address >> 8), 0x00);
        Board.Cpu.PC = 0x1780;
        RunToBrkFromHere(maxCycles);
    }

    /// <summary>Keeps running the already started program until it reaches a BRK.</summary>
    public long RunToBrkFromHere(long maxCycles)
    {
        long start = Board.Cycles;
        while (Board.Peek(Board.Cpu.PC) != 0x00)
        {
            Board.StepInstruction();
            Assert.True(Board.Cycles - start < maxCycles, $"No BRK within {maxCycles} cycles (PC=${Board.Cpu.PC:X4}).");
        }

        return Board.Cycles - start;
    }

    public void RunMs(double ms) => Board.RunUntil(Board.Cycles + (long)(ms * 1000));

    public void Press(Kim1Key key, int holdMs = 60, int releaseMs = 80)
    {
        Board.SetKey(key, true);
        RunMs(holdMs);
        Board.SetKey(key, false);
        RunMs(releaseMs);
    }

    /// <summary>The LEDs as text, e.g. "1234 56"; '?' for a pattern that isn't a hex digit.</summary>
    public string Display()
    {
        var chars = new List<char>();
        for (int d = 0; d < LedDisplay.DigitCount; d++)
        {
            if (d == 4) chars.Add(' ');
            byte seg = Board.Display.Latest.SegmentsOf(d);
            int hex = Array.IndexOf(HexSegments, seg);
            chars.Add(seg == 0 ? ' ' : hex >= 0 ? "0123456789ABCDEF"[hex] : '?');
        }

        return new string(chars.ToArray());
    }

    /// <summary>Raw segment patterns of the six digits in the last complete frame.</summary>
    public byte[] Segments() =>
        Enumerable.Range(0, LedDisplay.DigitCount).Select(d => Board.Display.Latest.SegmentsOf(d)).ToArray();

    /// <summary>Runs for <paramref name="ms"/> and counts how often PA0 of the application port changes.</summary>
    public int CountPa0Edges(double ms)
    {
        long end = Board.Cycles + (long)(ms * 1000);
        int edges = 0;
        bool last = Pa0;
        while (Board.Cycles < end)
        {
            Board.StepInstruction();
            if (Pa0 != last)
            {
                edges++;
                last = Pa0;
            }
        }

        return edges;
    }

    private bool Pa0 => (Board.Riot003.PortAData & Board.Riot003.PortADirection & 1) != 0;
}
