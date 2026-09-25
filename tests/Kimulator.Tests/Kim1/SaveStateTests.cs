using Kimulator.Kim1;

namespace Kimulator.Tests.Kim1;

public class SaveStateTests
{
    [Fact]
    public void RestoredMachineContinuesIdentically()
    {
        var original = Kim1Board.CreateWithDefaultRoms();
        original.PowerOn();
        // A program that keeps RAM, the timer and the stack busy:
        // loop: INC $10 / LDA $1746 (timer) / STA $11 / PHA / PLA / STA $1744 (restart timer ÷1) / JMP loop
        byte[] program = [0xE6, 0x10, 0xAD, 0x46, 0x17, 0x85, 0x11, 0x48, 0x68, 0x8D, 0x44, 0x17, 0x4C, 0x00, 0x02];
        for (int i = 0; i < program.Length; i++) original.Poke((ushort)(0x0200 + i), program[i]);
        original.RunUntil(20_000);
        original.Cpu.PC = 0x0200;
        original.RunUntil(original.Cycles + 12_345);

        using var state = new MemoryStream();
        original.SaveState(state);

        var restored = Kim1Board.CreateWithDefaultRoms();
        state.Position = 0;
        restored.LoadState(state);

        long target = original.Cycles + 50_000;
        original.RunUntil(target);
        restored.RunUntil(target);

        Assert.Equal(original.Cycles, restored.Cycles);
        Assert.Equal(original.Cpu.PC, restored.Cpu.PC);
        Assert.Equal(original.Cpu.A, restored.Cpu.A);
        Assert.Equal(original.Cpu.P, restored.Cpu.P);
        Assert.Equal(original.Ram, restored.Ram);
        Assert.Equal(original.Riot002.ReadIo(0x06), restored.Riot002.ReadIo(0x06));
    }

    [Fact]
    public void RejectsForeignData()
    {
        var board = Kim1Board.CreateWithDefaultRoms();
        Assert.Throws<InvalidDataException>(() => board.LoadState(new MemoryStream(new byte[64])));
    }
}
