namespace Kimulator.Core.Cpu;

public enum Mnemonic : byte
{
    // Documented
    ADC, AND, ASL, BCC, BCS, BEQ, BIT, BMI, BNE, BPL, BRK, BVC, BVS, CLC, CLD, CLI, CLV, CMP, CPX, CPY,
    DEC, DEX, DEY, EOR, INC, INX, INY, JMP, JSR, LDA, LDX, LDY, LSR, NOP, ORA, PHA, PHP, PLA, PLP, ROL,
    ROR, RTI, RTS, SBC, SEC, SED, SEI, STA, STX, STY, TAX, TAY, TSX, TXA, TXS, TYA,

    // Undocumented (NMOS)
    SLO, RLA, SRE, RRA, SAX, LAX, DCP, ISC, ANC, ALR, ARR, ANE, LXA, SBX, SHA, SHX, SHY, TAS, LAS, JAM,
}

public enum AddressingMode : byte
{
    Implied,
    Accumulator,
    Immediate,
    ZeroPage,
    ZeroPageX,
    ZeroPageY,
    Absolute,
    AbsoluteX,
    AbsoluteY,
    Indirect,
    IndexedIndirect,   // (zp,X)
    IndirectIndexed,   // (zp),Y
    Relative,
}

/// <summary>How the executor treats an opcode's memory operand.</summary>
public enum OperationKind : byte
{
    /// <summary>Hand-coded in the executor (flow control, stack, implied, SHx family, ...).</summary>
    Special,
    /// <summary>Reads its operand (immediate or from memory) and computes a result.</summary>
    Read,
    /// <summary>Stores a register value.</summary>
    Write,
    /// <summary>Read-modify-write: read, write back the old value, write the new value.</summary>
    ReadModifyWrite,
}

public readonly record struct OpcodeInfo(byte Opcode, Mnemonic Mnemonic, AddressingMode Mode, bool Undocumented, OperationKind Kind)
{
    /// <summary>
    /// Instruction length in bytes, including the opcode. BRK counts as 1 byte, as assemblers write it,
    /// although the CPU skips a padding byte after it when returning.
    /// </summary>
    public int Length => Mode switch
    {
        AddressingMode.Implied or AddressingMode.Accumulator => 1,
        AddressingMode.Absolute or AddressingMode.AbsoluteX or AddressingMode.AbsoluteY or AddressingMode.Indirect => 3,
        _ => 2,
    };
}

/// <summary>The NMOS 6502 opcode matrix, shared by the CPU core, disassembler and assembler.</summary>
public static class Opcodes
{
    public static IReadOnlyList<OpcodeInfo> Table => _table;

    private static readonly OpcodeInfo[] _table = Build();

    public static OpcodeInfo Get(byte opcode) => _table[opcode];

    private static OpcodeInfo[] Build()
    {
        const AddressingMode imp = AddressingMode.Implied, acc = AddressingMode.Accumulator, imm = AddressingMode.Immediate,
            zp = AddressingMode.ZeroPage, zpx = AddressingMode.ZeroPageX, zpy = AddressingMode.ZeroPageY,
            abs = AddressingMode.Absolute, abx = AddressingMode.AbsoluteX, aby = AddressingMode.AbsoluteY,
            ind = AddressingMode.Indirect, izx = AddressingMode.IndexedIndirect, izy = AddressingMode.IndirectIndexed,
            rel = AddressingMode.Relative;

        (Mnemonic, AddressingMode)[] m =
        [
            // 0x00
            (Mnemonic.BRK, imp), (Mnemonic.ORA, izx), (Mnemonic.JAM, imp), (Mnemonic.SLO, izx),
            (Mnemonic.NOP, zp),  (Mnemonic.ORA, zp),  (Mnemonic.ASL, zp),  (Mnemonic.SLO, zp),
            (Mnemonic.PHP, imp), (Mnemonic.ORA, imm), (Mnemonic.ASL, acc), (Mnemonic.ANC, imm),
            (Mnemonic.NOP, abs), (Mnemonic.ORA, abs), (Mnemonic.ASL, abs), (Mnemonic.SLO, abs),
            // 0x10
            (Mnemonic.BPL, rel), (Mnemonic.ORA, izy), (Mnemonic.JAM, imp), (Mnemonic.SLO, izy),
            (Mnemonic.NOP, zpx), (Mnemonic.ORA, zpx), (Mnemonic.ASL, zpx), (Mnemonic.SLO, zpx),
            (Mnemonic.CLC, imp), (Mnemonic.ORA, aby), (Mnemonic.NOP, imp), (Mnemonic.SLO, aby),
            (Mnemonic.NOP, abx), (Mnemonic.ORA, abx), (Mnemonic.ASL, abx), (Mnemonic.SLO, abx),
            // 0x20
            (Mnemonic.JSR, abs), (Mnemonic.AND, izx), (Mnemonic.JAM, imp), (Mnemonic.RLA, izx),
            (Mnemonic.BIT, zp),  (Mnemonic.AND, zp),  (Mnemonic.ROL, zp),  (Mnemonic.RLA, zp),
            (Mnemonic.PLP, imp), (Mnemonic.AND, imm), (Mnemonic.ROL, acc), (Mnemonic.ANC, imm),
            (Mnemonic.BIT, abs), (Mnemonic.AND, abs), (Mnemonic.ROL, abs), (Mnemonic.RLA, abs),
            // 0x30
            (Mnemonic.BMI, rel), (Mnemonic.AND, izy), (Mnemonic.JAM, imp), (Mnemonic.RLA, izy),
            (Mnemonic.NOP, zpx), (Mnemonic.AND, zpx), (Mnemonic.ROL, zpx), (Mnemonic.RLA, zpx),
            (Mnemonic.SEC, imp), (Mnemonic.AND, aby), (Mnemonic.NOP, imp), (Mnemonic.RLA, aby),
            (Mnemonic.NOP, abx), (Mnemonic.AND, abx), (Mnemonic.ROL, abx), (Mnemonic.RLA, abx),
            // 0x40
            (Mnemonic.RTI, imp), (Mnemonic.EOR, izx), (Mnemonic.JAM, imp), (Mnemonic.SRE, izx),
            (Mnemonic.NOP, zp),  (Mnemonic.EOR, zp),  (Mnemonic.LSR, zp),  (Mnemonic.SRE, zp),
            (Mnemonic.PHA, imp), (Mnemonic.EOR, imm), (Mnemonic.LSR, acc), (Mnemonic.ALR, imm),
            (Mnemonic.JMP, abs), (Mnemonic.EOR, abs), (Mnemonic.LSR, abs), (Mnemonic.SRE, abs),
            // 0x50
            (Mnemonic.BVC, rel), (Mnemonic.EOR, izy), (Mnemonic.JAM, imp), (Mnemonic.SRE, izy),
            (Mnemonic.NOP, zpx), (Mnemonic.EOR, zpx), (Mnemonic.LSR, zpx), (Mnemonic.SRE, zpx),
            (Mnemonic.CLI, imp), (Mnemonic.EOR, aby), (Mnemonic.NOP, imp), (Mnemonic.SRE, aby),
            (Mnemonic.NOP, abx), (Mnemonic.EOR, abx), (Mnemonic.LSR, abx), (Mnemonic.SRE, abx),
            // 0x60
            (Mnemonic.RTS, imp), (Mnemonic.ADC, izx), (Mnemonic.JAM, imp), (Mnemonic.RRA, izx),
            (Mnemonic.NOP, zp),  (Mnemonic.ADC, zp),  (Mnemonic.ROR, zp),  (Mnemonic.RRA, zp),
            (Mnemonic.PLA, imp), (Mnemonic.ADC, imm), (Mnemonic.ROR, acc), (Mnemonic.ARR, imm),
            (Mnemonic.JMP, ind), (Mnemonic.ADC, abs), (Mnemonic.ROR, abs), (Mnemonic.RRA, abs),
            // 0x70
            (Mnemonic.BVS, rel), (Mnemonic.ADC, izy), (Mnemonic.JAM, imp), (Mnemonic.RRA, izy),
            (Mnemonic.NOP, zpx), (Mnemonic.ADC, zpx), (Mnemonic.ROR, zpx), (Mnemonic.RRA, zpx),
            (Mnemonic.SEI, imp), (Mnemonic.ADC, aby), (Mnemonic.NOP, imp), (Mnemonic.RRA, aby),
            (Mnemonic.NOP, abx), (Mnemonic.ADC, abx), (Mnemonic.ROR, abx), (Mnemonic.RRA, abx),
            // 0x80
            (Mnemonic.NOP, imm), (Mnemonic.STA, izx), (Mnemonic.NOP, imm), (Mnemonic.SAX, izx),
            (Mnemonic.STY, zp),  (Mnemonic.STA, zp),  (Mnemonic.STX, zp),  (Mnemonic.SAX, zp),
            (Mnemonic.DEY, imp), (Mnemonic.NOP, imm), (Mnemonic.TXA, imp), (Mnemonic.ANE, imm),
            (Mnemonic.STY, abs), (Mnemonic.STA, abs), (Mnemonic.STX, abs), (Mnemonic.SAX, abs),
            // 0x90
            (Mnemonic.BCC, rel), (Mnemonic.STA, izy), (Mnemonic.JAM, imp), (Mnemonic.SHA, izy),
            (Mnemonic.STY, zpx), (Mnemonic.STA, zpx), (Mnemonic.STX, zpy), (Mnemonic.SAX, zpy),
            (Mnemonic.TYA, imp), (Mnemonic.STA, aby), (Mnemonic.TXS, imp), (Mnemonic.TAS, aby),
            (Mnemonic.SHY, abx), (Mnemonic.STA, abx), (Mnemonic.SHX, aby), (Mnemonic.SHA, aby),
            // 0xA0
            (Mnemonic.LDY, imm), (Mnemonic.LDA, izx), (Mnemonic.LDX, imm), (Mnemonic.LAX, izx),
            (Mnemonic.LDY, zp),  (Mnemonic.LDA, zp),  (Mnemonic.LDX, zp),  (Mnemonic.LAX, zp),
            (Mnemonic.TAY, imp), (Mnemonic.LDA, imm), (Mnemonic.TAX, imp), (Mnemonic.LXA, imm),
            (Mnemonic.LDY, abs), (Mnemonic.LDA, abs), (Mnemonic.LDX, abs), (Mnemonic.LAX, abs),
            // 0xB0
            (Mnemonic.BCS, rel), (Mnemonic.LDA, izy), (Mnemonic.JAM, imp), (Mnemonic.LAX, izy),
            (Mnemonic.LDY, zpx), (Mnemonic.LDA, zpx), (Mnemonic.LDX, zpy), (Mnemonic.LAX, zpy),
            (Mnemonic.CLV, imp), (Mnemonic.LDA, aby), (Mnemonic.TSX, imp), (Mnemonic.LAS, aby),
            (Mnemonic.LDY, abx), (Mnemonic.LDA, abx), (Mnemonic.LDX, aby), (Mnemonic.LAX, aby),
            // 0xC0
            (Mnemonic.CPY, imm), (Mnemonic.CMP, izx), (Mnemonic.NOP, imm), (Mnemonic.DCP, izx),
            (Mnemonic.CPY, zp),  (Mnemonic.CMP, zp),  (Mnemonic.DEC, zp),  (Mnemonic.DCP, zp),
            (Mnemonic.INY, imp), (Mnemonic.CMP, imm), (Mnemonic.DEX, imp), (Mnemonic.SBX, imm),
            (Mnemonic.CPY, abs), (Mnemonic.CMP, abs), (Mnemonic.DEC, abs), (Mnemonic.DCP, abs),
            // 0xD0
            (Mnemonic.BNE, rel), (Mnemonic.CMP, izy), (Mnemonic.JAM, imp), (Mnemonic.DCP, izy),
            (Mnemonic.NOP, zpx), (Mnemonic.CMP, zpx), (Mnemonic.DEC, zpx), (Mnemonic.DCP, zpx),
            (Mnemonic.CLD, imp), (Mnemonic.CMP, aby), (Mnemonic.NOP, imp), (Mnemonic.DCP, aby),
            (Mnemonic.NOP, abx), (Mnemonic.CMP, abx), (Mnemonic.DEC, abx), (Mnemonic.DCP, abx),
            // 0xE0
            (Mnemonic.CPX, imm), (Mnemonic.SBC, izx), (Mnemonic.NOP, imm), (Mnemonic.ISC, izx),
            (Mnemonic.CPX, zp),  (Mnemonic.SBC, zp),  (Mnemonic.INC, zp),  (Mnemonic.ISC, zp),
            (Mnemonic.INX, imp), (Mnemonic.SBC, imm), (Mnemonic.NOP, imp), (Mnemonic.SBC, imm),
            (Mnemonic.CPX, abs), (Mnemonic.SBC, abs), (Mnemonic.INC, abs), (Mnemonic.ISC, abs),
            // 0xF0
            (Mnemonic.BEQ, rel), (Mnemonic.SBC, izy), (Mnemonic.JAM, imp), (Mnemonic.ISC, izy),
            (Mnemonic.NOP, zpx), (Mnemonic.SBC, zpx), (Mnemonic.INC, zpx), (Mnemonic.ISC, zpx),
            (Mnemonic.SED, imp), (Mnemonic.SBC, aby), (Mnemonic.NOP, imp), (Mnemonic.ISC, aby),
            (Mnemonic.NOP, abx), (Mnemonic.SBC, abx), (Mnemonic.INC, abx), (Mnemonic.ISC, abx),
        ];

        var table = new OpcodeInfo[256];
        for (int i = 0; i < 256; i++)
        {
            var (mnemonic, mode) = m[i];
            bool undocumented = mnemonic >= Mnemonic.SLO
                || (mnemonic == Mnemonic.NOP && i != 0xEA)
                || i == 0xEB;
            table[i] = new OpcodeInfo((byte)i, mnemonic, mode, undocumented, Classify(mnemonic, mode));
        }

        return table;
    }

    private static OperationKind Classify(Mnemonic mnemonic, AddressingMode mode)
    {
        if (mode is AddressingMode.Implied or AddressingMode.Accumulator or AddressingMode.Relative)
            return OperationKind.Special;

        return mnemonic switch
        {
            Mnemonic.ORA or Mnemonic.AND or Mnemonic.EOR or Mnemonic.ADC or Mnemonic.SBC or Mnemonic.CMP
                or Mnemonic.CPX or Mnemonic.CPY or Mnemonic.BIT or Mnemonic.LDA or Mnemonic.LDX or Mnemonic.LDY
                or Mnemonic.LAX or Mnemonic.NOP or Mnemonic.LAS or Mnemonic.ANC or Mnemonic.ALR or Mnemonic.ARR
                or Mnemonic.ANE or Mnemonic.LXA or Mnemonic.SBX => OperationKind.Read,
            Mnemonic.STA or Mnemonic.STX or Mnemonic.STY or Mnemonic.SAX => OperationKind.Write,
            Mnemonic.ASL or Mnemonic.LSR or Mnemonic.ROL or Mnemonic.ROR or Mnemonic.INC or Mnemonic.DEC
                or Mnemonic.SLO or Mnemonic.RLA or Mnemonic.SRE or Mnemonic.RRA or Mnemonic.DCP
                or Mnemonic.ISC => OperationKind.ReadModifyWrite,
            _ => OperationKind.Special,
        };
    }
}
