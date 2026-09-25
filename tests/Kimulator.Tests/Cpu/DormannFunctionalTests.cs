using Kimulator.Core.Cpu;

namespace Kimulator.Tests.Cpu;

/// <summary>
/// Klaus Dormann's 6502 functional test (https://github.com/Klaus2m5/6502_65C02_functional_tests).
/// The binary loops forever on the failing check ("trap"); success is the trap at $3469.
/// </summary>
public class DormannFunctionalTests
{
    private const ushort SuccessAddress = 0x3469;

    [Fact]
    public void FunctionalTestReachesSuccessTrap()
    {
        var bus = new RecordingBus { Record = false };
        File.ReadAllBytes(TestPaths.DataFile("6502_functional_test.bin")).CopyTo(bus.Memory, 0);
        var cpu = new Cpu6502(bus) { PC = 0x0400 };

        ushort lastPc = 0;
        for (long i = 0; i < 100_000_000; i++)
        {
            lastPc = cpu.PC;
            cpu.Step();
            if (cpu.PC == lastPc)
                break; // trapped: JMP * or branch-to-self
        }

        Assert.True(lastPc == SuccessAddress, $"Trapped at ${lastPc:X4} (success is ${SuccessAddress:X4}), after {cpu.Cycles} cycles.");
    }
}
