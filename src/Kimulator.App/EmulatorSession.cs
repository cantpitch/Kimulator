using System.Diagnostics;
using Avalonia.Threading;
using Kimulator.Core.Machine;
using Kimulator.Kim1;

namespace Kimulator.App;

/// <summary>
/// Owns the emulated KIM-1 and the thread that runs it. All board access from the UI goes through
/// <see cref="MachineRunner.Post"/> so the board is only ever touched by the emulation thread.
/// </summary>
public sealed class EmulatorSession : IDisposable
{
    /// <summary>Keys are held at least this long so a quick click still survives the monitor's debounce.</summary>
    private static readonly TimeSpan MinimumKeyHold = TimeSpan.FromMilliseconds(60);

    private readonly Dictionary<Kim1Key, long> _pressedAt = [];

    public EmulatorSession()
    {
        Board = Kim1Board.CreateWithDefaultRoms();
        Runner = new MachineRunner(Board);
        PowerCycle();
        Runner.Start();
    }

    public Kim1Board Board { get; }

    public MachineRunner Runner { get; }

    /// <summary>
    /// Convenience (not on a real KIM-1): point the NMI and IRQ/BRK vectors at $17FA/$17FE to the
    /// monitor's SAVE routine at power-on so ST, SST and BRK work without entering them by hand.
    /// </summary>
    public bool PresetInterruptVectors { get; set; } = true;

    public DisplayFrame Display => Board.Display.Latest;

    public void PowerCycle() => Runner.Post(() =>
    {
        Board.PowerOn();
        if (!PresetInterruptVectors) return;
        Board.Poke(0x17FA, 0x00);
        Board.Poke(0x17FB, 0x1C);
        Board.Poke(0x17FE, 0x00);
        Board.Poke(0x17FF, 0x1C);
    });

    public void SetKey(Kim1Key key, bool down)
    {
        if (down)
        {
            _pressedAt[key] = Stopwatch.GetTimestamp();
            Runner.Post(() => Board.SetKey(key, true));
            return;
        }

        var held = _pressedAt.Remove(key, out long start) ? Stopwatch.GetElapsedTime(start) : MinimumKeyHold;
        if (held >= MinimumKeyHold)
            Runner.Post(() => Board.SetKey(key, false));
        else
            DispatcherTimer.RunOnce(() => Runner.Post(() => Board.SetKey(key, false)), MinimumKeyHold - held);
    }

    public void SetSingleStep(bool on) => Runner.Post(() => Board.SingleStep = on);

    public void Dispose() => Runner.Dispose();
}
