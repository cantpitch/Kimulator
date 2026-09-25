using Kimulator.Core.Cpu;

namespace Kimulator.Core.Debugging;

public readonly record struct DisassembledInstruction(ushort Address, OpcodeInfo Info, byte[] Bytes, string Operand, ushort? Target)
{
    public int Length => Bytes.Length;

    public ushort Next => (ushort)(Address + Bytes.Length);

    public string Text => Operand.Length == 0 ? Info.Mnemonic.ToString() : $"{Info.Mnemonic} {Operand}";
}

/// <summary>NMOS 6502 disassembler (documented and undocumented opcodes).</summary>
public static class Disassembler
{
    public static DisassembledInstruction Disassemble(Func<ushort, byte> read, ushort address, SymbolTable? symbols = null)
    {
        var info = Opcodes.Get(read(address));
        int length = info.Length;
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = read((ushort)(address + i));

        byte lo = length > 1 ? bytes[1] : (byte)0;
        ushort word = length > 2 ? (ushort)(lo | bytes[2] << 8) : lo;
        ushort? target = null;

        string operand = info.Mode switch
        {
            AddressingMode.Implied => "",
            AddressingMode.Accumulator => "A",
            AddressingMode.Immediate => $"#${lo:X2}",
            AddressingMode.ZeroPage => Name(target = lo, zeroPage: true),
            AddressingMode.ZeroPageX => Name(target = lo, zeroPage: true) + ",X",
            AddressingMode.ZeroPageY => Name(target = lo, zeroPage: true) + ",Y",
            AddressingMode.Absolute => Name(target = word),
            AddressingMode.AbsoluteX => Name(target = word) + ",X",
            AddressingMode.AbsoluteY => Name(target = word) + ",Y",
            AddressingMode.Indirect => $"({Name(target = word)})",
            AddressingMode.IndexedIndirect => $"({Name(target = lo, zeroPage: true)},X)",
            AddressingMode.IndirectIndexed => $"({Name(target = lo, zeroPage: true)}),Y",
            AddressingMode.Relative => Name(target = (ushort)(address + 2 + (sbyte)lo)),
            _ => "",
        };

        return new DisassembledInstruction(address, info, bytes, operand, target);

        string Name(ushort? value, bool zeroPage = false)
        {
            ushort v = value!.Value;
            return symbols?.NameOf(v) ?? (zeroPage ? $"${v:X2}" : $"${v:X4}");
        }
    }

    /// <summary>Disassembles <paramref name="count"/> consecutive instructions.</summary>
    public static List<DisassembledInstruction> DisassembleRange(Func<ushort, byte> read, ushort start, int count, SymbolTable? symbols = null)
    {
        var result = new List<DisassembledInstruction>(count);
        ushort address = start;
        for (int i = 0; i < count; i++)
        {
            var instruction = Disassemble(read, address, symbols);
            result.Add(instruction);
            address = instruction.Next;
        }

        return result;
    }

    /// <summary>
    /// Code has no markers, so disassembling backwards is a guess: finds an address about
    /// <paramref name="instructionsBefore"/> instructions before <paramref name="target"/> from which
    /// forward decoding lands exactly on <paramref name="target"/>.
    /// </summary>
    public static ushort FindStartBefore(Func<ushort, byte> read, ushort target, int instructionsBefore)
    {
        int maxBack = instructionsBefore * 3;
        ushort best = target;
        int bestCount = 0;
        for (int back = maxBack; back >= 1; back--)
        {
            ushort address = (ushort)(target - back);
            int count = 0;
            while (address != target && (ushort)(target - address) <= back)
            {
                address = (ushort)(address + Opcodes.Get(read(address)).Length);
                count++;
            }

            // Prefer the candidate that syncs and yields the number of instructions closest to what was asked.
            if (address == target && count <= instructionsBefore && count > bestCount)
            {
                best = (ushort)(target - back);
                bestCount = count;
            }
        }

        return best;
    }
}
