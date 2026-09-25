namespace Kimulator.Kim1;

/// <summary>
/// The KIM-1's 20 mA current-loop teletype interface, modelled at the bit level.
/// <para>
/// The monitor bit-bangs the serial protocol in software: it reads PA7 and writes PB0 using delay
/// loops it calibrates from the start bit of the first character typed after reset (RUBOUT).
/// This class turns queued characters into a waveform on the PA7 input and decodes PB0 back into
/// characters, both in emulated clock cycles, so timing behaves as it would on the real machine.
/// </para>
/// <para>
/// The monitor never echoes in software; the board echoes in hardware by gating the incoming data
/// onto the outgoing loop while PB5 ("DATIN") is low. <see cref="HardwareEcho"/> models that.
/// </para>
/// Called only from the emulation thread.
/// </summary>
public sealed class TtyInterface
{
    public const byte Rubout = 0x7F;

    private readonly double _clockHz;
    private readonly Queue<byte> _input = new();

    private double _cyclesPerBit;
    private long _cycle;

    // Receiver side of the KIM (our transmitter): PA7 waveform.
    private bool _rxActive;
    private long _rxStart;
    private byte _rxByte;
    private long _rxNotBefore;
    private bool _rxLevel = true;

    // Transmitter side of the KIM (our receiver): decode PB0.
    private bool _txActive;
    private long _txStart;
    private int _txBit;
    private int _txValue;
    private bool _txPrevLine = true;
    private long _txLastActivity;

    public TtyInterface(double clockHz, int baudRate = 1200)
    {
        _clockHz = clockHz;
        BaudRate = baudRate;
    }

    /// <summary>Line speed. The monitor adapts to whatever rate the first character arrives at.</summary>
    public int BaudRate
    {
        get;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            field = value;
            _cyclesPerBit = _clockHz / value;
        }
    }

    /// <summary>Stop bits sent after each character (a Teletype ASR-33 sends 2).</summary>
    public int StopBits { get; set; } = 2;

    /// <summary>Models the base board's echo of incoming data onto the output loop (enabled while PB5 is low).</summary>
    public bool HardwareEcho { get; set; } = true;

    /// <summary>
    /// Before sending the next character, wait until the KIM's output line has been idle for this many
    /// character times. The monitor has no receive buffer, so characters that arrive while it is busy
    /// printing are lost; pacing on output makes typing ahead and pasting reliable.
    /// </summary>
    public double IdleCharactersBeforeSend { get; set; } = 1.0;

    /// <summary>Current level of the serial input pin (PA7). True = mark (idle).</summary>
    public bool RxLevel => _rxLevel;

    /// <summary>Characters queued but not yet sent.</summary>
    public int PendingInput => _input.Count + (_rxActive ? 1 : 0);

    /// <summary>Raised on the emulation thread for every character the KIM sends (8 bits as sent).</summary>
    public event Action<byte>? CharacterReceived;

    public void Send(byte value) => _input.Enqueue(value);

    public void Send(ReadOnlySpan<byte> values)
    {
        foreach (byte b in values) _input.Enqueue(b);
    }

    /// <summary>Drops queued input and any character in flight.</summary>
    public void ClearInput()
    {
        _input.Clear();
        _rxActive = false;
        _rxLevel = true;
    }

    /// <summary>
    /// Queues a RUBOUT after <paramref name="delayCycles"/> so the monitor, waiting after reset,
    /// can measure the bit time (what a user does by pressing RUBOUT on the teletype).
    /// </summary>
    public void SendCalibrationRubout(long delayCycles)
    {
        ClearInput();
        _rxNotBefore = _cycle + delayCycles;
        _input.Enqueue(Rubout);
    }

    /// <summary>Resets line state (used after loading a save state).</summary>
    public void ResetLine()
    {
        ClearInput();
        _txActive = false;
        _txPrevLine = true;
        _txLastActivity = _cycle;
    }

    /// <summary>Advances one clock cycle given the current PB0 (serial out) and PB5 (DATIN) pin levels.</summary>
    public void Tick(long cycle, bool pb0, bool pb5)
    {
        _cycle = cycle;
        UpdateTransmitter(cycle);

        bool line = pb0 && (!HardwareEcho || pb5 || _rxLevel);
        UpdateReceiver(cycle, line);
    }

    private void UpdateTransmitter(long cycle)
    {
        if (!_rxActive)
        {
            if (_input.Count == 0 || cycle < _rxNotBefore) return;
            double idle = IdleCharactersBeforeSend * 10 * _cyclesPerBit;
            if (IdleCharactersBeforeSend > 0 && (_txActive || cycle - _txLastActivity < idle)) return;

            _rxByte = _input.Dequeue();
            _rxStart = cycle;
            _rxActive = true;
        }

        long bit = (long)((cycle - _rxStart) / _cyclesPerBit);
        if (bit == 0)
        {
            _rxLevel = false; // start bit
        }
        else if (bit <= 8)
        {
            _rxLevel = ((_rxByte >> (int)(bit - 1)) & 1) != 0;
        }
        else
        {
            _rxLevel = true; // stop bits
            if (bit >= 9 + StopBits)
            {
                _rxActive = false;
                _rxNotBefore = cycle;
            }
        }
    }

    private void UpdateReceiver(long cycle, bool line)
    {
        if (!line) _txLastActivity = cycle;

        if (!_txActive)
        {
            if (_txPrevLine && !line)
            {
                _txActive = true;
                _txStart = cycle;
                _txBit = 0;
                _txValue = 0;
            }
        }
        else if (cycle >= _txStart + (long)((_txBit + 1.5) * _cyclesPerBit))
        {
            // Sample the middle of each data bit, then the stop bit.
            if (_txBit < 8)
            {
                if (line) _txValue |= 1 << _txBit;
                _txBit++;
            }
            else
            {
                _txActive = false;
                _txLastActivity = cycle;
                if (line) CharacterReceived?.Invoke((byte)_txValue);
            }
        }

        _txPrevLine = line;
    }
}
