using Kimulator.Core.Audio;
using Kimulator.Core.Formats;
using Kimulator.Kim1;

namespace Kimulator.Tests.Kim1;

public class CassetteTests
{
    private static readonly byte[] Program = [.. Enumerable.Range(0, 24).Select(i => (byte)(i * 11 + 3))];

    [Fact]
    public void MonitorDumpsToTapeAndLoadsItBack()
    {
        var board = Kim1Board.CreateWithDefaultRoms();
        board.PowerOn();
        board.RunUntil(50_000);
        board.LoadMemory(0x0200, Program);

        // DUMPT parameters: SAL/SAH = start, EAL/EAH = end + 1, ID.
        board.Poke(0x17F5, 0x00);
        board.Poke(0x17F6, 0x02);
        board.Poke(0x17F7, (byte)Program.Length);
        board.Poke(0x17F8, 0x02);
        board.Poke(0x17F9, 0x42);

        board.Cassette.Record(board.Cycles);
        board.Cpu.PC = 0x1800; // DUMPT
        RunUntil(board, () => board.Cassette.State == TapeState.Stopped, seconds: 60);

        var recorded = board.Cassette.GetTape();
        Assert.True(recorded.Length > Cassette.RecordSampleRate * 3, $"Tape is only {recorded.Length} samples.");

        // Round-trip the tape through a 16-bit WAV file.
        var wav = WavFile.Read(WavFile.Write(recorded, Cassette.RecordSampleRate));
        Assert.Equal(Cassette.RecordSampleRate, wav.SampleRate);

        // Wipe the program, then load it with LOADT, asking for ID $42.
        board.LoadMemory(0x0200, new byte[Program.Length]);
        board.Poke(0xFA, 0x55);
        board.Poke(0xFB, 0x55);
        board.Poke(0x17F9, 0x42);
        board.Cassette.Insert(wav.Samples, wav.SampleRate);
        board.Cassette.Play();
        board.Cpu.PC = 0x1873; // LOADT
        RunUntil(board, () => board.Peek(0xFA) != 0x55, seconds: 60);

        Assert.Equal(0x00, board.Peek(0xFA)); // POINT = 0000 means success, FFFF a checksum error
        Assert.Equal(0x00, board.Peek(0xFB));
        Assert.Equal(Program, board.Ram[0x200..(0x200 + Program.Length)]);
    }

    [Fact]
    public void LoadsFromANoisyQuieterTape()
    {
        // Record, then degrade: lower level, DC offset and noise, like a real cassette capture.
        var board = Kim1Board.CreateWithDefaultRoms();
        board.PowerOn();
        board.RunUntil(50_000);
        board.LoadMemory(0x0200, Program);
        board.Poke(0x17F5, 0x00);
        board.Poke(0x17F6, 0x02);
        board.Poke(0x17F7, (byte)Program.Length);
        board.Poke(0x17F8, 0x02);
        board.Poke(0x17F9, 0x01);
        board.Cassette.Record(board.Cycles);
        board.Cpu.PC = 0x1800;
        RunUntil(board, () => board.Cassette.State == TapeState.Stopped, seconds: 60);

        var random = new Random(1);
        var tape = board.Cassette.GetTape().Select(s => s * 0.3f + 0.1f + (float)(random.NextDouble() - 0.5) * 0.04f).ToArray();

        board.LoadMemory(0x0200, new byte[Program.Length]);
        board.Poke(0xFA, 0x55);
        board.Poke(0x17F9, 0x00); // ID 00: load any record
        board.Cassette.Insert(tape, Cassette.RecordSampleRate);
        board.Cassette.Play();
        board.Cpu.PC = 0x1873;
        RunUntil(board, () => board.Peek(0xFA) != 0x55, seconds: 60);

        Assert.Equal(0x00, board.Peek(0xFA));
        Assert.Equal(Program, board.Ram[0x200..(0x200 + Program.Length)]);
    }

    [Fact]
    public void RecordingStopsAfterSilence()
    {
        var board = Kim1Board.CreateWithDefaultRoms();
        board.PowerOn();
        board.Cassette.AutoStopSilenceSeconds = 0.5;
        board.Cassette.Record(board.Cycles);
        board.RunUntil(board.Cycles + 2_000_000);
        Assert.Equal(TapeState.Stopped, board.Cassette.State);
    }

    [Fact]
    public void WavReaderHandlesStereo8Bit()
    {
        // 8-bit stereo, 4 frames: channels are averaged to mono.
        byte[] header = WavFile.Write(new float[1], 8000)[..44];
        var bytes = header.ToArray();
        BitConverter.GetBytes((short)2).CopyTo(bytes, 22);   // channels
        BitConverter.GetBytes(8000 * 2).CopyTo(bytes, 28);   // byte rate
        BitConverter.GetBytes((short)2).CopyTo(bytes, 32);   // block align
        BitConverter.GetBytes((short)8).CopyTo(bytes, 34);   // bits
        BitConverter.GetBytes(8).CopyTo(bytes, 40);          // data size
        byte[] data = [255, 255, 0, 0, 128, 128, 255, 1];
        var audio = WavFile.Read([.. bytes, .. data]);
        Assert.Equal(4, audio.Samples.Length);
        Assert.True(audio.Samples[0] > 0.9f);
        Assert.True(audio.Samples[1] < -0.9f);
        Assert.Equal(0f, audio.Samples[2]);
        Assert.Equal(0f, audio.Samples[3], 0.01f);
    }

    [Fact]
    public void SpeakerProducesSamplesAtTheOutputRate()
    {
        var ring = new AudioRing(100_000);
        var sampler = new PinSampler(1_000_000, 44100, ring);
        for (int cycle = 0; cycle < 1_000_000; cycle++)
            sampler.Tick(cycle / 500 % 2 == 0); // 1 kHz square wave
        Assert.InRange(ring.Count, 44099, 44101);

        var samples = new float[ring.Count];
        ring.Read(samples);
        Assert.True(samples.Max() > 0.2f && samples.Min() < -0.2f);
    }

    private static void RunUntil(Kim1Board board, Func<bool> done, int seconds)
    {
        long limit = board.Cycles + seconds * 1_000_000L;
        while (!done())
        {
            Assert.True(board.Cycles < limit, "Timed out.");
            board.RunUntil(board.Cycles + 10_000);
        }
    }
}
