using Kimulator.Core.Cpu;
using Kimulator.Core.Debugging;
using Kimulator.Core.Formats;

namespace Kimulator.Core.Assembly;

/// <summary>
/// A 6502 assembler with ca65/64tass-style syntax:
/// <code>
///         .org $0200          ; or  * = $0200
/// COUNT   = 10                ; constants: name = expr
/// start:  ldx #COUNT          ; labels with or without ':' (without only in column 1)
/// @loop:  dex                 ; @local labels are scoped to the previous normal label
///         bne @loop
/// :       jmp :-              ; anonymous labels, referenced as :- :-- :+ :++
///         lda #&lt;msg           ; &lt; low byte, &gt; high byte, * current address
///         jsr OUTCH           ; KIM-1 monitor symbols are predefined
/// msg:    .byte "HI", $0D, 0  ; .byte .word .dbyt .res .text .asciiz .align .end
/// </code>
/// Symbols are case-insensitive. Zero-page addressing is chosen automatically when the operand is
/// known to be below $100 on the first pass; <c>a:</c> / <c>z:</c> force absolute / zero page.
/// Undocumented NMOS opcodes (LAX, SAX, DCP, ISC, SLO, RLA, SRE, RRA, ANC, ALR, ARR, SBX, ...) are accepted.
/// </summary>
public sealed partial class Assembler
{
    private const int MaxPasses = 16;

    private static readonly Dictionary<string, Mnemonic> Mnemonics = BuildMnemonics();
    private static readonly Dictionary<(Mnemonic, AddressingMode), byte> OpcodeMap = BuildOpcodeMap();

    private readonly AssemblerOptions _options;
    private readonly List<Statement> _statements = [];
    private readonly List<AssemblyError> _parseErrors = [];
    private readonly Dictionary<string, Symbol> _symbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _lines;

    private List<AssemblyError> _passErrors = [];
    private List<(int Statement, int Address)> _anonymous = [];
    private List<(int Statement, int Address)> _previousAnonymous = [];
    private HashSet<string> _definedThisPass = new(StringComparer.OrdinalIgnoreCase);
    private bool _changed;
    private bool _finalPass;
    private int _pass;

    private Assembler(string source, AssemblerOptions options)
    {
        _options = options;
        _lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    public static AssemblyResult Assemble(string source, AssemblerOptions? options = null) =>
        new Assembler(source, options ?? new AssemblerOptions()).Run();

    /// <summary>True if <paramref name="word"/> is an instruction mnemonic this assembler knows.</summary>
    public static bool IsMnemonic(string word) => Mnemonics.ContainsKey(word);

    public static IEnumerable<string> MnemonicNames => Mnemonics.Keys;

    public static IReadOnlyList<string> Directives { get; } =
        [".org", ".byte", ".byt", ".db", ".word", ".addr", ".dw", ".dbyt", ".res", ".ds", ".text", ".asciiz", ".align", ".end", ".equ", ".set"];

    private AssemblyResult Run()
    {
        ParseAll();

        do
        {
            _pass++;
            RunPass();
        }
        while ((_pass == 1 || _changed) && _pass < MaxPasses);

        _finalPass = true;
        var image = new byte[0x10000];
        var written = new bool[0x10000];
        var listing = RunPass(image, written);

        var errors = _parseErrors.Concat(_passErrors).OrderBy(e => e.Line).ToList();
        if (_changed) errors.Add(new AssemblyError(0, "Symbol values did not settle (circular definitions?)."));

        var symbols = new SymbolTable();
        foreach (var (name, symbol) in _symbols.OrderBy(s => s.Value.Value))
        {
            if (symbol.Value is { } v && !name.Contains('@')) symbols.Add(name, (ushort)v);
        }

        var lineAddresses = new Dictionary<int, ushort>();
        foreach (var st in _statements.Where(s => s.Kind is StatementKind.Instruction or StatementKind.Data && s.Size > 0))
            lineAddresses.TryAdd(st.Line, st.Address);

        return new AssemblyResult
        {
            Errors = errors,
            Segments = errors.Count == 0 ? Segments(image, written) : [],
            Symbols = symbols,
            Listing = listing,
            LineAddresses = lineAddresses,
        };
    }

    private static List<MemorySegment> Segments(byte[] image, bool[] written)
    {
        var segments = new List<MemorySegment>();
        int a = 0;
        while (a < 0x10000)
        {
            if (!written[a])
            {
                a++;
                continue;
            }

            int start = a;
            while (a < 0x10000 && written[a]) a++;
            segments.Add(new MemorySegment((ushort)start, image[start..a]));
        }

        return segments;
    }

    // ---------------------------------------------------------------- parsing

    private enum StatementKind { None, Instruction, Data, Constant, Org, End }

    private sealed class Statement
    {
        public int Line;
        public string? Label;
        public bool Anonymous;
        public StatementKind Kind;
        public string Op = "";
        public Mnemonic Mnemonic;
        public List<Token> Operand = [];
        public OperandSyntax Syntax;

        // Decided on the first pass, then fixed so later passes can't oscillate.
        public AddressingMode? Mode;
        public int Size;
        public ushort Address;
    }

    private void ParseAll()
    {
        for (int n = 0; n < _lines.Length; n++)
        {
            string text = Lexer.StripComment(_lines[n]);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var tokens = Lexer.Tokenize(text, out string? error);
            if (error is not null)
            {
                _parseErrors.Add(new AssemblyError(n + 1, error));
                continue;
            }

            try
            {
                if (ParseStatement(n + 1, text, tokens) is { } statement) _statements.Add(statement);
            }
            catch (AssemblyException ex)
            {
                _parseErrors.Add(new AssemblyError(n + 1, ex.Message));
            }
        }
    }

    private Statement? ParseStatement(int line, string text, List<Token> tokens)
    {
        var st = new Statement { Line = line };
        int i = 0;

        // Anonymous label ":"
        if (tokens[0].Kind == TokenKind.Colon)
        {
            st.Anonymous = true;
            i = 1;
        }
        // "name:" label
        else if (tokens.Count >= 2 && tokens[0].Kind == TokenKind.Identifier && tokens[1].Kind == TokenKind.Colon && !tokens[0].Text.StartsWith('.'))
        {
            st.Label = tokens[0].Text;
            i = 2;
        }
        // "name = expr", "name := expr", "name .equ expr"
        else if (tokens.Count >= 2 && tokens[0].Kind == TokenKind.Identifier
                 && (tokens[1].Is(TokenKind.Operator, "=") || tokens[1].Is(TokenKind.Operator, ":=")
                     || tokens[1].Is(TokenKind.Identifier, ".equ") || tokens[1].Is(TokenKind.Identifier, ".set")))
        {
            st.Kind = StatementKind.Constant;
            st.Op = tokens[0].Text;
            st.Operand = tokens[2..];
            if (st.Operand.Count == 0) throw new AssemblyException($"Missing value for {st.Op}.");
            return st;
        }
        // "* = expr"
        else if (tokens.Count >= 2 && tokens[0].Is(TokenKind.Operator, "*") && tokens[1].Is(TokenKind.Operator, "="))
        {
            st.Kind = StatementKind.Org;
            st.Operand = tokens[2..];
            return st;
        }
        // A label without ':' is allowed in column 1 when followed by an instruction/directive or nothing.
        else if (tokens[0].Kind == TokenKind.Identifier && !char.IsWhiteSpace(text[0]) && !tokens[0].Text.StartsWith('.')
                 && !IsMnemonic(tokens[0].Text)
                 && (tokens.Count == 1 || tokens[1].Kind == TokenKind.Identifier))
        {
            st.Label = tokens[0].Text;
            i = 1;
        }

        if (i >= tokens.Count) return st; // label only

        var head = tokens[i];
        if (head.Kind != TokenKind.Identifier)
            throw new AssemblyException($"Expected an instruction or directive, found '{head.Text}'.");

        st.Operand = tokens[(i + 1)..];
        if (head.Text.StartsWith('.'))
        {
            st.Op = head.Text.ToLowerInvariant();
            st.Kind = st.Op switch
            {
                ".org" => StatementKind.Org,
                ".end" => StatementKind.End,
                ".byte" or ".byt" or ".db" or ".word" or ".addr" or ".dw" or ".dbyt" or ".res" or ".ds"
                    or ".text" or ".asciiz" or ".align" => StatementKind.Data,
                ".include" => throw new AssemblyException(".include is not supported; put everything in one file."),
                _ => throw new AssemblyException($"Unknown directive {head.Text}."),
            };
            return st;
        }

        if (!Mnemonics.TryGetValue(head.Text, out var mnemonic))
            throw new AssemblyException($"Unknown instruction '{head.Text}'.");

        st.Kind = StatementKind.Instruction;
        st.Op = head.Text.ToUpperInvariant();
        st.Mnemonic = mnemonic;
        st.Syntax = OperandSyntax.Parse(mnemonic, st.Operand);
        return st;
    }

    // ---------------------------------------------------------------- passes

    private sealed class Symbol
    {
        public int? Value;
        public int Line;
    }

    private List<ListingLine> RunPass(byte[]? image = null, bool[]? written = null)
    {
        _changed = false;
        _passErrors = [];
        _definedThisPass = new(StringComparer.OrdinalIgnoreCase);
        _previousAnonymous = _anonymous;
        _anonymous = [];

        var listing = new List<ListingLine>();
        var bytesByLine = new Dictionary<int, List<byte>>();
        var addressByLine = new Dictionary<int, ushort>();

        int pc = _options.DefaultOrigin;
        string scope = "";

        for (int index = 0; index < _statements.Count; index++)
        {
            var st = _statements[index];
            st.Address = (ushort)pc;
            addressByLine.TryAdd(st.Line, (ushort)pc);

            try
            {
                if (st.Anonymous) _anonymous.Add((index, pc));
                if (st.Label is { } label)
                {
                    if (!label.StartsWith('@')) scope = label;
                    Define(Qualify(label, scope), pc, st.Line);
                }

                var ctx = new EvalContext(this, pc, index, scope);
                switch (st.Kind)
                {
                    case StatementKind.Constant:
                    {
                        int? value = ctx.Evaluate(st.Operand);
                        if (value is not null) Define(Qualify(st.Op, scope), value.Value, st.Line);
                        else Unresolved(st.Operand, ctx);
                        break;
                    }
                    case StatementKind.Org:
                    {
                        int? value = ctx.Evaluate(st.Operand);
                        if (value is null) Unresolved(st.Operand, ctx, "The origin must be known");
                        else if (value is < 0 or > 0xFFFF) throw new AssemblyException($"Origin ${value:X} is outside $0000-$FFFF.");
                        else pc = value.Value;
                        st.Address = (ushort)pc;
                        addressByLine[st.Line] = (ushort)pc;
                        break;
                    }
                    case StatementKind.End:
                        index = _statements.Count;
                        break;
                    case StatementKind.Instruction:
                    {
                        var bytes = AssembleInstruction(st, ctx);
                        pc = Emit(bytes, pc, st, image, written, bytesByLine);
                        break;
                    }
                    case StatementKind.Data:
                    {
                        var bytes = AssembleData(st, ctx, out int size);
                        if (bytes is null) pc += size; // reserved space: advance without writing
                        else pc = Emit(bytes, pc, st, image, written, bytesByLine);
                        st.Size = size;
                        break;
                    }
                }
            }
            catch (AssemblyException ex)
            {
                _passErrors.Add(new AssemblyError(st.Line, ex.Message));
            }

            if (pc > 0x10000)
            {
                _passErrors.Add(new AssemblyError(st.Line, "Program runs past $FFFF."));
                break;
            }
        }

        if (image is null) return listing;

        for (int n = 0; n < _lines.Length; n++)
        {
            int line = n + 1;
            listing.Add(new ListingLine(line, addressByLine.TryGetValue(line, out var a) ? a : null,
                bytesByLine.TryGetValue(line, out var b) ? [.. b] : [], _lines[n]));
        }

        return listing;
    }

    private static int Emit(byte[] bytes, int pc, Statement st, byte[]? image, bool[]? written, Dictionary<int, List<byte>> byLine)
    {
        if (image is not null && written is not null)
        {
            if (!byLine.TryGetValue(st.Line, out var list)) byLine[st.Line] = list = [];
            for (int i = 0; i < bytes.Length && pc + i <= 0xFFFF; i++)
            {
                image[pc + i] = bytes[i];
                written[pc + i] = true;
                list.Add(bytes[i]);
            }
        }

        return pc + bytes.Length;
    }

    private void Define(string name, int value, int line)
    {
        if (!_definedThisPass.Add(name))
        {
            if (_finalPass) _passErrors.Add(new AssemblyError(line, $"'{name}' is defined more than once (first on line {_symbols[name].Line})."));
            return;
        }

        if (_symbols.TryGetValue(name, out var existing))
        {
            if (existing.Value != value) _changed = true;
            existing.Value = value;
            existing.Line = line;
        }
        else
        {
            _symbols[name] = new Symbol { Value = value, Line = line };
            _changed = true;
        }
    }

    private void Unresolved(List<Token> expression, EvalContext ctx, string? what = null)
    {
        if (!_finalPass) return;
        string names = string.Join(", ", ctx.UnresolvedNames.Distinct());
        throw new AssemblyException(names.Length > 0
            ? $"Undefined symbol{(ctx.UnresolvedNames.Distinct().Count() > 1 ? "s" : "")}: {names}."
            : $"{what ?? "Value"} could not be resolved.");
    }

    private static string Qualify(string name, string scope) => name.StartsWith('@') ? scope + name : name;

    // ---------------------------------------------------------------- instructions

    private byte[] AssembleInstruction(Statement st, EvalContext ctx)
    {
        var syntax = st.Syntax;
        int? value = syntax.Expression.Count > 0 ? ctx.Evaluate(syntax.Expression) : null;
        bool known = value is not null;

        if (st.Mode is null)
        {
            st.Mode = ChooseMode(st, syntax, value);
            st.Size = 1 + OperandSize(st.Mode.Value);
        }

        var mode = st.Mode.Value;
        byte opcode = OpcodeMap[(st.Mnemonic, mode)];
        if (!known)
        {
            if (syntax.Expression.Count > 0) Unresolved(syntax.Expression, ctx);
            value = 0;
        }

        int v = value!.Value;
        switch (mode)
        {
            case AddressingMode.Implied or AddressingMode.Accumulator:
                return [opcode];
            case AddressingMode.Relative:
            {
                int offset = v - (ctx.Pc + 2);
                if (known && offset is < -128 or > 127)
                    throw new AssemblyException($"Branch target ${v:X4} is out of range ({offset} bytes; the limit is -128..127).");
                return [opcode, (byte)offset];
            }
            case AddressingMode.Immediate:
                if (known && v is < -128 or > 255) throw new AssemblyException($"Immediate value ${v:X} does not fit in a byte (use < or >).");
                return [opcode, (byte)v];
            case AddressingMode.ZeroPage or AddressingMode.ZeroPageX or AddressingMode.ZeroPageY
                or AddressingMode.IndexedIndirect or AddressingMode.IndirectIndexed:
                if (known && v is < 0 or > 0xFF) throw new AssemblyException($"${v:X} is not a zero-page address.");
                return [opcode, (byte)v];
            default:
                if (known && v is < 0 or > 0xFFFF) throw new AssemblyException($"${v:X} is outside $0000-$FFFF.");
                return [opcode, (byte)v, (byte)(v >> 8)];
        }
    }

    private static AddressingMode ChooseMode(Statement st, OperandSyntax syntax, int? value)
    {
        var m = st.Mnemonic;
        bool Has(AddressingMode mode) => OpcodeMap.ContainsKey((m, mode));

        AddressingMode Pick(AddressingMode zp, AddressingMode abs)
        {
            bool zeroPage = !syntax.ForceAbsolute && (syntax.ForceZeroPage || value is >= 0 and < 0x100);
            if (zeroPage && Has(zp)) return zp;
            if (Has(abs)) return abs;
            if (Has(zp)) return zp;
            throw NotAvailable();
        }

        AssemblyException NotAvailable() => new($"{st.Op} does not support {syntax.Describe()} addressing.");

        switch (syntax.Form)
        {
            case OperandForm.None:
                if (Has(AddressingMode.Implied)) return AddressingMode.Implied;
                if (Has(AddressingMode.Accumulator)) return AddressingMode.Accumulator;
                throw new AssemblyException($"{st.Op} needs an operand.");
            case OperandForm.Accumulator:
                return Has(AddressingMode.Accumulator) ? AddressingMode.Accumulator : throw NotAvailable();
            case OperandForm.Immediate:
                return Has(AddressingMode.Immediate) ? AddressingMode.Immediate : throw NotAvailable();
            case OperandForm.Direct:
                if (Has(AddressingMode.Relative)) return AddressingMode.Relative;
                return Pick(AddressingMode.ZeroPage, AddressingMode.Absolute);
            case OperandForm.DirectX:
                return Pick(AddressingMode.ZeroPageX, AddressingMode.AbsoluteX);
            case OperandForm.DirectY:
                return Pick(AddressingMode.ZeroPageY, AddressingMode.AbsoluteY);
            case OperandForm.IndexedIndirect:
                return Has(AddressingMode.IndexedIndirect) ? AddressingMode.IndexedIndirect : throw NotAvailable();
            case OperandForm.IndirectIndexed:
                return Has(AddressingMode.IndirectIndexed) ? AddressingMode.IndirectIndexed : throw NotAvailable();
            case OperandForm.Indirect:
                return Has(AddressingMode.Indirect) ? AddressingMode.Indirect : throw NotAvailable();
            default:
                throw NotAvailable();
        }
    }

    private static int OperandSize(AddressingMode mode) => mode switch
    {
        AddressingMode.Implied or AddressingMode.Accumulator => 0,
        AddressingMode.Absolute or AddressingMode.AbsoluteX or AddressingMode.AbsoluteY or AddressingMode.Indirect => 2,
        _ => 1,
    };

    // ---------------------------------------------------------------- data directives

    /// <summary>Returns the bytes to write, or null for reserved space that should not be written.</summary>
    private byte[]? AssembleData(Statement st, EvalContext ctx, out int size)
    {
        var items = SplitArguments(st.Operand);
        var bytes = new List<byte>();
        switch (st.Op)
        {
            case ".byte" or ".byt" or ".db" or ".text" or ".asciiz":
                if (items.Count == 0) throw new AssemblyException($"{st.Op} needs at least one value.");
                foreach (var item in items)
                {
                    if (item is [{ Kind: TokenKind.String } s])
                    {
                        bytes.AddRange(s.Text.Select(c => (byte)c));
                        continue;
                    }

                    int v = Value(item, ctx);
                    if (v is < -128 or > 255) throw new AssemblyException($"${v:X} does not fit in a byte.");
                    bytes.Add((byte)v);
                }

                if (st.Op == ".asciiz") bytes.Add(0);
                break;

            case ".word" or ".addr" or ".dw" or ".dbyt":
                if (items.Count == 0) throw new AssemblyException($"{st.Op} needs at least one value.");
                foreach (var item in items)
                {
                    int v = Value(item, ctx);
                    if (v is < -32768 or > 0xFFFF) throw new AssemblyException($"${v:X} does not fit in a word.");
                    if (st.Op == ".dbyt") bytes.AddRange([(byte)(v >> 8), (byte)v]);
                    else bytes.AddRange([(byte)v, (byte)(v >> 8)]);
                }

                break;

            case ".res" or ".ds":
            {
                if (items.Count is < 1 or > 2) throw new AssemblyException($"{st.Op} takes a count and an optional fill value.");
                int count = KnownValue(items[0], ctx, "The size of reserved space");
                if (count is < 0 or > 0x10000) throw new AssemblyException($"Invalid size {count}.");
                size = count;
                if (items.Count == 1) return null;
                int fill = Value(items[1], ctx);
                return Enumerable.Repeat((byte)fill, count).ToArray();
            }

            case ".align":
            {
                if (items.Count is < 1 or > 2) throw new AssemblyException(".align takes a boundary and an optional fill value.");
                int boundary = KnownValue(items[0], ctx, "The alignment");
                if (boundary <= 0) throw new AssemblyException("Alignment must be positive.");
                int count = (boundary - ctx.Pc % boundary) % boundary;
                size = count;
                if (items.Count == 1) return null;
                return Enumerable.Repeat((byte)Value(items[1], ctx), count).ToArray();
            }
        }

        size = bytes.Count;
        return [.. bytes];
    }

    private int Value(List<Token> expression, EvalContext ctx)
    {
        int? v = ctx.Evaluate(expression);
        if (v is null) Unresolved(expression, ctx);
        return v ?? 0;
    }

    private static int KnownValue(List<Token> expression, EvalContext ctx, string what) =>
        ctx.Evaluate(expression) ?? throw new AssemblyException($"{what} must not depend on labels defined later.");

    private static List<List<Token>> SplitArguments(List<Token> tokens)
    {
        var result = new List<List<Token>>();
        if (tokens.Count == 0) return result;
        var current = new List<Token>();
        int depth = 0;
        foreach (var t in tokens)
        {
            if (t.Kind == TokenKind.LParen) depth++;
            if (t.Kind == TokenKind.RParen) depth--;
            if (t.Kind == TokenKind.Comma && depth == 0)
            {
                if (current.Count == 0) throw new AssemblyException("Empty value in list.");
                result.Add(current);
                current = [];
                continue;
            }

            current.Add(t);
        }

        if (current.Count == 0) throw new AssemblyException("Empty value in list.");
        result.Add(current);
        return result;
    }

    // ---------------------------------------------------------------- tables

    private static Dictionary<string, Mnemonic> BuildMnemonics()
    {
        var map = new Dictionary<string, Mnemonic>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in Enum.GetValues<Mnemonic>()) map[m.ToString()] = m;
        // Common alternative names for undocumented opcodes.
        map["ISB"] = Mnemonic.ISC;
        map["INS"] = Mnemonic.ISC;
        map["DCM"] = Mnemonic.DCP;
        map["ASR"] = Mnemonic.ALR;
        map["LSE"] = Mnemonic.SRE;
        map["AAC"] = Mnemonic.ANC;
        map["XAA"] = Mnemonic.ANE;
        map["AXA"] = Mnemonic.SHA;
        map["AHX"] = Mnemonic.SHA;
        map["SHS"] = Mnemonic.TAS;
        map["LAR"] = Mnemonic.LAS;
        map["KIL"] = Mnemonic.JAM;
        map["HLT"] = Mnemonic.JAM;
        return map;
    }

    private static Dictionary<(Mnemonic, AddressingMode), byte> BuildOpcodeMap()
    {
        var map = new Dictionary<(Mnemonic, AddressingMode), byte>();
        foreach (var info in Opcodes.Table)
        {
            var key = (info.Mnemonic, info.Mode);
            // Prefer documented encodings (NOP $EA, SBC $E9), otherwise the first one in the table.
            if (!map.TryGetValue(key, out byte existing) || (Opcodes.Get(existing).Undocumented && !info.Undocumented))
                map[key] = info.Opcode;
        }

        // LAX #imm is the unstable $AB (LXA).
        map[(Mnemonic.LAX, AddressingMode.Immediate)] = 0xAB;
        return map;
    }
}

internal sealed class AssemblyException(string message) : Exception(message);
