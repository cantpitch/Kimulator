using Kimulator.Kim1;
using Kimulator.Kim1.Cards;

namespace Kimulator.Tests.Workbook;

/// <summary>Workbook part 4: the MTU K-1008 Visible Memory at $2000.</summary>
public class VisibleMemoryTests
{
    private const int W = VisibleMemoryCard.Width, H = VisibleMemoryCard.Height;

    [Theory]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    [InlineData(0xAA)]
    public void P37_Fill(byte value)
    {
        var k = new WorkbookRig("p37-fill", visibleMemory: true);
        Array.Fill(k.Screen!.Memory, (byte)0x5C);
        k[0x10] = value;
        k.RunToBrk();
        Assert.All(k.Screen.Memory, b => Assert.Equal(value, b));
    }

    [Fact]
    public void P38_Checkerboard()
    {
        var k = new WorkbookRig("p38-checkerboard", visibleMemory: true);
        k.RunToBrk();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                Assert.Equal(((x / 8) ^ (y / 8)) % 2 == 1, k.Screen!.Dot(x, y));
    }

    [Fact]
    public void P39_Plot()
    {
        var k = new WorkbookRig("p39-plot", visibleMemory: true);
        k.RunToBrk();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                Assert.Equal(x == 0 || y == 0 || x == W - 1 || y == H - 1 || x == y, k.Screen!.Dot(x, y));
    }

    [Fact]
    public void P40_LineDemo()
    {
        var k = new WorkbookRig("p40-line", visibleMemory: true);
        k.RunToBrk();
        var expected = new bool[W, H];
        foreach (var (x0, y0, x1, y1) in new[]
                 {
                     (0, 0, 319, 199), (319, 0, 0, 199), (160, 0, 160, 199), (0, 100, 319, 100),
                     (100, 20, 220, 180), (220, 20, 100, 180),
                 })
            Bresenham(expected, x0, y0, x1, y1);
        AssertScreen(k, expected);
    }

    [Fact]
    public void P40_LineMatchesBresenhamInEveryDirection()
    {
        var k = new WorkbookRig("p40-line", visibleMemory: true);
        ushort line = Symbol(k, "LINE");
        var random = new Random(6502);
        var expected = new bool[W, H];
        var ends = new List<(int, int, int, int)> { (5, 5, 5, 5), (0, 199, 319, 0), (319, 199, 0, 0), (10, 190, 10, 3), (300, 7, 2, 7) };
        for (int i = 0; i < 40; i++) ends.Add((random.Next(W), random.Next(H), random.Next(W), random.Next(H)));
        foreach (var (x0, y0, x1, y1) in ends)
        {
            k.Poke(0x40, (byte)x0, (byte)(x0 >> 8), (byte)y0, (byte)x1, (byte)(x1 >> 8), (byte)y1);
            k.Call(line);
            Bresenham(expected, x0, y0, x1, y1);
        }

        AssertScreen(k, expected);
    }

    [Fact]
    public void P41_Text()
    {
        var k = new WorkbookRig("p41-text", visibleMemory: true);
        k.RunToBrk();
        string chars = System.Text.Encoding.ASCII.GetString(k.Bytes(Symbol(k, "CHARS"), 19));
        ushort font = Symbol(k, "FONT");
        var expected = new byte[VisibleMemoryCard.DisplayBytes];
        void Print(string text, int col, int row)
        {
            foreach (char c in text)
            {
                int glyph = font + 8 * chars.IndexOf(c);
                for (int line = 0; line < 8; line++) expected[row * 320 + line * 40 + col] = k[glyph + line];
                col++;
            }
        }

        Print("HELLO, WORLD!", 13, 10);
        Print("KIM-1 6502", 15, 12);
        Assert.Equal(expected, k.Screen!.Memory.Take(VisibleMemoryCard.DisplayBytes));
        Assert.Equal(0x44, k[0x2000 + 10 * 320 + 13]); // the top of the H
    }

    [Fact]
    public void P42_Scroll()
    {
        var k = new WorkbookRig("p42-scroll", visibleMemory: true);
        var before = Enumerable.Range(0, 0x2000).Select(i => (byte)(i * 7 + i / 40)).ToArray();
        before.CopyTo(k.Screen!.Memory, 0);
        k.RunToBrk();
        var mem = k.Screen.Memory;
        Assert.Equal(before[320..8000], mem[..7680]);
        Assert.All(mem[7680..8000], b => Assert.Equal(0, b));
        Assert.Equal(before[8000..], mem[8000..]); // the hidden 192 bytes are left alone
    }

    [Fact]
    public void P43_Bounce()
    {
        var k = new WorkbookRig("p43-bounce", visibleMemory: true);
        ushort frame = Symbol(k, "FRAME");
        k.Go();
        int minX = 999, maxX = -1, minY = 999, maxY = -1, frames = 0;
        long end = k.Board.Cycles + 10_000_000;
        ushort lastPc = 0;
        while (k.Board.Cycles < end)
        {
            k.Board.StepInstruction();
            ushort pc = k.Board.Cpu.PC;
            if (pc == frame && lastPc != frame + 3) // arrived at FRAME, not looping in its BIT/BPL wait
            {
                int bx = k.Word(0x30), by = k[0x32];
                minX = Math.Min(minX, bx); maxX = Math.Max(maxX, bx);
                minY = Math.Min(minY, by); maxY = Math.Max(maxY, by);
                if (frames++ % 25 == 0) AssertBall(k, bx, by);
            }

            lastPc = pc;
        }

        Assert.InRange(frames, 480, 500); // 10 s at 1/50 s (20.48 ms) per frame
        Assert.Equal((0, 312, 0, 192), (minX, maxX, minY, maxY));
    }

    [Fact]
    public void P44_Sketch()
    {
        var k = new WorkbookRig("p44-sketch", visibleMemory: true);
        Array.Fill(k.Screen!.Memory, (byte)0xFF);
        k.Go();
        k.RunMs(100);
        Assert.Equal(1, LitDots(k));
        Assert.True(k.Screen.Dot(160, 100));

        k.Press(Kim1Key.D6, holdMs: 500, releaseMs: 100); // right
        int x = k.Word(0x30);
        Assert.InRange(x, 160 + 22, 160 + 26);
        k.Press(Kim1Key.D9, holdMs: 300, releaseMs: 100); // up
        int y = k[0x32];
        Assert.InRange(y, 100 - 16, 100 - 13);
        for (int i = 160; i <= x; i++) Assert.True(k.Screen.Dot(i, 100));
        for (int j = y; j <= 100; j++) Assert.True(k.Screen.Dot(x, j));
        Assert.Equal(x - 160 + 100 - y + 1, LitDots(k));

        k.Board.SetKey(Kim1Key.D4, true); // left, all the way
        k.RunMs(5000);
        k.Board.SetKey(Kim1Key.D4, false);
        Assert.Equal(0, k.Word(0x30));
        Assert.True(k.Screen.Dot(0, y));

        k.Press(Kim1Key.D0); // clear
        k.RunMs(100);
        Assert.Equal(1, LitDots(k));
        Assert.True(k.Screen.Dot(0, y));
    }

    // ------------------------------------------------------------------ helpers

    private static ushort Symbol(WorkbookRig k, string name)
    {
        Assert.True(k.Assembly.Symbols.TryGetAddress(name, out ushort a), name);
        return a;
    }

    private static int LitDots(WorkbookRig k)
    {
        int n = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (k.Screen!.Dot(x, y)) n++;
        return n;
    }

    private static void AssertBall(WorkbookRig k, int bx, int by)
    {
        byte[] shape = [0x3C, 0x7E, 0xFF, 0xFF, 0xFF, 0xFF, 0x7E, 0x3C];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool inBall = x >= bx && x < bx + 8 && y >= by && y < by + 8 && (shape[y - by] & (0x80 >> (x - bx))) != 0;
                Assert.True(inBall == k.Screen!.Dot(x, y), $"ball at ({bx},{by}): dot ({x},{y})");
            }
    }

    private static void AssertScreen(WorkbookRig k, bool[,] expected)
    {
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                Assert.True(expected[x, y] == k.Screen!.Dot(x, y), $"dot ({x},{y})");
    }

    private static void Bresenham(bool[,] dots, int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            dots[x0, y0] = true;
            if (x0 == x1 && y0 == y1) return;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
