using Kimulator.Core.Cpu;
using Kimulator.Core.Debugging;

namespace Kimulator.Core.Machine;

/// <summary>A clocked 6502 system the <see cref="MachineRunner"/> can drive in real time and debug.</summary>
public interface IMachine
{
    /// <summary>Nominal CPU clock in Hz.</summary>
    double ClockHz { get; }

    /// <summary>Clock cycles elapsed since power-on.</summary>
    long Cycles { get; }

    Cpu6502 Cpu { get; }

    Debugger Debugger { get; }

    /// <summary>
    /// Runs whole instructions until <see cref="Cycles"/> reaches <paramref name="targetCycle"/> (may overshoot by
    /// one instruction), or until the debugger requests a stop.
    /// </summary>
    void RunUntil(long targetCycle);

    /// <summary>Runs exactly one instruction (or interrupt sequence), ignoring breakpoints. Returns cycles used.</summary>
    int StepInstruction();

    /// <summary>Reads memory as the CPU would see it, without side effects.</summary>
    byte Peek(ushort address);

    /// <summary>Writes memory (RAM, and I/O registers) outside of a bus cycle.</summary>
    void Poke(ushort address, byte value);
}
