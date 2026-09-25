using Kimulator.Core.Debugging;

namespace Kimulator.App.Debugging;

/// <summary>A consistent copy of the machine state taken on the emulation thread for the debugger UI.</summary>
public sealed record DebugSnapshot(
    byte A,
    byte X,
    byte Y,
    byte S,
    byte P,
    ushort PC,
    long Cycles,
    bool Jammed,
    bool InterruptPending,
    byte[] Memory,
    IReadOnlyList<Breakpoint> Breakpoints,
    bool BreakOnBrk,
    bool BreakOnJam,
    bool BusTraceEnabled)
{
    public byte Read(ushort address) => Memory[address];
}
