namespace Kimulator.Tests.Workbook;

/// <summary>Workbook parts 1 and 2: plain 6502 problems that end with BRK.</summary>
public class BasicsTests
{
    [Fact]
    public void P01_HelloMemory()
    {
        var k = new WorkbookRig("p01-hello-memory");
        k.RunToBrk();
        Assert.Equal(0x42, k[0x10]);
    }

    [Fact]
    public void P02_CopyCat()
    {
        var k = new WorkbookRig("p02-copy-cat");
        k[0x10] = 0x9C;
        k.RunToBrk();
        Assert.Equal(new byte[] { 0x9C, 0x9C, 0x9C, 0x9C }, k.Bytes(0x10, 4));
    }

    [Fact]
    public void P03_Swap()
    {
        var k = new WorkbookRig("p03-swap");
        k.Poke(0x10, 0x12, 0xAB);
        k.RunToBrk();
        Assert.Equal(new byte[] { 0xAB, 0x12 }, k.Bytes(0x10, 2));
    }

    [Theory]
    [InlineData(0x41, 0x42, 0x40)]
    [InlineData(0xFF, 0x00, 0xFE)]
    [InlineData(0x00, 0x01, 0xFF)]
    public void P04_Neighbours(byte n, byte plus, byte minus)
    {
        var k = new WorkbookRig("p04-neighbours");
        k[0x10] = n;
        k.RunToBrk();
        Assert.Equal(new byte[] { n, plus, minus }, k.Bytes(0x10, 3));
    }

    [Fact]
    public void P05_InPlace()
    {
        var k = new WorkbookRig("p05-in-place");
        k.Poke(0x10, 0xFF, 0x02);
        k.RunToBrk();
        Assert.Equal(new byte[] { 0x01, 0xFF }, k.Bytes(0x10, 2));
    }

    [Theory]
    [InlineData(0x12, 0x34, 0x46)]
    [InlineData(0xF0, 0x20, 0x10)]
    public void P06_Add(byte a, byte b, byte sum)
    {
        var k = new WorkbookRig("p06-add");
        k.Poke(0x10, a, b);
        k.Board.Cpu.P |= 1; // a stale carry must not leak in
        k.RunToBrk();
        Assert.Equal(sum, k[0x12]);
    }

    [Theory]
    [InlineData(0x50, 0x20, 0x30)]
    [InlineData(0x10, 0x20, 0xF0)]
    public void P07_Subtract(byte a, byte b, byte diff)
    {
        var k = new WorkbookRig("p07-subtract");
        k.Poke(0x10, a, b);
        k.RunToBrk();
        Assert.Equal(diff, k[0x12]);
    }

    [Theory]
    [InlineData(0x12FF, 0x0001, 0x1300)]
    [InlineData(0x8000, 0x8001, 0x0001)]
    [InlineData(1234, 4321, 5555)]
    public void P08_Add16(int a, int b, int sum)
    {
        var k = new WorkbookRig("p08-add16");
        k.Poke(0x10, (byte)a, (byte)(a >> 8), (byte)b, (byte)(b >> 8));
        k.RunToBrk();
        Assert.Equal(sum, k.Word(0x14));
    }

    [Theory]
    [InlineData(0x1300, 0x0001, 0x12FF)]
    [InlineData(5555, 4321, 1234)]
    [InlineData(0x0000, 0x0001, 0xFFFF)]
    public void P09_Sub16(int a, int b, int diff)
    {
        var k = new WorkbookRig("p09-sub16");
        k.Poke(0x10, (byte)a, (byte)(a >> 8), (byte)b, (byte)(b >> 8));
        k.RunToBrk();
        Assert.Equal(diff, k.Word(0x14));
    }

    [Fact]
    public void P10_TimesTen()
    {
        for (int n = 0; n <= 25; n++)
        {
            var k = new WorkbookRig("p10-times-ten");
            k[0x10] = (byte)n;
            k.RunToBrk();
            Assert.Equal(10 * n, k[0x11]);
        }
    }

    [Theory]
    [InlineData(0x000000, 0x000001)]
    [InlineData(0x0000FF, 0x000100)]
    [InlineData(0x12FFFF, 0x130000)]
    [InlineData(0xFFFFFF, 0x000000)]
    public void P11_Inc24(int value, int result)
    {
        var k = new WorkbookRig("p11-inc24");
        k.Poke(0x10, (byte)value, (byte)(value >> 8), (byte)(value >> 16));
        k.RunToBrk();
        Assert.Equal(result, k.Word(0x10) | k[0x12] << 16);
    }

    [Theory]
    [InlineData(1, 0xFFFF)]
    [InlineData(0x0100, 0xFF00)]
    [InlineData(0, 0)]
    [InlineData(0x8000, 0x8000)]
    [InlineData(1000, 0x10000 - 1000)]
    public void P12_Negate16(int value, int result)
    {
        var k = new WorkbookRig("p12-negate16");
        k.Poke(0x10, (byte)value, (byte)(value >> 8));
        k.RunToBrk();
        Assert.Equal(result, k.Word(0x12));
    }

    [Theory]
    [InlineData(3, 9, 9)]
    [InlineData(200, 9, 200)]
    [InlineData(7, 7, 7)]
    public void P13_Bigger(byte a, byte b, byte max)
    {
        var k = new WorkbookRig("p13-bigger");
        k.Poke(0x10, a, b);
        k.RunToBrk();
        Assert.Equal(max, k[0x12]);
    }

    [Fact]
    public void P14_Tables()
    {
        var k = new WorkbookRig("p14-tables");
        k.RunToBrk();
        Assert.Equal(Enumerable.Range(0, 64).Select(i => (byte)i), k.Bytes(0x300, 64));
        Assert.Equal(Enumerable.Range(0, 64).Select(i => (byte)(63 - i)), k.Bytes(0x340, 64));
        Assert.Equal(0, k[0x380]);
    }

    [Fact]
    public void P15_Sum()
    {
        var k = new WorkbookRig("p15-sum");
        var data = Enumerable.Range(0, 32).Select(i => (byte)(i * 37 + 200)).ToArray();
        k.Poke(0x300, data);
        k.Poke(0x10, 0x55, 0x55);
        k.RunToBrk();
        Assert.Equal(data.Sum(b => b), k.Word(0x10));
    }

    [Theory]
    [InlineData(new byte[] { 42 }, 42, 42)]
    [InlineData(new byte[] { 5, 200, 1, 99, 255, 0, 7 }, 0, 255)]
    [InlineData(new byte[] { 9, 8, 7, 10, 3 }, 3, 10)]
    public void P16_MinMax(byte[] list, byte min, byte max)
    {
        var k = new WorkbookRig("p16-minmax");
        k[0x300] = (byte)list.Length;
        k.Poke(0x301, list);
        k.RunToBrk();
        Assert.Equal(new[] { min, max }, k.Bytes(0x10, 2));
    }

    [Theory]
    [InlineData(0x5A)]
    [InlineData(0xC3)]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    public void P17_Masks(byte value)
    {
        var k = new WorkbookRig("p17-masks");
        k[0x10] = value;
        k.RunToBrk();
        Assert.Equal(
            new[] { value, (byte)(value & 0x0F), (byte)(value | 0x81), (byte)~value, (byte)((value >> 6) & 1) },
            k.Bytes(0x10, 5));
    }

    [Fact]
    public void P18_Popcount()
    {
        for (int v = 0; v < 256; v++)
        {
            var k = new WorkbookRig("p18-popcount");
            k[0x10] = (byte)v;
            k.RunToBrk();
            Assert.Equal(System.Numerics.BitOperations.PopCount((uint)v), k[0x11]);
            Assert.Equal(v, k[0x10]);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(12, 13)]
    [InlineData(255, 255)]
    [InlineData(200, 3)]
    [InlineData(3, 200)]
    [InlineData(128, 2)]
    public void P19_Multiply(int a, int b)
    {
        var k = new WorkbookRig("p19-multiply");
        k.Poke(0x10, (byte)a, (byte)b);
        k.RunToBrk();
        Assert.Equal(a * b, k.Word(0x12));
    }

    [Theory]
    [InlineData(0x3C, "3C")]
    [InlineData(0x00, "00")]
    [InlineData(0xFF, "FF")]
    [InlineData(0x9A, "9A")]
    [InlineData(0xA9, "A9")]
    public void P20_HexAscii(byte value, string text)
    {
        var k = new WorkbookRig("p20-hex-ascii");
        k[0x10] = value;
        k.RunToBrk();
        Assert.Equal(text, System.Text.Encoding.ASCII.GetString(k.Bytes(0x11, 2)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(200)]
    public void P21_BlockCopy(int count)
    {
        var k = new WorkbookRig("p21-block-copy");
        var data = Enumerable.Range(0, 200).Select(i => (byte)(i ^ 0xA5)).ToArray();
        k.Poke(0x338, data);
        k.Poke(0x10, 0x38, 0x03, 0x40, 0x02, (byte)count); // $0338 -> $0240 (crosses a page)
        k.RunToBrk();
        Assert.Equal(data.Take(count), k.Bytes(0x240, count));
        Assert.Equal(0, k[0x240 + count]);
    }

    [Theory]
    [InlineData("Hello, KIM-1!", "HELLO, KIM-1!")]
    [InlineData("", "")]
    [InlineData("az{`@AZ", "AZ{`@AZ")]
    public void P22_Shout(string input, string output)
    {
        var k = new WorkbookRig("p22-shout");
        k.Poke(0x300, [.. System.Text.Encoding.ASCII.GetBytes(input), 0]);
        k.Poke(0x10, 0x00, 0x03);
        k.RunToBrk();
        Assert.Equal(output, System.Text.Encoding.ASCII.GetString(k.Bytes(0x300, input.Length)));
        Assert.Equal(input.Length, k[0x12]);
    }

    [Theory]
    [InlineData(0x1234, 0x4321, 0x5555, 0)]
    [InlineData(0x0999, 0x0001, 0x1000, 0)]
    [InlineData(0x9999, 0x0001, 0x0000, 1)]
    [InlineData(0x5678, 0x6789, 0x2467, 1)]
    public void P23_BcdAdd(int a, int b, int sum, byte carry)
    {
        var k = new WorkbookRig("p23-bcd-add");
        k.Poke(0x10, (byte)a, (byte)(a >> 8), (byte)b, (byte)(b >> 8));
        k.RunToBrk();
        Assert.Equal(sum, k.Word(0x14));
        Assert.Equal(carry, k[0x16]);
        Assert.Equal(0, k.Board.Cpu.P & 0x08); // decimal flag cleared again
    }

    [Fact]
    public void P24_BinaryToBcd()
    {
        for (int v = 0; v < 256; v++)
        {
            var k = new WorkbookRig("p24-bin-bcd");
            k[0x10] = (byte)v;
            k.RunToBrk();
            Assert.Equal(v / 100, k[0x11]);
            Assert.Equal((v / 10 % 10) << 4 | v % 10, k[0x12]);
        }
    }

    [Theory]
    [InlineData(new byte[] { 2, 1 })]
    [InlineData(new byte[] { 5, 3, 9, 1, 3, 255, 0, 128, 7 })]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    public void P25_Sort(byte[] list)
    {
        var k = new WorkbookRig("p25-sort");
        k[0x300] = (byte)list.Length;
        k.Poke(0x301, list);
        k.RunToBrk();
        Assert.Equal(list.Order(), k.Bytes(0x301, list.Length));
    }

    [Fact]
    public void P25_SortLongList()
    {
        var k = new WorkbookRig("p25-sort");
        var list = Enumerable.Range(0, 255).Select(i => (byte)(i * 97 % 256)).ToArray();
        k[0x300] = 255;
        k.Poke(0x301, list);
        k.RunToBrk(maxCycles: 50_000_000);
        Assert.Equal(list.Order(), k.Bytes(0x301, 255));
    }
}
