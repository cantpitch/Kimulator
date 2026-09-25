namespace Kimulator.Core.Cpu;

public sealed partial class Cpu6502
{
    private static readonly OpcodeInfo[] Decode = [.. Opcodes.Table];

    private void Execute(byte opcode)
    {
        ref readonly OpcodeInfo info = ref Decode[opcode];
        switch (info.Kind)
        {
            case OperationKind.Read:
            {
                byte value = info.Mode == AddressingMode.Immediate
                    ? FetchOperand()
                    : ReadCycle(ResolveAddress(info.Mode, alwaysDummyRead: false));
                ExecuteRead(info.Mnemonic, value);
                return;
            }
            case OperationKind.Write:
            {
                ushort address = ResolveAddress(info.Mode, alwaysDummyRead: true);
                WriteCycle(address, info.Mnemonic switch
                {
                    Mnemonic.STA => A,
                    Mnemonic.STX => X,
                    Mnemonic.STY => Y,
                    _ => (byte)(A & X), // SAX
                });
                return;
            }
            case OperationKind.ReadModifyWrite:
            {
                ushort address = ResolveAddress(info.Mode, alwaysDummyRead: true);
                byte value = ReadCycle(address);
                WriteCycle(address, value); // NMOS writes the unmodified value back first
                WriteCycle(address, ExecuteModify(info.Mnemonic, value));
                return;
            }
            default:
                ExecuteSpecial(opcode);
                return;
        }
    }

    private ushort ResolveAddress(AddressingMode mode, bool alwaysDummyRead) => mode switch
    {
        AddressingMode.ZeroPage => AddrZeroPage(),
        AddressingMode.ZeroPageX => AddrZeroPageIndexed(X),
        AddressingMode.ZeroPageY => AddrZeroPageIndexed(Y),
        AddressingMode.Absolute => AddrAbsolute(),
        AddressingMode.AbsoluteX => AddrAbsoluteIndexed(X, alwaysDummyRead),
        AddressingMode.AbsoluteY => AddrAbsoluteIndexed(Y, alwaysDummyRead),
        AddressingMode.IndexedIndirect => AddrIndexedIndirect(),
        AddressingMode.IndirectIndexed => AddrIndirectIndexed(alwaysDummyRead),
        _ => throw new InvalidOperationException($"Addressing mode {mode} has no memory operand."),
    };

    private void ExecuteRead(Mnemonic mnemonic, byte value)
    {
        switch (mnemonic)
        {
            case Mnemonic.LDA: A = value; SetNZ(A); break;
            case Mnemonic.LDX: X = value; SetNZ(X); break;
            case Mnemonic.LDY: Y = value; SetNZ(Y); break;
            case Mnemonic.LAX: A = X = value; SetNZ(A); break;
            case Mnemonic.ORA: A |= value; SetNZ(A); break;
            case Mnemonic.AND: A &= value; SetNZ(A); break;
            case Mnemonic.EOR: A ^= value; SetNZ(A); break;
            case Mnemonic.ADC: Adc(value); break;
            case Mnemonic.SBC: Sbc(value); break;
            case Mnemonic.CMP: Compare(A, value); break;
            case Mnemonic.CPX: Compare(X, value); break;
            case Mnemonic.CPY: Compare(Y, value); break;
            case Mnemonic.BIT:
                SetFlag(FlagZ, (A & value) == 0);
                _p = (byte)((_p & ~(FlagN | FlagV)) | (value & (FlagN | FlagV)));
                break;
            case Mnemonic.NOP: break;
            case Mnemonic.LAS:
                A = X = S = (byte)(value & S);
                SetNZ(A);
                break;
            case Mnemonic.ANC:
                A &= value;
                SetNZ(A);
                SetFlag(FlagC, (A & 0x80) != 0);
                break;
            case Mnemonic.ALR:
                A &= value;
                SetFlag(FlagC, (A & 0x01) != 0);
                A >>= 1;
                SetNZ(A);
                break;
            case Mnemonic.ARR: Arr(value); break;
            case Mnemonic.ANE:
                // Unstable on real silicon; $EE is the commonly observed "magic" constant.
                A = (byte)((A | 0xEE) & X & value);
                SetNZ(A);
                break;
            case Mnemonic.LXA:
                A = X = (byte)((A | 0xEE) & value);
                SetNZ(A);
                break;
            case Mnemonic.SBX:
            {
                int result = (A & X) - value;
                SetFlag(FlagC, result >= 0);
                X = (byte)result;
                SetNZ(X);
                break;
            }
            default:
                throw new InvalidOperationException($"{mnemonic} is not a read operation.");
        }
    }

    private byte ExecuteModify(Mnemonic mnemonic, byte value)
    {
        switch (mnemonic)
        {
            case Mnemonic.ASL: return Asl(value);
            case Mnemonic.LSR: return Lsr(value);
            case Mnemonic.ROL: return Rol(value);
            case Mnemonic.ROR: return Ror(value);
            case Mnemonic.INC: value++; SetNZ(value); return value;
            case Mnemonic.DEC: value--; SetNZ(value); return value;
            case Mnemonic.SLO: value = Asl(value); A |= value; SetNZ(A); return value;
            case Mnemonic.RLA: value = Rol(value); A &= value; SetNZ(A); return value;
            case Mnemonic.SRE: value = Lsr(value); A ^= value; SetNZ(A); return value;
            case Mnemonic.RRA: value = Ror(value); Adc(value); return value;
            case Mnemonic.DCP: value--; Compare(A, value); return value;
            case Mnemonic.ISC: value++; Sbc(value); return value;
            default: throw new InvalidOperationException($"{mnemonic} is not a read-modify-write operation.");
        }
    }

    private void ExecuteSpecial(byte opcode)
    {
        switch (opcode)
        {
            // ---- interrupts and subroutines
            case 0x00: // BRK
                FetchOperand(); // padding byte
                Push((byte)(PC >> 8));
                Push((byte)PC);
                EnterInterruptVector(breakFlag: true);
                break;
            case 0x20: // JSR abs
            {
                byte lo = FetchOperand();
                ReadCycle((ushort)(0x0100 | S)); // internal operation
                Push((byte)(PC >> 8));
                Push((byte)PC);
                byte hi = ReadCycle(PC);
                PC = (ushort)(lo | hi << 8);
                break;
            }
            case 0x40: // RTI
            {
                DummyReadPc();
                ReadCycle((ushort)(0x0100 | S));
                P = Pull();
                byte lo = Pull();
                byte hi = Pull();
                PC = (ushort)(lo | hi << 8);
                break;
            }
            case 0x60: // RTS
            {
                DummyReadPc();
                ReadCycle((ushort)(0x0100 | S));
                byte lo = Pull();
                byte hi = Pull();
                PC = (ushort)(lo | hi << 8);
                ReadCycle(PC);
                PC++;
                break;
            }
            case 0x4C: // JMP abs
                PC = AddrAbsolute();
                break;
            case 0x6C: // JMP (ind) -- the pointer high byte does not carry into the next page
            {
                ushort pointer = AddrAbsolute();
                byte lo = ReadCycle(pointer);
                byte hi = ReadCycle((ushort)((pointer & 0xFF00) | ((pointer + 1) & 0x00FF)));
                PC = (ushort)(lo | hi << 8);
                break;
            }

            // ---- stack
            case 0x08: DummyReadPc(); Push((byte)(_p | FlagB | FlagU)); break;       // PHP
            case 0x48: DummyReadPc(); Push(A); break;                                // PHA
            case 0x28: DummyReadPc(); ReadCycle((ushort)(0x0100 | S)); P = Pull(); break; // PLP
            case 0x68: DummyReadPc(); ReadCycle((ushort)(0x0100 | S)); A = Pull(); SetNZ(A); break; // PLA

            // ---- branches
            case 0x10: Branch(!GetFlag(FlagN)); break; // BPL
            case 0x30: Branch(GetFlag(FlagN)); break;  // BMI
            case 0x50: Branch(!GetFlag(FlagV)); break; // BVC
            case 0x70: Branch(GetFlag(FlagV)); break;  // BVS
            case 0x90: Branch(!GetFlag(FlagC)); break; // BCC
            case 0xB0: Branch(GetFlag(FlagC)); break;  // BCS
            case 0xD0: Branch(!GetFlag(FlagZ)); break; // BNE
            case 0xF0: Branch(GetFlag(FlagZ)); break;  // BEQ

            // ---- flags (the change lands after the dummy read, which matters for IRQ polling)
            case 0x18: DummyReadPc(); SetFlag(FlagC, false); break; // CLC
            case 0x38: DummyReadPc(); SetFlag(FlagC, true); break;  // SEC
            case 0x58: DummyReadPc(); SetFlag(FlagI, false); break; // CLI
            case 0x78: DummyReadPc(); SetFlag(FlagI, true); break;  // SEI
            case 0xB8: DummyReadPc(); SetFlag(FlagV, false); break; // CLV
            case 0xD8: DummyReadPc(); SetFlag(FlagD, false); break; // CLD
            case 0xF8: DummyReadPc(); SetFlag(FlagD, true); break;  // SED

            // ---- register transfers, increments
            case 0xAA: DummyReadPc(); X = A; SetNZ(X); break; // TAX
            case 0xA8: DummyReadPc(); Y = A; SetNZ(Y); break; // TAY
            case 0x8A: DummyReadPc(); A = X; SetNZ(A); break; // TXA
            case 0x98: DummyReadPc(); A = Y; SetNZ(A); break; // TYA
            case 0xBA: DummyReadPc(); X = S; SetNZ(X); break; // TSX
            case 0x9A: DummyReadPc(); S = X; break;           // TXS
            case 0xE8: DummyReadPc(); X++; SetNZ(X); break;   // INX
            case 0xC8: DummyReadPc(); Y++; SetNZ(Y); break;   // INY
            case 0xCA: DummyReadPc(); X--; SetNZ(X); break;   // DEX
            case 0x88: DummyReadPc(); Y--; SetNZ(Y); break;   // DEY

            // ---- accumulator shifts
            case 0x0A: DummyReadPc(); A = Asl(A); break;
            case 0x4A: DummyReadPc(); A = Lsr(A); break;
            case 0x2A: DummyReadPc(); A = Rol(A); break;
            case 0x6A: DummyReadPc(); A = Ror(A); break;

            // ---- implied NOPs (documented $EA and undocumented)
            case 0xEA or 0x1A or 0x3A or 0x5A or 0x7A or 0xDA or 0xFA:
                DummyReadPc();
                break;

            // ---- unstable "AND with high byte + 1" stores
            case 0x93: // SHA (zp),Y
                StoreAndHigh(ReadIndirectPointer(), Y, (byte)(A & X));
                break;
            case 0x9F: // SHA abs,Y
                StoreAndHigh(AddrAbsolute(), Y, (byte)(A & X));
                break;
            case 0x9E: // SHX abs,Y
                StoreAndHigh(AddrAbsolute(), Y, X);
                break;
            case 0x9C: // SHY abs,X
                StoreAndHigh(AddrAbsolute(), X, Y);
                break;
            case 0x9B: // TAS abs,Y
            {
                ushort baseAddr = AddrAbsolute();
                S = (byte)(A & X);
                StoreAndHigh(baseAddr, Y, S);
                break;
            }

            // ---- JAM / KIL
            case 0x02 or 0x12 or 0x22 or 0x32 or 0x42 or 0x52 or 0x62 or 0x72 or 0x92 or 0xB2 or 0xD2 or 0xF2:
                ReadCycle(PC);
                Jammed = true;
                break;

            default:
                throw new InvalidOperationException($"Opcode ${opcode:X2} is not handled.");
        }
    }

    private void Branch(bool taken)
    {
        sbyte offset = (sbyte)FetchOperand();
        if (!taken)
            return;

        // A taken branch that does not cross a page does not poll interrupts on its last cycle,
        // so an IRQ that arrived during the operand fetch waits one more instruction.
        if (_irqPending && !_prevIrqPending)
            _irqPending = false;

        ReadCycle(PC);
        ushort target = (ushort)(PC + offset);
        if (((target ^ PC) & 0xFF00) != 0)
            ReadCycle((ushort)((PC & 0xFF00) | (target & 0x00FF)));
        PC = target;
    }

    /// <summary>
    /// SHA/SHX/SHY/TAS: store value AND (high byte of base address + 1). When indexing crosses a page
    /// the stored value also replaces the high byte of the effective address.
    /// </summary>
    private void StoreAndHigh(ushort baseAddr, byte index, byte value)
    {
        ushort address = AddIndexWithDummyRead(baseAddr, index, alwaysDummyRead: true);
        byte result = (byte)(value & ((baseAddr >> 8) + 1));
        if (((baseAddr ^ address) & 0xFF00) != 0)
            address = (ushort)((result << 8) | (address & 0x00FF));
        WriteCycle(address, result);
    }

    // ---------------------------------------------------------------- ALU

    private void Compare(byte register, byte value)
    {
        int result = register - value;
        SetFlag(FlagC, result >= 0);
        SetNZ((byte)result);
    }

    private byte Asl(byte value)
    {
        SetFlag(FlagC, (value & 0x80) != 0);
        value <<= 1;
        SetNZ(value);
        return value;
    }

    private byte Lsr(byte value)
    {
        SetFlag(FlagC, (value & 0x01) != 0);
        value >>= 1;
        SetNZ(value);
        return value;
    }

    private byte Rol(byte value)
    {
        int carryIn = _p & FlagC;
        SetFlag(FlagC, (value & 0x80) != 0);
        value = (byte)((value << 1) | carryIn);
        SetNZ(value);
        return value;
    }

    private byte Ror(byte value)
    {
        int carryIn = (_p & FlagC) << 7;
        SetFlag(FlagC, (value & 0x01) != 0);
        value = (byte)((value >> 1) | carryIn);
        SetNZ(value);
        return value;
    }

    private void Adc(byte value)
    {
        int carry = _p & FlagC;
        if ((_p & FlagD) == 0)
        {
            int sum = A + value + carry;
            SetFlag(FlagV, (~(A ^ value) & (A ^ sum) & 0x80) != 0);
            SetFlag(FlagC, sum > 0xFF);
            A = (byte)sum;
            SetNZ(A);
            return;
        }

        // NMOS decimal mode: Z comes from the binary sum, N and V from the intermediate result.
        int lo = (A & 0x0F) + (value & 0x0F) + carry;
        if (lo >= 0x0A)
            lo = ((lo + 0x06) & 0x0F) + 0x10;
        int result = (A & 0xF0) + (value & 0xF0) + lo;
        SetFlag(FlagZ, ((A + value + carry) & 0xFF) == 0);
        SetFlag(FlagN, (result & 0x80) != 0);
        SetFlag(FlagV, (~(A ^ value) & (A ^ result) & 0x80) != 0);
        if (result >= 0xA0)
            result += 0x60;
        SetFlag(FlagC, result >= 0x100);
        A = (byte)result;
    }

    private void Sbc(byte value)
    {
        int carry = _p & FlagC;
        int binary = A - value - (1 - carry);

        // NMOS decimal mode: all flags come from the binary subtraction.
        SetFlag(FlagV, ((A ^ value) & (A ^ binary) & 0x80) != 0);
        SetFlag(FlagC, binary >= 0);
        SetNZ((byte)binary);

        if ((_p & FlagD) == 0)
        {
            A = (byte)binary;
            return;
        }

        int lo = (A & 0x0F) - (value & 0x0F) + carry - 1;
        if (lo < 0)
            lo = ((lo - 0x06) & 0x0F) - 0x10;
        int result = (A & 0xF0) - (value & 0xF0) + lo;
        if (result < 0)
            result -= 0x60;
        A = (byte)result;
    }

    private void Arr(byte value)
    {
        int and = A & value;
        int carryIn = _p & FlagC;
        int result = (and >> 1) | (carryIn << 7);

        if ((_p & FlagD) == 0)
        {
            A = (byte)result;
            SetNZ(A);
            SetFlag(FlagC, (result & 0x40) != 0);
            SetFlag(FlagV, (((result >> 6) ^ (result >> 5)) & 1) != 0);
            return;
        }

        SetFlag(FlagN, carryIn != 0);
        SetFlag(FlagZ, result == 0);
        SetFlag(FlagV, ((and ^ result) & 0x40) != 0);
        if ((and & 0x0F) + (and & 0x01) > 0x05)
            result = (result & 0xF0) | ((result + 0x06) & 0x0F);
        if ((and & 0xF0) + (and & 0x10) > 0x50)
        {
            result = (result + 0x60) & 0xFF;
            SetFlag(FlagC, true);
        }
        else
        {
            SetFlag(FlagC, false);
        }

        A = (byte)result;
    }
}
