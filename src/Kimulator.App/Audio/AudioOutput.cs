using System.Collections.Concurrent;
using Kimulator.Core.Audio;
using Silk.NET.OpenAL;

namespace Kimulator.App.Audio;

/// <summary>
/// Streams samples from an <see cref="AudioRing"/> to the default sound device through OpenAL Soft
/// (bundled for Windows, macOS and Linux). All OpenAL calls happen on one dedicated thread.
/// When the emulator is paused or slow, silence fills the gaps; when it runs ahead the backlog is trimmed.
/// </summary>
public sealed unsafe class AudioOutput : IDisposable
{
    private const int BufferCount = 4;
    private const int BufferSamples = 512; // ~12 ms each at 44.1 kHz, so clicks feel immediate

    private readonly AudioRing _ring;
    private readonly int _sampleRate;
    private readonly Thread _thread;
    private volatile bool _stop;
    private readonly ConcurrentQueue<(float[] Samples, float Volume)> _newEffects = new();
    private readonly List<(float[] Samples, float Volume, int Position)> _effects = [];

    public AudioOutput(AudioRing ring, int sampleRate)
    {
        _ring = ring;
        _sampleRate = sampleRate;
        _thread = new Thread(Run) { IsBackground = true, Name = "Audio" };
        _thread.Start();
    }

    /// <summary>Null while starting or when running fine; otherwise why sound is unavailable.</summary>
    public string? Error { get; private set; }

    /// <summary>Mixes a one-shot sound (mono, at the output rate) over the emulator's audio. Any thread.</summary>
    public void PlayEffect(float[] samples, float volume)
    {
        if (samples.Length > 0) _newEffects.Enqueue((samples, volume));
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(500);
    }

    private void Run()
    {
        ALContext? alc = null;
        AL? al = null;
        Device* device = null;
        Context* context = null;
        uint source = 0;
        uint[] buffers = new uint[BufferCount];
        try
        {
            alc = ALContext.GetApi(soft: true);
            al = AL.GetApi(soft: true);
            device = alc.OpenDevice("");
            if (device == null) throw new InvalidOperationException("No audio output device.");
            context = alc.CreateContext(device, null);
            alc.MakeContextCurrent(context);

            source = al.GenSource();
            for (int i = 0; i < BufferCount; i++)
            {
                buffers[i] = al.GenBuffer();
                Fill(al, buffers[i]);
            }

            al.SourceQueueBuffers(source, buffers);
            al.SourcePlay(source);

            var one = new uint[1];
            while (!_stop)
            {
                al.GetSourceProperty(source, GetSourceInteger.BuffersProcessed, out int processed);
                while (processed-- > 0)
                {
                    al.SourceUnqueueBuffers(source, one);
                    Fill(al, one[0]);
                    al.SourceQueueBuffers(source, one);
                }

                al.GetSourceProperty(source, GetSourceInteger.SourceState, out int state);
                if (state != (int)SourceState.Playing) al.SourcePlay(source);
                Thread.Sleep(5);
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            if (al is not null && source != 0)
            {
                al.SourceStop(source);
                al.DeleteSource(source);
                al.DeleteBuffers(buffers);
            }

            if (alc is not null)
            {
                if (context != null)
                {
                    alc.MakeContextCurrent(null);
                    alc.DestroyContext(context);
                }

                if (device != null) alc.CloseDevice(device);
            }
        }
    }

    private readonly float[] _floats = new float[BufferSamples];
    private readonly short[] _pcm = new short[BufferSamples];

    private void Fill(AL al, uint buffer)
    {
        // Keep latency bounded: if the emulator got ahead (fast-forward, hiccup), drop the backlog.
        if (_ring.Count > _sampleRate / 6) _ring.Trim(_sampleRate / 20);

        int n = _ring.Read(_floats);
        for (int i = n; i < BufferSamples; i++) _floats[i] = 0f;

        while (_newEffects.TryDequeue(out var effect)) _effects.Add((effect.Samples, effect.Volume, 0));
        for (int e = _effects.Count - 1; e >= 0; e--)
        {
            var (samples, volume, position) = _effects[e];
            int count = Math.Min(BufferSamples, samples.Length - position);
            for (int i = 0; i < count; i++) _floats[i] += samples[position + i] * volume;
            if (position + count >= samples.Length) _effects.RemoveAt(e);
            else _effects[e] = (samples, volume, position + count);
        }

        for (int i = 0; i < BufferSamples; i++)
            _pcm[i] = (short)(Math.Clamp(_floats[i], -1f, 1f) * 32767f);

        al.BufferData(buffer, BufferFormat.Mono16, _pcm, _sampleRate);
    }
}
