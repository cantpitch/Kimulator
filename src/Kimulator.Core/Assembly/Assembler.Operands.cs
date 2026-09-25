using Kimulator.Core.Cpu;

namespace Kimulator.Core.Assembly;

public sealed partial class Assembler
{
    private enum OperandForm { None, Accumulator, Immediate, Direct, DirectX, DirectY, IndexedIndirect, IndirectIndexed, Indirect }

    /// <summary>The shape of an instruction operand, decided once from its tokens.</summary>
    private readonly record struct OperandSyntax(OperandForm Form, List<Token> Expression, bool ForceAbsolute, bool ForceZeroPage)
    {
        public string Describe() => Form switch
        {
            OperandForm.None => "implied",
            OperandForm.Accumulator => "accumulator",
            OperandForm.Immediate => "immediate (#)",
            OperandForm.Direct => "absolute/zero-page",
            OperandForm.DirectX => "indexed ,X",
            OperandForm.DirectY => "indexed ,Y",
            OperandForm.IndexedIndirect => "(zp,X)",
            OperandForm.IndirectIndexed => "(zp),Y",
            OperandForm.Indirect => "indirect ()",
            _ => "this",
        };

        public static OperandSyntax Parse(Mnemonic mnemonic, List<Token> tokens)
        {
            if (tokens.Count == 0) return new(OperandForm.None, [], false, false);

            if (tokens is [{ Kind: TokenKind.Identifier } only] && only.Text.Equals("A", StringComparison.OrdinalIgnoreCase)
                && mnemonic is Mnemonic.ASL or Mnemonic.LSR or Mnemonic.ROL or Mnemonic.ROR)
                return new(OperandForm.Accumulator, [], false, false);

            if (tokens[0].Kind == TokenKind.Hash)
            {
                if (tokens.Count == 1) throw new AssemblyException("Missing value after #.");
                return new(OperandForm.Immediate, tokens[1..], false, false);
            }

            // a:expr / z:expr force the operand size (ca65 style).
            bool forceAbs = false, forceZp = false;
            if (tokens.Count > 2 && tokens[0].Kind == TokenKind.Identifier && tokens[1].Kind == TokenKind.Colon)
            {
                string prefix = tokens[0].Text.ToLowerInvariant();
                forceAbs = prefix is "a" or "abs";
                forceZp = prefix is "z" or "zp";
                if (!forceAbs && !forceZp) throw new AssemblyException($"Unknown address size '{tokens[0].Text}:' (use a: or z:).");
                tokens = tokens[2..];
            }

            if (tokens[0].Kind == TokenKind.LParen && Matching(tokens, 0) is int close)
            {
                var inner = tokens[1..close];
                // (expr,X)
                if (close == tokens.Count - 1 && inner.Count >= 3 && IsRegister(inner[^1], "X") && inner[^2].Kind == TokenKind.Comma)
                    return new(OperandForm.IndexedIndirect, inner[..^2], forceAbs, forceZp);
                // (expr),Y
                if (close == tokens.Count - 3 && tokens[^2].Kind == TokenKind.Comma && IsRegister(tokens[^1], "Y"))
                    return new(OperandForm.IndirectIndexed, inner, forceAbs, forceZp);
                // (expr) is indirect only for JMP; elsewhere the parentheses just group the expression.
                if (close == tokens.Count - 1 && mnemonic == Mnemonic.JMP)
                    return new(OperandForm.Indirect, inner, forceAbs, forceZp);
            }

            if (tokens.Count >= 3 && tokens[^2].Kind == TokenKind.Comma)
            {
                if (IsRegister(tokens[^1], "X")) return new(OperandForm.DirectX, tokens[..^2], forceAbs, forceZp);
                if (IsRegister(tokens[^1], "Y")) return new(OperandForm.DirectY, tokens[..^2], forceAbs, forceZp);
                throw new AssemblyException($"Only ,X or ,Y can follow an address, not ,{tokens[^1].Text}.");
            }

            return new(OperandForm.Direct, tokens, forceAbs, forceZp);
        }

        private static bool IsRegister(Token t, string name) =>
            t.Kind == TokenKind.Identifier && t.Text.Equals(name, StringComparison.OrdinalIgnoreCase);

        private static int? Matching(List<Token> tokens, int open)
        {
            int depth = 0;
            for (int i = open; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.LParen) depth++;
                else if (tokens[i].Kind == TokenKind.RParen && --depth == 0) return i;
            }

            throw new AssemblyException("Unbalanced parentheses.");
        }
    }

    /// <summary>
    /// Evaluates expressions for one statement. Returns null when a symbol isn't known yet
    /// (a forward reference on an early pass, or an undefined symbol).
    /// Precedence, lowest first: | ^ &amp; (&lt;&lt; &gt;&gt;) (+ -) (* / %) unary(- ~ &lt; &gt; !).
    /// </summary>
    private sealed class EvalContext(Assembler asm, int pc, int statementIndex, string scope)
    {
        private List<Token> _tokens = [];
        private int _pos;

        public int Pc { get; } = pc;

        public List<string> UnresolvedNames { get; } = [];

        public int? Evaluate(List<Token> tokens)
        {
            if (tokens.Count == 0) throw new AssemblyException("Missing value.");
            _tokens = tokens;
            _pos = 0;
            int? value = Binary(0);
            if (_pos < _tokens.Count) throw new AssemblyException($"Unexpected '{_tokens[_pos].Text}' in expression.");
            return value;
        }

        private static readonly string[][] Levels =
        [
            ["|"],
            ["^"],
            ["&"],
            ["<<", ">>"],
            ["+", "-"],
            ["*", "/", "%"],
        ];

        private int? Binary(int level)
        {
            if (level == Levels.Length) return Unary();
            int? left = Binary(level + 1);
            while (_pos < _tokens.Count && _tokens[_pos].Kind == TokenKind.Operator && Levels[level].Contains(_tokens[_pos].Text))
            {
                string op = _tokens[_pos++].Text;
                int? right = Binary(level + 1);
                if (left is null || right is null)
                {
                    left = null;
                    continue;
                }

                left = op switch
                {
                    "|" => left | right,
                    "^" => left ^ right,
                    "&" => left & right,
                    "<<" => left << right,
                    ">>" => left >> right,
                    "+" => left + right,
                    "-" => left - right,
                    "*" => left * right,
                    "/" => right == 0 ? throw new AssemblyException("Division by zero.") : left / right,
                    _ => right == 0 ? throw new AssemblyException("Division by zero.") : left % right,
                };
            }

            return left;
        }

        private int? Unary()
        {
            if (_pos >= _tokens.Count) throw new AssemblyException("Expression ends unexpectedly.");
            var t = _tokens[_pos];
            if (t.Kind == TokenKind.Operator && t.Text is "-" or "~" or "<" or ">" or "!" or "+")
            {
                _pos++;
                int? v = Unary();
                return v is null ? null : t.Text switch
                {
                    "-" => -v,
                    "~" => ~v & 0xFFFF,
                    "<" => v & 0xFF,
                    ">" => (v >> 8) & 0xFF,
                    "!" => v == 0 ? 1 : 0,
                    _ => v,
                };
            }

            return Primary();
        }

        private int? Primary()
        {
            var t = _tokens[_pos++];
            switch (t.Kind)
            {
                case TokenKind.Number:
                    return t.Value;
                case TokenKind.String when t.Text.Length == 1:
                    return t.Text[0];
                case TokenKind.Operator when t.Text == "*":
                    return Pc;
                case TokenKind.LParen:
                {
                    int? v = Binary(0);
                    if (_pos >= _tokens.Count || _tokens[_pos].Kind != TokenKind.RParen) throw new AssemblyException("Missing ')'.");
                    _pos++;
                    return v;
                }
                case TokenKind.AnonymousRef:
                    return Anonymous(t.Value);
                case TokenKind.Identifier:
                    return Lookup(t.Text);
                default:
                    throw new AssemblyException($"Unexpected '{t.Text}' in expression.");
            }
        }

        private int? Lookup(string name)
        {
            string key = name.StartsWith('@') ? scope + name : name;
            if (asm._symbols.TryGetValue(key, out var symbol) && symbol.Value is not null) return symbol.Value;
            if (!name.StartsWith('@') && asm._options.PredefinedSymbols?.TryGetAddress(name, out ushort predefined) == true) return predefined;
            UnresolvedNames.Add(name);
            return null;
        }

        private int? Anonymous(int offset)
        {
            // Backward refs use labels already placed this pass; forward refs use the previous pass's positions.
            if (offset < 0)
            {
                var before = asm._anonymous.Where(a => a.Statement <= statementIndex).ToList();
                int i = before.Count + offset;
                if (i >= 0) return before[i].Address;
            }
            else
            {
                var after = asm._previousAnonymous.Where(a => a.Statement > statementIndex).ToList();
                if (offset - 1 < after.Count) return after[offset - 1].Address;
            }

            UnresolvedNames.Add(offset < 0 ? ":" + new string('-', -offset) : ":" + new string('+', offset));
            return null;
        }
    }
}
