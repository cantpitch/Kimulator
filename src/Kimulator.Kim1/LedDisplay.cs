namespace Kimulator.Kim1;

/// <summary>
/// The six multiplexed 7-segment digits. Software lights one digit at a time, so instead of a
/// "current" value we integrate how many cycles each segment spends lit and turn that into brightness.
/// All members are called on the emulation thread except <see cref="Latest"/>.
/// </summary>
public sealed class LedDisplay
{
    public const int DigitCount = 6;
    public const int SegmentCount = 8; // a-g plus the (unwired on the KIM-1) decimal point slot

    private readonly long[] _onCycles = new long[DigitCount * SegmentCount];
    private int _digit = -1;
    private byte _segments;
    private long _lastChange;
    private long _frameStart;

    /// <summary>The most recent completed frame; safe to read from any thread.</summary>
    public volatile DisplayFrame Latest = DisplayFrame.Blank;

    /// <summary>Records a change in which digit is selected or which segments are driven.</summary>
    public void Update(long cycle, int digit, byte segments)
    {
        if (digit == _digit && segments == _segments)
            return;
        Accumulate(cycle);
        _digit = digit;
        _segments = segments;
    }

    /// <summary>Closes the current frame, publishes it to <see cref="Latest"/> and starts a new one.</summary>
    public DisplayFrame EndFrame(long cycle)
    {
        Accumulate(cycle);
        long length = Math.Max(1, cycle - _frameStart);
        var levels = new float[DigitCount * SegmentCount];
        for (int i = 0; i < levels.Length; i++)
        {
            levels[i] = (float)_onCycles[i] / length;
            _onCycles[i] = 0;
        }

        _frameStart = cycle;
        var frame = new DisplayFrame(levels);
        Latest = frame;
        return frame;
    }

    private void Accumulate(long cycle)
    {
        long elapsed = cycle - _lastChange;
        _lastChange = cycle;
        if (_digit < 0 || _segments == 0 || elapsed <= 0)
            return;

        int baseIndex = _digit * SegmentCount;
        for (int s = 0; s < SegmentCount; s++)
        {
            if ((_segments & (1 << s)) != 0)
                _onCycles[baseIndex + s] += elapsed;
        }
    }
}

/// <summary>
/// Duty cycle (0..1) of each segment over one frame, indexed [digit * 8 + segment], segment 0 = a ... 6 = g.
/// The KIM-1 monitor lights each digit roughly 1/6 of the time, so ~0.15 corresponds to "fully on".
/// </summary>
public sealed class DisplayFrame(float[] levels)
{
    public static readonly DisplayFrame Blank = new(new float[LedDisplay.DigitCount * LedDisplay.SegmentCount]);

    public float this[int digit, int segment] => levels[digit * LedDisplay.SegmentCount + segment];

    /// <summary>Segment bitmask of a digit counting a segment as lit when its duty cycle exceeds <paramref name="threshold"/>.</summary>
    public byte SegmentsOf(int digit, float threshold = 0.02f)
    {
        byte mask = 0;
        for (int s = 0; s < 7; s++)
        {
            if (this[digit, s] > threshold)
                mask |= (byte)(1 << s);
        }

        return mask;
    }
}
