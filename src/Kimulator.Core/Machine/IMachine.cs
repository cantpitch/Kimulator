namespace Kimulator.Core.Machine;

/// <summary>A clocked system the <see cref="MachineRunner"/> can drive in real time.</summary>
public interface IMachine
{
    /// <summary>Nominal CPU clock in Hz.</summary>
    double ClockHz { get; }

    /// <summary>Clock cycles elapsed since power-on.</summary>
    long Cycles { get; }

    /// <summary>Runs whole instructions until <see cref="Cycles"/> reaches <paramref name="targetCycle"/> (may overshoot by one instruction).</summary>
    void RunUntil(long targetCycle);
}
