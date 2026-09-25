namespace Kimulator.Core.Audio;

/// <summary>
/// Turns a digital pin sampled every CPU cycle into audio: each output sample is the average pin
/// level over its period (a box filter against aliasing), followed by a DC-blocking high-pass
/// the way a speaker's coupling capacitor would.
/// </summary>
public sealed class PinSampler(double clockHz, int sampleRate, AudioRing output)
{
    private readonly long _clock = (long)clockHz;
    private long _phase;
    private int _high;
    private int _cycles;
    private float _prevIn;
    private float _prevOut;

    public AudioRing Output => output;

    public int SampleRate => sampleRate;

    /// <summary>0..1 output volume.</summary>
    public float Volume { get; set; } = 0.5f;

    public void Tick(bool level)
    {
        if (level) _high++;
        _cycles++;
        _phase += sampleRate;
        if (_phase < _clock) return;
        _phase -= _clock;

        float x = (float)_high / _cycles;
        _high = _cycles = 0;
        float y = x - _prevIn + 0.995f * _prevOut;
        _prevIn = x;
        _prevOut = y;
        output.Write(y * Volume);
    }
}
