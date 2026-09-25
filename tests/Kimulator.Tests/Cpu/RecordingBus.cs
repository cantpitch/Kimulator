using Kimulator.Core.Cpu;

namespace Kimulator.Tests.Cpu;

/// <summary>Flat 64 KB RAM that records every bus cycle.</summary>
internal sealed class RecordingBus : ICpuBus
{
    public readonly byte[] Memory = new byte[0x10000];
    public readonly List<(ushort Address, byte Value, bool IsWrite)> Cycles = [];
    public bool Record { get; set; } = true;

    public byte Read(ushort address, bool sync)
    {
        byte value = Memory[address];
        if (Record) Cycles.Add((address, value, false));
        return value;
    }

    public void Write(ushort address, byte value)
    {
        Memory[address] = value;
        if (Record) Cycles.Add((address, value, true));
    }
}
