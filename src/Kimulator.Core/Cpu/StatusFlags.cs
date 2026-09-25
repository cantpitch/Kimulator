namespace Kimulator.Core.Cpu;

/// <summary>Bits of the 6502 processor status register (P).</summary>
[Flags]
public enum StatusFlags : byte
{
    None = 0,
    Carry = 0x01,
    Zero = 0x02,
    InterruptDisable = 0x04,
    Decimal = 0x08,
    Break = 0x10,
    Unused = 0x20,
    Overflow = 0x40,
    Negative = 0x80,
}
