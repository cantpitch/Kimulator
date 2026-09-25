using Kimulator.Core.Formats;

namespace Kimulator.Tests.Formats;

public class FormatTests
{
    private static readonly byte[] Sample = [.. Enumerable.Range(0, 60).Select(i => (byte)(i * 13))];

    [Fact]
    public void PaperTapeRoundTrips()
    {
        string tape = PaperTape.Write(0x0200, Sample);
        Assert.StartsWith(";180200", tape);
        Assert.EndsWith(";0000030003\r\n", tape);

        var segment = Assert.Single(PaperTape.Parse(tape));
        Assert.Equal(0x0200, segment.Address);
        Assert.Equal(Sample, segment.Data);
    }

    [Fact]
    public void PaperTapeParsesMonitorExample()
    {
        // Two bytes at $0000 and the final record, with the monitor's CR/LF and NUL padding.
        string tape = "\r\n\0\0;020000A9FF01AA\r\n\0\0;0000010001\r\n";
        var segment = Assert.Single(PaperTape.Parse(tape));
        Assert.Equal(new byte[] { 0xA9, 0xFF }, segment.Data);
    }

    [Fact]
    public void PaperTapeDetectsChecksumErrors()
    {
        string tape = PaperTape.Write(0x0200, Sample).Replace(";180200", ";180201");
        Assert.Throws<FormatException>(() => PaperTape.Parse(tape));
    }

    [Fact]
    public void IntelHexRoundTrips()
    {
        string hex = IntelHex.Write(0x1780, Sample);
        Assert.EndsWith(":00000001FF\r\n", hex);
        var segment = Assert.Single(IntelHex.Parse(hex));
        Assert.Equal(0x1780, segment.Address);
        Assert.Equal(Sample, segment.Data);
    }

    [Fact]
    public void IntelHexDetectsChecksumErrors()
    {
        Assert.Throws<FormatException>(() => IntelHex.Parse(":0300300002337A1F\n:00000001FF"));
        Assert.Single(IntelHex.Parse(":0300300002337A1E\n:00000001FF"));
    }
}
