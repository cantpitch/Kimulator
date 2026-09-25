namespace Kimulator.Kim1;

/// <summary>
/// MOS 6530 RRIOT: 1 KB mask ROM, 64 bytes RAM, two 8-bit I/O ports and an interval timer.
/// The board maps the three areas; this class only knows offsets within each.
/// </summary>
public sealed class Riot6530
{
    private static readonly int[] PrescaleShifts = [0, 3, 6, 10]; // ÷1, ÷8, ÷64, ÷1024

    private readonly byte[] _rom;
    private readonly byte[] _ram = new byte[64];

    private byte _timer;
    private int _prescaleShift;
    private int _prescaleCounter;
    private bool _timedOut;

    public Riot6530(string name, byte[] rom)
    {
        if (rom.Length != 1024)
            throw new ArgumentException($"A 6530 ROM must be 1024 bytes, got {rom.Length}.", nameof(rom));
        Name = name;
        _rom = rom;
        Reset();
    }

    public string Name { get; }

    public byte PortAData { get; private set; }
    public byte PortADirection { get; private set; }
    public byte PortBData { get; private set; }
    public byte PortBDirection { get; private set; }

    public bool TimerInterruptFlag { get; private set; }
    public bool TimerInterruptEnabled { get; private set; }

    /// <summary>True when the timer IRQ output (shared with PB7 on IRQ-option parts) is asserted.</summary>
    public bool IrqAsserted => TimerInterruptFlag && TimerInterruptEnabled;

    /// <summary>Levels driven onto the port A pins by external hardware (1 = high / pulled up).</summary>
    public Func<byte>? PortAInput { get; set; }

    /// <summary>Levels driven onto the port B pins by external hardware (1 = high / pulled up).</summary>
    public Func<byte>? PortBInput { get; set; }

    /// <summary>Raised after a write to a port data or direction register.</summary>
    public event Action? PortsChanged;

    /// <summary>Pin levels of port A: output bits from the latch, input bits from outside.</summary>
    public byte PortAPins => Combine(PortAData, PortADirection, PortAInput?.Invoke() ?? 0xFF);

    /// <summary>Pin levels of port B: output bits from the latch, input bits from outside.</summary>
    public byte PortBPins => Combine(PortBData, PortBDirection, PortBInput?.Invoke() ?? 0xFF);

    public byte[] Ram => _ram;

    /// <summary>The RES line clears the port registers (all pins become inputs) and the timer IRQ enable.</summary>
    public void Reset()
    {
        PortAData = PortADirection = PortBData = PortBDirection = 0;
        TimerInterruptEnabled = false;
        PortsChanged?.Invoke();
    }

    public byte ReadRom(int offset) => _rom[offset & 0x3FF];

    public byte ReadRam(int offset) => _ram[offset & 0x3F];

    public void WriteRam(int offset, byte value) => _ram[offset & 0x3F] = value;

    /// <summary>Advances the interval timer by one clock cycle.</summary>
    public void Tick()
    {
        if (--_prescaleCounter > 0)
            return;

        if (_timer == 0)
        {
            // Passing through zero sets the flag; from then on the timer counts at the clock rate.
            TimerInterruptFlag = true;
            _timedOut = true;
        }

        _timer--;
        _prescaleCounter = _timedOut ? 1 : 1 << _prescaleShift;
    }

    /// <summary>Reads an I/O / timer register. <paramref name="offset"/> is A0-A3.</summary>
    public byte ReadIo(int offset)
    {
        offset &= 0x0F;
        if ((offset & 0x04) == 0)
        {
            return (offset & 0x03) switch
            {
                0 => PortAPins,
                1 => PortADirection,
                2 => PortBPins,
                _ => PortBDirection,
            };
        }

        if ((offset & 0x01) == 0)
        {
            // Read timer; A3 sets the interrupt enable, and the access clears the flag.
            TimerInterruptEnabled = (offset & 0x08) != 0;
            TimerInterruptFlag = false;
            return _timer;
        }

        return TimerInterruptFlag ? (byte)0x80 : (byte)0x00;
    }

    /// <summary>Writes an I/O / timer register. <paramref name="offset"/> is A0-A3.</summary>
    public void WriteIo(int offset, byte value)
    {
        offset &= 0x0F;
        if ((offset & 0x04) == 0)
        {
            switch (offset & 0x03)
            {
                case 0: PortAData = value; break;
                case 1: PortADirection = value; break;
                case 2: PortBData = value; break;
                default: PortBDirection = value; break;
            }

            PortsChanged?.Invoke();
            return;
        }

        // Timer write: A0-A1 select the prescaler, A3 the interrupt enable.
        _timer = value;
        _prescaleShift = PrescaleShifts[offset & 0x03];
        _prescaleCounter = 1 << _prescaleShift;
        _timedOut = false;
        TimerInterruptEnabled = (offset & 0x08) != 0;
        TimerInterruptFlag = false;
    }

    private static byte Combine(byte latch, byte direction, byte external) =>
        (byte)((latch & direction) | (external & ~direction));
}
