using System.Text;
using System.Text.Json;
using Kimulator.Core.Cpu;

namespace Kimulator.Tests.Cpu;

/// <summary>
/// Tom Harte's SingleStepTests (https://github.com/SingleStepTests/65x02, 6502/v1): ~10,000 randomised
/// cases per opcode, each checking registers, memory and every individual bus cycle.
/// Fetch the data with scripts/fetch-test-data.sh; the tests skip if it is missing.
/// </summary>
public class HarteSingleStepTests
{
    private static readonly string DataDir = Path.Combine(TestPaths.RepoRoot, "test-data", "harte", "6502");

    // JAM opcodes: the suite records an arbitrary number of trailing $FFFF/$FFFE reads while the
    // CPU is hung, so only the first two cycles and the final state are compared.
    private static readonly HashSet<int> JamOpcodes = [0x02, 0x12, 0x22, 0x32, 0x42, 0x52, 0x62, 0x72, 0x92, 0xB2, 0xD2, 0xF2];

    public static TheoryData<int> AllOpcodes()
    {
        var data = new TheoryData<int>();
        for (int i = 0; i < 256; i++) data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllOpcodes))]
    public void Opcode(int opcode)
    {
        string file = Path.Combine(DataDir, $"{opcode:x2}.json");
        if (!File.Exists(file))
            Assert.Skip($"Harte test data not found at {file}. Run scripts/fetch-test-data.sh.");

        using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
        var failures = new StringBuilder();
        int failed = 0, total = 0;

        var bus = new RecordingBus();

        foreach (var test in doc.RootElement.EnumerateArray())
        {
            total++;
            string? error = RunCase(bus, test, JamOpcodes.Contains(opcode));
            if (error is null) continue;
            if (++failed <= 5)
                failures.AppendLine($"[{test.GetProperty("name").GetString()}] {error}");
        }

        Assert.True(failed == 0, $"Opcode ${opcode:X2}: {failed}/{total} cases failed.\n{failures}");
    }

    private static string? RunCase(RecordingBus bus, JsonElement test, bool isJam)
    {
        var initial = test.GetProperty("initial");
        var final = test.GetProperty("final");

        foreach (var cell in initial.GetProperty("ram").EnumerateArray())
            bus.Memory[cell[0].GetInt32()] = (byte)cell[1].GetInt32();

        var cpu = new Cpu6502(bus)
        {
            PC = (ushort)initial.GetProperty("pc").GetInt32(),
            S = (byte)initial.GetProperty("s").GetInt32(),
            A = (byte)initial.GetProperty("a").GetInt32(),
            X = (byte)initial.GetProperty("x").GetInt32(),
            Y = (byte)initial.GetProperty("y").GetInt32(),
            P = (byte)initial.GetProperty("p").GetInt32(),
        };

        bus.Cycles.Clear();
        cpu.Step();

        var errors = new StringBuilder();
        Check(errors, "PC", final.GetProperty("pc").GetInt32(), cpu.PC);
        Check(errors, "S", final.GetProperty("s").GetInt32(), cpu.S);
        Check(errors, "A", final.GetProperty("a").GetInt32(), cpu.A);
        Check(errors, "X", final.GetProperty("x").GetInt32(), cpu.X);
        Check(errors, "Y", final.GetProperty("y").GetInt32(), cpu.Y);
        // B and bit 5 do not exist in the register itself; they only appear when P is pushed.
        Check(errors, "P", final.GetProperty("p").GetInt32() & 0xCF, cpu.P & 0xCF);

        foreach (var cell in final.GetProperty("ram").EnumerateArray())
            Check(errors, $"[{cell[0].GetInt32():X4}]", cell[1].GetInt32(), bus.Memory[cell[0].GetInt32()]);

        var expected = test.GetProperty("cycles").EnumerateArray().ToList();
        int compare = isJam ? 2 : expected.Count;
        if (!isJam && expected.Count != bus.Cycles.Count)
            errors.Append($" cycles: expected {expected.Count}, got {bus.Cycles.Count};");

        for (int i = 0; i < Math.Min(compare, bus.Cycles.Count); i++)
        {
            var e = expected[i];
            var (addr, value, isWrite) = bus.Cycles[i];
            bool expWrite = e[2].GetString() == "write";
            if (e[0].GetInt32() != addr || e[1].GetInt32() != value || expWrite != isWrite)
            {
                errors.Append($" cycle {i}: expected {(expWrite ? "W" : "R")} {e[0].GetInt32():X4}={e[1].GetInt32():X2}, " +
                              $"got {(isWrite ? "W" : "R")} {addr:X4}={value:X2};");
                break;
            }
        }

        // Clear touched memory for the next case.
        foreach (var cell in initial.GetProperty("ram").EnumerateArray())
            bus.Memory[cell[0].GetInt32()] = 0;
        foreach (var (addr, _, _) in bus.Cycles)
            bus.Memory[addr] = 0;

        return errors.Length == 0 ? null : errors.ToString();
    }

    private static void Check(StringBuilder errors, string what, int expected, int actual)
    {
        if (expected != actual)
            errors.Append($" {what}: expected {expected:X2}, got {actual:X2};");
    }
}
