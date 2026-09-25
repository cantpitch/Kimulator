using Avalonia.Platform;
using NLayer;

namespace Kimulator.App.Audio;

/// <summary>
/// The keypad click (assets/sounds/keypress.mp3). The recording holds a key going down and, a moment
/// later, coming back up; it is split at the silence between them so each half plays on the matching
/// key event.
/// </summary>
public sealed class KeyClickSound
{
    private const float SilenceLevel = 0.006f;   // about -45 dBFS
    private const double PreRollSeconds = 0.004;
    private const double MinGapSeconds = 0.05;

    private KeyClickSound(float[] press, float[] release)
    {
        Press = press;
        Release = release;
    }

    public float[] Press { get; }

    public float[] Release { get; }

    public static KeyClickSound Load(int outputRate)
    {
        using var stream = AssetLoader.Open(new Uri("avares://Kimulator/Assets/sounds/keypress.mp3"));
        using var mp3 = new MpegFile(stream);
        var interleaved = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = mp3.ReadSamples(buffer, 0, buffer.Length)) > 0)
            interleaved.AddRange(buffer.AsSpan(0, read));

        float[] mono = Resample(ToMono(interleaved, mp3.Channels), mp3.SampleRate, outputRate);
        var bursts = FindBursts(mono, outputRate);
        if (bursts.Count == 0) return new KeyClickSound([], []);

        float[] Slice((int Start, int End) b) => mono[b.Start..b.End];
        return new KeyClickSound(Slice(bursts[0]), bursts.Count > 1 ? Slice(bursts[1]) : []);
    }

    private static float[] ToMono(List<float> interleaved, int channels)
    {
        var mono = new float[interleaved.Count / channels];
        for (int i = 0; i < mono.Length; i++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
            mono[i] = sum / channels;
        }

        return mono;
    }

    private static float[] Resample(float[] input, int from, int to)
    {
        if (from == to || input.Length == 0) return input;
        var output = new float[(int)((long)input.Length * to / from)];
        for (int i = 0; i < output.Length; i++)
        {
            double pos = (double)i * from / to;
            int j = (int)pos;
            double t = pos - j;
            output[i] = (float)(input[Math.Min(j, input.Length - 1)] * (1 - t) + input[Math.Min(j + 1, input.Length - 1)] * t);
        }

        return output;
    }

    /// <summary>Stretches of sound separated by at least <see cref="MinGapSeconds"/> of silence.</summary>
    private static List<(int Start, int End)> FindBursts(float[] samples, int rate)
    {
        var bursts = new List<(int, int)>();
        int gap = (int)(MinGapSeconds * rate), preRoll = (int)(PreRollSeconds * rate);
        int start = -1, lastLoud = -1;
        for (int i = 0; i < samples.Length; i++)
        {
            if (Math.Abs(samples[i]) < SilenceLevel) continue;
            if (start >= 0 && i - lastLoud > gap)
            {
                bursts.Add((Math.Max(0, start - preRoll), Math.Min(samples.Length, lastLoud + preRoll)));
                start = -1;
            }

            if (start < 0) start = i;
            lastLoud = i;
        }

        if (start >= 0) bursts.Add((Math.Max(0, start - preRoll), Math.Min(samples.Length, lastLoud + preRoll)));
        return bursts;
    }
}
