namespace Kimulator.Core.Bus;

/// <summary>
/// A card on the system bus (KIM-1 expansion/application connectors, or a KIM-4 motherboard slot).
/// Cards see every bus cycle with the full 16-bit address and may claim it.
/// </summary>
public interface IExpansionCard
{
    string Name { get; }

    /// <summary>Called once per clock cycle before the bus access.</summary>
    void Tick() { }

    /// <summary>Returns true and the data if this card responds to a read at <paramref name="address"/>.</summary>
    bool TryRead(ushort address, out byte value);

    /// <summary>Returns true if this card responds to a write at <paramref name="address"/>.</summary>
    bool TryWrite(ushort address, byte value);

    /// <summary>
    /// Models the KIM-1 DECODE ENABLE line: return true to stop the base board's own (A0-A12 only)
    /// decoding from answering at this address. Memory cards above $1FFF need this to avoid the
    /// base board's 8 KB mirrors.
    /// </summary>
    bool DisablesOnboardDecode(ushort address) => false;

    bool IrqAsserted => false;

    bool NmiAsserted => false;

    void Reset() { }
}
