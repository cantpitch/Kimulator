using System.Buffers.Binary;
using System.Text;

namespace Kimulator.Core.Formats;

/// <summary>Minimal RIFF/WAVE reader (PCM 8/16/24/32-bit, IEEE float, extensible; any channel count) and 16-bit mono writer.</summary>
public static class WavFile
{
    public sealed record Audio(float[] Samples, int SampleRate)
    {
        public double DurationSeconds => (double)Samples.Length / SampleRate;
    }

    /// <summary>Reads a WAV file and mixes all channels down to mono samples in -1..1.</summary>
    public static Audio Read(byte[] data)
    {
        if (data.Length < 12 || Encoding.ASCII.GetString(data, 0, 4) != "RIFF" || Encoding.ASCII.GetString(data, 8, 4) != "WAVE")
            throw new FormatException("Not a WAV file.");

        int format = 0, channels = 0, rate = 0, bits = 0;
        int pos = 12;
        while (pos + 8 <= data.Length)
        {
            string id = Encoding.ASCII.GetString(data, pos, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos + 4));
            int body = pos + 8;
            if (size < 0 || body + size > data.Length) size = data.Length - body; // tolerate truncated files

            if (id == "fmt ")
            {
                var fmt = data.AsSpan(body, size);
                format = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(fmt[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
                if (format == 0xFFFE && size >= 26) format = BinaryPrimitives.ReadUInt16LittleEndian(fmt[24..]); // extensible: subformat GUID
            }
            else if (id == "data")
            {
                if (channels == 0) throw new FormatException("WAV data comes before its format chunk.");
                return new Audio(Decode(data.AsSpan(body, size), format, channels, bits), rate);
            }

            pos = body + size + (size & 1);
        }

        throw new FormatException("WAV file has no data.");
    }

    private static float[] Decode(ReadOnlySpan<byte> pcm, int format, int channels, int bits)
    {
        int bytesPerSample = bits / 8;
        if (format is not (1 or 3) || bytesPerSample is < 1 or > 4 || (format == 3 && bits != 32))
            throw new FormatException($"Unsupported WAV encoding (format {format}, {bits} bits).");

        int frameSize = bytesPerSample * channels;
        int frames = pcm.Length / frameSize;
        var samples = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                var s = pcm.Slice(f * frameSize + c * bytesPerSample, bytesPerSample);
                sum += (format, bits) switch
                {
                    (3, _) => BinaryPrimitives.ReadSingleLittleEndian(s),
                    (_, 8) => (s[0] - 128) / 128f,
                    (_, 16) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                    (_, 24) => ((s[0] | s[1] << 8 | (sbyte)s[2] << 16)) / 8388608f,
                    _ => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
                };
            }

            samples[f] = sum / channels;
        }

        return samples;
    }

    /// <summary>Writes mono samples (-1..1) as a 16-bit PCM WAV file.</summary>
    public static byte[] Write(ReadOnlySpan<float> samples, int sampleRate)
    {
        int dataSize = samples.Length * 2;
        var bytes = new byte[44 + dataSize];
        var span = bytes.AsSpan();
        Encoding.ASCII.GetBytes("RIFF").CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataSize);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);           // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);           // mono
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);           // block align
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);          // bits
        Encoding.ASCII.GetBytes("data").CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataSize);
        for (int i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(span[(44 + i * 2)..], (short)Math.Clamp(samples[i] * 32767f, -32768f, 32767f));
        return bytes;
    }
}
