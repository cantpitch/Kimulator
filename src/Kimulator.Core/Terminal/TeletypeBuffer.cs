using System.Text;

namespace Kimulator.Core.Terminal;

/// <summary>
/// Text as printed by a teletype: CR returns the carriage without feeding paper, LF feeds paper
/// without moving the carriage, BS backs up one column, and printing over a column replaces it.
/// Non-printing characters (NUL padding, RUBOUT, X-ON/X-OFF, BEL) are dropped.
/// </summary>
public sealed class TeletypeBuffer(int maxLines = 2000)
{
    private readonly List<StringBuilder> _lines = [new()];
    private int _column;

    public int LineCount => _lines.Count;

    /// <summary>Increments whenever the content changes; cheap change detection for views.</summary>
    public long Version { get; private set; }

    public void Write(byte value)
    {
        char c = (char)(value & 0x7F);
        switch (c)
        {
            case '\r':
                _column = 0;
                break;
            case '\n':
                _lines.Add(new StringBuilder().Append(' ', _column));
                if (_lines.Count > maxLines) _lines.RemoveAt(0);
                break;
            case '\b':
                if (_column > 0) _column--;
                break;
            case '\t':
                _column = (_column / 8 + 1) * 8;
                break;
            default:
                if (c < ' ' || c == '\x7F') return;
                var line = _lines[^1];
                if (line.Length < _column) line.Append(' ', _column - line.Length);
                if (_column < line.Length) line[_column] = c;
                else line.Append(c);
                _column++;
                break;
        }

        Version++;
    }

    public void Write(ReadOnlySpan<byte> values)
    {
        foreach (byte b in values) Write(b);
    }

    public void Clear()
    {
        _lines.Clear();
        _lines.Add(new StringBuilder());
        _column = 0;
        Version++;
    }

    public override string ToString() => string.Join('\n', _lines);
}
