namespace Kimulator.Core.Cpu;

/// <summary>
/// The 6502's view of the outside world. Every call is exactly one clock cycle:
/// the implementation is expected to advance the rest of the system by one cycle
/// (tick timers, update interrupt lines, etc.) as part of the access.
/// </summary>
public interface ICpuBus
{
    /// <summary>Performs a read cycle. <paramref name="sync"/> mirrors the 6502 SYNC pin (opcode fetch).</summary>
    byte Read(ushort address, bool sync);

    /// <summary>Performs a write cycle.</summary>
    void Write(ushort address, byte value);
}
