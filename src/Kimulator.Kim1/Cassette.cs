namespace Kimulator.Kim1;

public enum TapeState { Stopped, Playing, Recording }

/// <summary>
/// A cassette recorder on the KIM-1's audio interface.
/// <para>
/// Recording samples the tape output (6530-002 PB7 driven as an output) at 44.1 kHz. Playback feeds
/// the tape through a model of the board's LM565 phase-locked loop: the PLL is tuned near 3 kHz, so
/// the monitor's 3.6 kHz tone and 2.4 kHz tone come out as opposite logic levels on PB7 (as an input),
/// which the monitor times in software. Any WAV of a KIM tape can be played.
/// </para>
/// Timing is in emulated cycles, so tapes load faster when the emulator runs unthrottled.
/// Called only from the emulation thread.
/// </summary>
public sealed class Cassette(double clockHz)
{
    public const int RecordSampleRate = 44100;

    /// <summary>Frequency (Hz) separating the two tape tones; PLLCAL calibrates the real PLL to ~3 kHz.</summary>
    public const double PllCenterHz = 3000;

    private readonly long _clock = (long)clockHz;
    private readonly List<float> _recording = [];

    private float[] _tape = [];
    private int _tapeRate = RecordSampleRate;
    private int _position;       // next tape sample
    private long _phase;

    // Recording box filter
    private int _high, _cycles;

    // PLL model
    private float _dcIn, _dcOut, _envelope;
    private bool _schmitt;
    private long _sampleCount;
    private long _lastCrossing;
    private double _lastHalfPeriod;
    private long _lastRecordActivity;

    public TapeState State { get; private set; }

    /// <summary>What the PLL presents on PB7 (true = high). The low tone reads as high.</summary>
    public bool PllOutput { get; private set; } = true;

    /// <summary>Stop recording automatically after this many seconds without any change on the tape output (0 = never).</summary>
    public double AutoStopSilenceSeconds { get; set; } = 2.0;

    public int TapeSampleRate => _tapeRate;

    public double PositionSeconds => (double)(State == TapeState.Recording ? _recording.Count : _position) / (State == TapeState.Recording ? RecordSampleRate : _tapeRate);

    public double LengthSeconds => State == TapeState.Recording ? PositionSeconds : (double)_tape.Length / _tapeRate;

    public bool HasTape => _tape.Length > 0;

    /// <summary>Raised on the emulation thread when the tape stops by itself (end of tape, or silence while recording).</summary>
    public event Action? StoppedAutomatically;

    /// <summary>Puts a recording on the deck (rewound).</summary>
    public void Insert(float[] samples, int sampleRate)
    {
        Stop();
        _tape = samples;
        _tapeRate = sampleRate;
        _position = 0;
    }

    /// <summary>The tape's contents, e.g. to save as a WAV file.</summary>
    public float[] GetTape() => State == TapeState.Recording ? [.. _recording] : _tape;

    public void Play()
    {
        if (!HasTape) return;
        if (_position >= _tape.Length) _position = 0;
        State = TapeState.Playing;
        _phase = 0;
        _sampleCount = _lastCrossing = 0;
        _lastHalfPeriod = 0;
        _envelope = 0;
    }

    /// <summary>Starts a new recording (replacing the tape).</summary>
    public void Record(long cycle)
    {
        Stop();
        _recording.Clear();
        _phase = 0;
        _high = _cycles = 0;
        _lastRecordActivity = cycle;
        State = TapeState.Recording;
    }

    public void Stop()
    {
        if (State == TapeState.Recording)
        {
            _tape = [.. _recording];
            _tapeRate = RecordSampleRate;
            _position = 0;
        }

        State = TapeState.Stopped;
        PllOutput = true;
    }

    public void Rewind()
    {
        if (State == TapeState.Recording) Stop();
        _position = 0;
    }

    /// <summary>Advances one CPU cycle. <paramref name="tapeOut"/> is the level of the tape output (PB7).</summary>
    public void Tick(long cycle, bool tapeOut, bool tapeOutChanged)
    {
        _phase += State == TapeState.Recording ? RecordSampleRate : _tapeRate;
        if (State == TapeState.Recording)
        {
            if (tapeOut) _high++;
            _cycles++;
            if (tapeOutChanged) _lastRecordActivity = cycle;
            if (_phase < _clock) return;
            _phase -= _clock;
            _recording.Add(((float)_high / _cycles * 2 - 1) * 0.8f);
            _high = _cycles = 0;

            if (AutoStopSilenceSeconds > 0 && cycle - _lastRecordActivity > AutoStopSilenceSeconds * _clock && _recording.Count > RecordSampleRate)
            {
                Stop();
                StoppedAutomatically?.Invoke();
            }

            return;
        }

        while (_phase >= _clock)
        {
            _phase -= _clock;
            if (_position >= _tape.Length)
            {
                Stop();
                StoppedAutomatically?.Invoke();
                return;
            }

            Pll(_tape[_position++]);
        }
    }

    /// <summary>
    /// PLL model: remove DC, square the signal with a Schmitt trigger relative to the signal envelope,
    /// measure the time between zero crossings and compare the frequency with the PLL's centre.
    /// </summary>
    private void Pll(float sample)
    {
        _sampleCount++;
        float x = sample - _dcIn + 0.999f * _dcOut; // DC block
        _dcIn = sample;
        _dcOut = x;

        float magnitude = Math.Abs(x);
        _envelope = Math.Max(magnitude, _envelope * 0.9995f);
        float hysteresis = Math.Max(0.02f, _envelope * 0.2f);

        bool crossed = false;
        if (!_schmitt && x > hysteresis) { _schmitt = true; crossed = true; }
        else if (_schmitt && x < -hysteresis) { _schmitt = false; crossed = true; }

        if (crossed)
        {
            double halfPeriod = (_sampleCount - _lastCrossing) / (double)_tapeRate;
            _lastCrossing = _sampleCount;
            double average = _lastHalfPeriod > 0 ? (halfPeriod + _lastHalfPeriod) / 2 : halfPeriod;
            _lastHalfPeriod = halfPeriod;
            PllOutput = 1 / (2 * average) < PllCenterHz;
        }
        else if ((_sampleCount - _lastCrossing) / (double)_tapeRate > 0.002)
        {
            PllOutput = true; // no signal: the PLL drifts to its low-frequency side
            _lastHalfPeriod = 0;
        }
    }
}
