using System.Text;
using Kimulator.Core.Terminal;

namespace Kimulator.Tests.Formats;

public class TeletypeBufferTests
{
    private static string Print(string text, int maxLines = 100)
    {
        var buffer = new TeletypeBuffer(maxLines);
        buffer.Write(Encoding.ASCII.GetBytes(text));
        return buffer.ToString();
    }

    [Fact]
    public void CrLfStartsANewLine() => Assert.Equal("\nKIM\n0000 00 ", Print("\r\n\0\0KIM\r\n\0\00000 00 "));

    [Fact]
    public void CrAloneOverprints() => Assert.Equal("XBC", Print("ABC\rX"));

    [Fact]
    public void LfAloneKeepsTheColumn() => Assert.Equal("AB\n  C", Print("AB\nC"));

    [Fact]
    public void BackspaceOverprints() => Assert.Equal("AX", Print("AB\bX"));

    [Fact]
    public void NonPrintingCharactersAreDropped() => Assert.Equal("KIM", Print("\x13K\x7FI\x11M\a"));

    [Fact]
    public void KeepsOnlyTheLastLines() => Assert.Equal("3\n4", Print("1\r\n2\r\n3\r\n4", maxLines: 2));
}
