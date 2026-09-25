namespace Kimulator.Core.Assembly;

internal enum TokenKind
{
    Identifier, // mnemonics, directives (.byte), symbols, @locals
    Number,
    String,     // "text"
    Operator,   // + - * / % & | ^ ~ < > << >> ! =  :=
    Hash,       // #
    Comma,
    LParen,
    RParen,
    Colon,
    AnonymousRef, // :+ :++ :- :--  (Value = +n / -n)
}

internal readonly record struct Token(TokenKind Kind, string Text, int Value, int Column)
{
    public bool Is(TokenKind kind, string text) =>
        Kind == kind && string.Equals(Text, text, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Text;
}

/// <summary>Splits one source line (comment already removed) into tokens.</summary>
internal static class Lexer
{
    public static List<Token> Tokenize(string line, out string? error)
    {
        error = null;
        var tokens = new List<Token>();
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            int start = i;
            if (char.IsLetter(c) || c is '_' or '.' or '@')
            {
                i++;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] is '_' or '.' or '@')) i++;
                tokens.Add(new Token(TokenKind.Identifier, line[start..i], 0, start));
            }
            else if (char.IsDigit(c))
            {
                int radix = 10;
                if (c == '0' && i + 1 < line.Length && line[i + 1] is 'x' or 'X')
                {
                    radix = 16;
                    i += 2;
                }

                int digitsStart = i;
                while (i < line.Length && IsDigit(line[i], radix)) i++;
                if (!TryParse(line[digitsStart..i], radix, out int value, out error)) return tokens;
                tokens.Add(new Token(TokenKind.Number, line[start..i], value, start));
            }
            else if (c == '$' && i + 1 < line.Length && IsDigit(line[i + 1], 16))
            {
                i++;
                while (i < line.Length && IsDigit(line[i], 16)) i++;
                if (!TryParse(line[(start + 1)..i], 16, out int value, out error)) return tokens;
                tokens.Add(new Token(TokenKind.Number, line[start..i], value, start));
            }
            else if (c == '%' && i + 1 < line.Length && line[i + 1] is '0' or '1' && !EndsValue(tokens))
            {
                i++;
                while (i < line.Length && line[i] is '0' or '1') i++;
                if (!TryParse(line[(start + 1)..i], 2, out int value, out error)) return tokens;
                tokens.Add(new Token(TokenKind.Number, line[start..i], value, start));
            }
            else if (c == '\'')
            {
                // 'A' (the closing quote is optional, as in ca65)
                if (i + 1 >= line.Length)
                {
                    error = "Character constant is empty.";
                    return tokens;
                }

                int value = line[i + 1];
                i += 2;
                if (i < line.Length && line[i] == '\'') i++;
                tokens.Add(new Token(TokenKind.Number, line[start..i], value, start));
            }
            else if (c == '"')
            {
                int end = line.IndexOf('"', i + 1);
                if (end < 0)
                {
                    error = "String is missing its closing quote.";
                    return tokens;
                }

                tokens.Add(new Token(TokenKind.String, line[(i + 1)..end], 0, start));
                i = end + 1;
            }
            else if (c == ':' && i + 1 < line.Length && line[i + 1] is '+' or '-')
            {
                char dir = line[i + 1];
                i++;
                int count = 0;
                while (i < line.Length && line[i] == dir)
                {
                    count++;
                    i++;
                }

                tokens.Add(new Token(TokenKind.AnonymousRef, line[start..i], dir == '+' ? count : -count, start));
            }
            else
            {
                i++;
                var (kind, text) = c switch
                {
                    '#' => (TokenKind.Hash, "#"),
                    ',' => (TokenKind.Comma, ","),
                    '(' => (TokenKind.LParen, "("),
                    ')' => (TokenKind.RParen, ")"),
                    ':' when i < line.Length && line[i] == '=' => (TokenKind.Operator, ":="),
                    ':' => (TokenKind.Colon, ":"),
                    '<' when i < line.Length && line[i] == '<' => (TokenKind.Operator, "<<"),
                    '>' when i < line.Length && line[i] == '>' => (TokenKind.Operator, ">>"),
                    '+' or '-' or '*' or '/' or '%' or '&' or '|' or '^' or '~' or '<' or '>' or '!' or '=' => (TokenKind.Operator, c.ToString()),
                    _ => (TokenKind.Operator, "\0"),
                };

                if (text == "\0")
                {
                    error = $"Unexpected character '{c}'.";
                    return tokens;
                }

                if (text.Length == 2) i++;
                tokens.Add(new Token(kind, text, 0, start));
            }
        }

        return tokens;
    }

    /// <summary>Removes a ';' comment, ignoring semicolons inside strings and character constants.</summary>
    public static string StripComment(string line)
    {
        bool inString = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') inString = !inString;
            else if (c == '\'' && !inString) i++; // skip the quoted character
            else if (c == ';' && !inString) return line[..i];
        }

        return line;
    }

    private static bool EndsValue(List<Token> tokens) =>
        tokens.Count > 0 && tokens[^1].Kind is TokenKind.Number or TokenKind.Identifier or TokenKind.RParen or TokenKind.AnonymousRef;

    private static bool IsDigit(char c, int radix) => radix switch
    {
        16 => char.IsAsciiHexDigit(c),
        2 => c is '0' or '1',
        _ => char.IsAsciiDigit(c),
    };

    private static bool TryParse(string digits, int radix, out int value, out string? error)
    {
        error = null;
        value = 0;
        if (digits.Length == 0)
        {
            error = "Number has no digits.";
            return false;
        }

        try
        {
            long v = Convert.ToInt64(digits, radix);
            if (v > 0xFFFFFF) throw new OverflowException();
            value = (int)v;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            error = $"Invalid number '{digits}'.";
            return false;
        }
    }
}
