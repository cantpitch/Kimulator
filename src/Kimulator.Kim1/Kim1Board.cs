using Kimulator.Core.Bus;
using Kimulator.Core.Cpu;
using Kimulator.Core.Machine;

namespace Kimulator.Kim1;

/// <summary>
/// The KIM-1 base board: 6502, 1 KB RAM, two 6530 RRIOTs (002 and 003), the 74145 digit/row
/// decoder, the keypad matrix, six LED digits and the RS/ST/SST logic.
/// <para>
/// Address decoding matches the hardware: only A0-A12 are decoded (A10-A12 by the 74145 into
/// K0-K7), so the 8 KB map repeats through the 64 KB space. That is how the reset vector at
/// $FFFC comes from the 6530-002 ROM at $1FFC.
/// </para>
/// Not thread-safe: drive it from one thread (see <see cref="MachineRunner"/>).
/// </summary>
public sealed class Kim1Board : ICpuBus, IMachine
{
    public const double DefaultClockHz = 1_000_000;

    /// <summary>Emulated cycles per display frame (20 ms at 1 MHz).</summary>
    public const int CyclesPerFrame = 20_000;

    private readonly byte[] _ram = new byte[1024];
    private readonly List<IExpansionCard> _cards = [];
    private int _pressedKeys; // bit n = Kim1Key n held (matrix keys only)
    private bool _stopHeld;
    private bool _resetHeld;
    private long _cycles;
    private long _nextFrameCycle = CyclesPerFrame;
    private byte _dataBus;

    public Kim1Board(byte[] rom002, byte[] rom003)
    {
        Cpu = new Cpu6502(this);
        Riot002 = new Riot6530("6530-002 (U2)", rom002) { PortAInput = KeypadPortA };
        Riot003 = new Riot6530("6530-003 (U3)", rom003);
        Riot002.PortsChanged += UpdateDisplay;
    }

    /// <summary>Creates a board with the original ROMs embedded in this assembly.</summary>
    public static Kim1Board CreateWithDefaultRoms() => new(LoadRom("6530-002.bin"), LoadRom("6530-003.bin"));

    public Cpu6502 Cpu { get; }

    /// <summary>U2: monitor ROM $1C00-$1FFF, RAM $17C0-$17FF, I/O+timer $1740-$177F (keypad, display, TTY, tape).</summary>
    public Riot6530 Riot002 { get; }

    /// <summary>U3: ROM $1800-$1BFF, RAM $1780-$17BF, I/O+timer $1700-$173F (application port).</summary>
    public Riot6530 Riot003 { get; }

    public LedDisplay Display { get; } = new();

    public byte[] Ram => _ram;

    public IReadOnlyList<IExpansionCard> Cards => _cards;

    public double ClockHz => DefaultClockHz;

    public long Cycles => _cycles;

    /// <summary>The SST (single step) slide switch.</summary>
    public bool SingleStep { get; set; }

    /// <summary>TTY/keyboard jumper: true connects 74145 output 3 to PA0 so the monitor starts in TTY mode.</summary>
    public bool TtyMode { get; set; }

    /// <summary>Optional jumper routing the 6530-003 timer interrupt (PB7) to the CPU IRQ line.</summary>
    public bool Riot003TimerIrqJumper { get; set; }

    /// <summary>True while RS is held: the CPU is stopped in reset.</summary>
    public bool InReset => _resetHeld;

    public void AddCard(IExpansionCard card) => _cards.Add(card);

    public void RemoveCard(IExpansionCard card) => _cards.Remove(card);

    /// <summary>Power-on: clears RAM (real 2102s come up random), resets all chips and the CPU.</summary>
    public void PowerOn(bool randomizeRam = false)
    {
        if (randomizeRam) Random.Shared.NextBytes(_ram);
        else Array.Clear(_ram);
        Array.Clear(Riot002.Ram);
        Array.Clear(Riot003.Ram);
        ResetSystem();
    }

    /// <summary>Pulses the RES line (like tapping RS).</summary>
    public void ResetSystem()
    {
        Riot002.Reset();
        Riot003.Reset();
        foreach (var card in _cards) card.Reset();
        Cpu.Reset();
    }

    public void SetKey(Kim1Key key, bool down)
    {
        switch (key)
        {
            case Kim1Key.Stop:
                _stopHeld = down;
                break;
            case Kim1Key.Reset:
                if (down && !_resetHeld)
                {
                    Riot002.Reset();
                    Riot003.Reset();
                    foreach (var card in _cards) card.Reset();
                }
                else if (!down && _resetHeld)
                {
                    Cpu.Reset();
                }

                _resetHeld = down;
                break;
            default:
                if (down) _pressedKeys |= 1 << (int)key;
                else _pressedKeys &= ~(1 << (int)key);
                break;
        }
    }

    public bool IsKeyDown(Kim1Key key) => key switch
    {
        Kim1Key.Stop => _stopHeld,
        Kim1Key.Reset => _resetHeld,
        _ => (_pressedKeys & (1 << (int)key)) != 0,
    };

    public void RunUntil(long targetCycle)
    {
        while (_cycles < targetCycle)
        {
            if (_resetHeld)
            {
                // CPU held in reset: time passes but nothing touches the bus.
                long idleTo = Math.Min(targetCycle, _nextFrameCycle);
                while (_cycles < idleTo) TickDevices();
                CheckFrame();
                continue;
            }

            Cpu.Step();
            CheckFrame();
        }
    }

    /// <summary>Runs exactly one instruction (or interrupt sequence). Returns cycles used.</summary>
    public int StepInstruction()
    {
        int cycles = Cpu.Step();
        CheckFrame();
        return cycles;
    }

    // ---------------------------------------------------------------- ICpuBus

    public byte Read(ushort address, bool sync)
    {
        TickDevices();

        // SST: NMI on every opcode fetch outside the monitor ROM (K7, $1C00-$1FFF).
        bool sstNmi = SingleStep && sync && (address & 0x1C00) != 0x1C00;
        UpdateInterruptLines(sstNmi);

        _dataBus = DecodeRead(address);
        return _dataBus;
    }

    public void Write(ushort address, byte value)
    {
        TickDevices();
        UpdateInterruptLines(false);
        _dataBus = value;
        DecodeWrite(address, value);
    }

    /// <summary>Reads memory as the CPU would see it, without side effects on I/O timers or clocks.</summary>
    public byte Peek(ushort address)
    {
        foreach (var card in _cards)
        {
            if (card.TryRead(address, out byte v)) return v;
        }

        int a = address & 0x1FFF;
        return (a >> 10) switch
        {
            0 => _ram[a & 0x3FF],
            5 when a >= 0x1780 => a >= 0x17C0 ? Riot002.ReadRam(a) : Riot003.ReadRam(a),
            6 => Riot003.ReadRom(a),
            7 => Riot002.ReadRom(a),
            _ => 0xFF,
        };
    }

    /// <summary>Writes RAM directly (no bus cycle). Writes to ROM/I/O are ignored.</summary>
    public void Poke(ushort address, byte value)
    {
        int a = address & 0x1FFF;
        if (a < 0x0400) _ram[a] = value;
        else if (a is >= 0x1780 and < 0x17C0) Riot003.WriteRam(a, value);
        else if (a is >= 0x17C0 and < 0x1800) Riot002.WriteRam(a, value);
        else
        {
            foreach (var card in _cards)
            {
                if (card.TryWrite(address, value)) return;
            }
        }
    }

    // ---------------------------------------------------------------- internals

    private void TickDevices()
    {
        _cycles++;
        Riot002.Tick();
        Riot003.Tick();
        foreach (var card in _cards) card.Tick();
    }

    private void UpdateInterruptLines(bool sstNmi)
    {
        bool nmi = _stopHeld || sstNmi;
        bool irq = Riot003TimerIrqJumper && Riot003.IrqAsserted;
        foreach (var card in _cards)
        {
            nmi |= card.NmiAsserted;
            irq |= card.IrqAsserted;
        }

        Cpu.NmiLine = nmi;
        Cpu.IrqLine = irq;
    }

    private bool OnboardDecodeEnabled(ushort address)
    {
        foreach (var card in _cards)
        {
            if (card.DisablesOnboardDecode(address)) return false;
        }

        return true;
    }

    private byte DecodeRead(ushort address)
    {
        foreach (var card in _cards)
        {
            if (card.TryRead(address, out byte value)) return value;
        }

        if (!OnboardDecodeEnabled(address))
            return _dataBus;

        int a = address & 0x1FFF;
        switch (a >> 10)
        {
            case 0: return _ram[a & 0x3FF];                  // K0: 1 KB RAM
            case 5 when a >= 0x1700:                          // K5: RRIOT I/O and RAM
                return (a & 0xC0) switch
                {
                    0x00 => Riot003.ReadIo(a),
                    0x40 => Riot002.ReadIo(a),
                    0x80 => Riot003.ReadRam(a),
                    _ => Riot002.ReadRam(a),
                };
            case 6: return Riot003.ReadRom(a);               // K6: $1800-$1BFF
            case 7: return Riot002.ReadRom(a);               // K7: $1C00-$1FFF
            default: return _dataBus;                         // K1-K4 unpopulated: open bus
        }
    }

    private void DecodeWrite(ushort address, byte value)
    {
        foreach (var card in _cards)
        {
            if (card.TryWrite(address, value)) return;
        }

        if (!OnboardDecodeEnabled(address))
            return;

        int a = address & 0x1FFF;
        switch (a >> 10)
        {
            case 0:
                _ram[a & 0x3FF] = value;
                break;
            case 5 when a >= 0x1700:
                switch (a & 0xC0)
                {
                    case 0x00: Riot003.WriteIo(a, value); break;
                    case 0x40: Riot002.WriteIo(a, value); break;
                    case 0x80: Riot003.WriteRam(a, value); break;
                    default: Riot002.WriteRam(a, value); break;
                }

                break;
        }
    }

    /// <summary>
    /// Port A of the 002 as seen from outside: keypad columns pulled low by a pressed key in the row
    /// selected through the 74145, the TTY/KB jumper on row 3, and PA7 = TTY serial input (idle high).
    /// </summary>
    private byte KeypadPortA()
    {
        int selected = (Riot002.PortBPins >> 1) & 0x0F;
        byte pins = 0xFF;
        if (selected <= 2 && _pressedKeys != 0)
        {
            for (int key = selected * 7; key < selected * 7 + 7 && key <= (int)Kim1Key.Pc; key++)
            {
                if ((_pressedKeys & (1 << key)) != 0)
                    pins &= (byte)~(1 << ((Kim1Key)key).Column());
            }
        }
        else if (selected == 3 && TtyMode)
        {
            pins &= 0xFE;
        }

        return pins;
    }

    private void UpdateDisplay()
    {
        // 74145 outputs 4-9 drive the digit cathodes; PA0-PA6 driven high light segments a-g.
        int selected = (Riot002.PortBPins >> 1) & 0x0F;
        int digit = selected is >= 4 and <= 9 ? selected - 4 : -1;
        byte segments = (byte)(Riot002.PortAData & Riot002.PortADirection & 0x7F);
        Display.Update(_cycles, digit, segments);
    }

    private void CheckFrame()
    {
        if (_cycles < _nextFrameCycle) return;
        Display.EndFrame(_cycles);
        _nextFrameCycle = _cycles + CyclesPerFrame;
    }

    private static byte[] LoadRom(string name)
    {
        using var stream = typeof(Kim1Board).Assembly.GetManifestResourceStream($"Kimulator.Kim1.Roms.{name}")
            ?? throw new InvalidOperationException($"Embedded ROM {name} not found.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
