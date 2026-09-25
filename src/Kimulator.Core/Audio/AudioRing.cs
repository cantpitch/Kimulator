namespace Kimulator.Core.Audio;

/// <summary>
/// Single-producer / single-consumer ring of audio samples: the emulation thread writes,
/// the audio thread reads. When full, new samples are dropped (the emulator is running ahead).
/// </summary>
public sealed class AudioRing(int capacity)
{
    private readonly float[] _buffer = new float[capacity];
    private long _written; // total samples written (producer)
    private long _read;    // total samples read (consumer)

    public int Capacity => _buffer.Length;

    public int Count => (int)(Volatile.Read(ref _written) - Volatile.Read(ref _read));

    public void Write(float sample)
    {
        long w = _written;
        if (w - Volatile.Read(ref _read) >= _buffer.Length) return;
        _buffer[w % _buffer.Length] = sample;
        Volatile.Write(ref _written, w + 1);
    }

    /// <summary>Copies up to <paramref name="destination"/>.Length samples; returns how many were available.</summary>
    public int Read(Span<float> destination)
    {
        long r = _read;
        int n = (int)Math.Min(destination.Length, Volatile.Read(ref _written) - r);
        for (int i = 0; i < n; i++) destination[i] = _buffer[(r + i) % _buffer.Length];
        Volatile.Write(ref _read, r + n);
        return n;
    }

    /// <summary>Discards the oldest samples so at most <paramref name="keep"/> remain (bounds latency).</summary>
    public void Trim(int keep)
    {
        long excess = Count - keep;
        if (excess > 0) Volatile.Write(ref _read, _read + excess);
    }
}
