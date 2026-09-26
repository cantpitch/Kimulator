using Kimulator.Core.Bus;

namespace Kimulator.Kim1.Cards;

/// <summary>An address range a card responds to.</summary>
public readonly record struct AddressRange(ushort Start, ushort End)
{
    public bool Contains(ushort address) => address >= Start && address <= End;

    public bool Overlaps(AddressRange other) => Start <= other.End && other.Start <= End;

    public override string ToString() => $"${Start:X4}–${End:X4}";
}

/// <summary>Base for KIM system cards: named, with ranges for the memory map and a saveable state.</summary>
public abstract class KimCard : IExpansionCard
{
    public abstract string Name { get; }

    /// <summary>Address ranges this card answers (for the memory map and conflict checks).</summary>
    public abstract IEnumerable<AddressRange> Ranges { get; }

    public abstract bool TryRead(ushort address, out byte value);

    public abstract bool TryWrite(ushort address, byte value);

    public virtual void Tick() { }

    public virtual void Reset() { }

    public virtual bool IrqAsserted => false;

    public virtual bool NmiAsserted => false;

    public virtual bool DisablesOnboardDecode(ushort address) => false;

    public virtual void SaveState(BinaryWriter writer) { }

    public virtual void LoadState(BinaryReader reader) { }
}

/// <summary>
/// KIM-2 (4K) or KIM-3 (8K) static RAM card. The base address comes from the card's address DIP switch,
/// exactly as in the MOS KIM-2/3 manual (Tables 4 and 5): switch 1 = A15, 2 = A14, 3 = A13, and on the
/// KIM-2 switch 4 = A12. "On" means the address bit is 1. Switch 4 of the KIM-3 is not connected.
/// </summary>
public sealed class RamCard : KimCard
{
    private readonly byte[] _ram;

    private RamCard(string name, int size, ushort baseAddress)
    {
        Name = name;
        _ram = new byte[size];
        BaseAddress = baseAddress;
    }

    public static RamCard Kim2(int switches) => new("KIM-2 4K RAM", 0x1000, Kim2Address(switches));

    public static RamCard Kim3(int switches) => new("KIM-3 8K RAM", 0x2000, Kim3Address(switches));

    /// <summary>Base address for KIM-2 switches (bit 0 = switch 1, set = on).</summary>
    public static ushort Kim2Address(int switches) =>
        (ushort)(Bit(switches, 0) << 15 | Bit(switches, 1) << 14 | Bit(switches, 2) << 13 | Bit(switches, 3) << 12);

    /// <summary>Base address for KIM-3 switches (bit 0 = switch 1, set = on).</summary>
    public static ushort Kim3Address(int switches) =>
        (ushort)(Bit(switches, 0) << 15 | Bit(switches, 1) << 14 | Bit(switches, 2) << 13);

    public override string Name { get; }

    public ushort BaseAddress { get; }

    public int Size => _ram.Length;

    public byte[] Memory => _ram;

    public override IEnumerable<AddressRange> Ranges => [new(BaseAddress, (ushort)(BaseAddress + _ram.Length - 1))];

    public override bool TryRead(ushort address, out byte value)
    {
        int offset = address - BaseAddress;
        if ((uint)offset < (uint)_ram.Length)
        {
            value = _ram[offset];
            return true;
        }

        value = 0;
        return false;
    }

    public override bool TryWrite(ushort address, byte value)
    {
        int offset = address - BaseAddress;
        if ((uint)offset >= (uint)_ram.Length) return false;
        _ram[offset] = value;
        return true;
    }

    public override void SaveState(BinaryWriter writer) => writer.Write(_ram);

    public override void LoadState(BinaryReader reader) => reader.ReadExactly(_ram);

    internal static int Bit(int value, int bit) => (value >> bit) & 1;
}

/// <summary>
/// KIM-5 ROM board: eight sockets for MOS 6540 2K mask ROMs in two banks of four. Each bank's 8K
/// block is set by its DIP switch (S1 → sockets U1–U4, S2 → U5–U8), switches 1–3 = A15–A13 like the KIM-3.
/// (This bank/switch mapping is inferred from the board and the ROM addresses; the KIM-5 manual isn't available.)
/// The Resident Assembler/Editor ROMs (6540-007/-008/-009) belong in U1–U3 with S1 at $E000.
/// </summary>
public sealed class RomCard : KimCard
{
    public const int SocketCount = 8;
    public const int RomSize = 0x800;

    private readonly byte[]?[] _sockets = new byte[SocketCount][];

    public RomCard(int bank1Switches, int bank2Switches)
    {
        Bank1Base = RamCard.Kim3Address(bank1Switches);
        Bank2Base = RamCard.Kim3Address(bank2Switches);
    }

    public override string Name => "KIM-5 ROM";

    public ushort Bank1Base { get; }

    public ushort Bank2Base { get; }

    public void Insert(int socket, byte[]? rom)
    {
        if (rom is not null && rom.Length != RomSize)
            throw new ArgumentException($"A 6540 ROM image is {RomSize} bytes; this one is {rom.Length}.", nameof(rom));
        _sockets[socket] = rom;
    }

    public bool IsOccupied(int socket) => _sockets[socket] is not null;

    /// <summary>Address of a socket's first byte.</summary>
    public ushort SocketAddress(int socket) => (ushort)((socket < 4 ? Bank1Base : Bank2Base) + (socket % 4) * RomSize);

    public override IEnumerable<AddressRange> Ranges =>
        Enumerable.Range(0, SocketCount).Where(IsOccupied)
            .Select(s => new AddressRange(SocketAddress(s), (ushort)(SocketAddress(s) + RomSize - 1)));

    public override bool TryRead(ushort address, out byte value)
    {
        value = 0;
        int socket;
        if (address >= Bank1Base && address - Bank1Base < 4 * RomSize) socket = (address - Bank1Base) / RomSize;
        else if (address >= Bank2Base && address - Bank2Base < 4 * RomSize) socket = 4 + (address - Bank2Base) / RomSize;
        else return false;

        if (_sockets[socket] is not { } rom) return false;
        value = rom[address % RomSize];
        return true;
    }

    public override bool TryWrite(ushort address, byte value) => false;
}

/// <summary>
/// MTU K-1008 Visible Memory: 8K of RAM that is also a 320 × 200 dot display. Each line is 40 bytes,
/// the first byte is the top left, the most significant bit is the leftmost dot and a 1 is a white dot
/// (K-1008 manual, "Programming"). The video circuit reads the RAM in the half cycle the 6502 leaves
/// the bus alone, so the display never slows the processor. The last 192 bytes aren't shown.
/// The 8K block is chosen with jumper pairs in socket S1 (manual, "Unpacking and installation"):
/// switch 1 or 2 selects A15 = 0 or 1, switch 3 or 4 A14, switch 5 or 6 A13 — exactly one of each pair on.
/// </summary>
public sealed class VisibleMemoryCard : KimCard
{
    public const int Width = 320;
    public const int Height = 200;
    public const int BytesPerLine = Width / 8;
    public const int DisplayBytes = BytesPerLine * Height;
    public const int Size = 0x2000;

    private readonly byte[] _ram = new byte[Size];

    public VisibleMemoryCard(int switches) => BaseAddress = Address(switches);

    public override string Name => "K-1008 Visible Memory";

    /// <summary>First address, or null when a switch pair has both or neither switch on.</summary>
    public ushort? BaseAddress { get; }

    /// <summary>The RAM, which is also the picture. The display reads it from the UI thread (tearing is harmless).</summary>
    public byte[] Memory => _ram;

    /// <summary>Base address for switch settings (bit 0 = switch 1, set = on), or null if a pair is set wrongly.</summary>
    public static ushort? Address(int switches)
    {
        int address = 0;
        for (int pair = 0; pair < 3; pair++)
        {
            switch ((switches >> (2 * pair)) & 3)
            {
                case 0b01: break;                               // odd switch on: address bit = 0
                case 0b10: address |= 0x8000 >> pair; break;    // even switch on: address bit = 1
                default: return null;
            }
        }

        return (ushort)address;
    }

    /// <summary>Switch settings for a base address on an 8K boundary.</summary>
    public static int SwitchesFor(ushort address)
    {
        int switches = 0;
        for (int pair = 0; pair < 3; pair++)
            switches |= ((address & (0x8000 >> pair)) != 0 ? 0b10 : 0b01) << (2 * pair);
        return switches;
    }

    public override IEnumerable<AddressRange> Ranges =>
        BaseAddress is { } start ? [new(start, (ushort)(start + Size - 1))] : [];

    public override bool TryRead(ushort address, out byte value)
    {
        int offset = address - (BaseAddress ?? -Size);
        if ((uint)offset < Size)
        {
            value = _ram[offset];
            return true;
        }

        value = 0;
        return false;
    }

    public override bool TryWrite(ushort address, byte value)
    {
        int offset = address - (BaseAddress ?? -Size);
        if ((uint)offset >= Size) return false;
        _ram[offset] = value;
        return true;
    }

    /// <summary>Whether the dot at (<paramref name="x"/>, <paramref name="y"/>) is lit; y = 0 is the top line.</summary>
    public bool Dot(int x, int y) => (_ram[y * BytesPerLine + x / 8] & (0x80 >> (x % 8))) != 0;

    public override void SaveState(BinaryWriter writer) => writer.Write(_ram);

    public override void LoadState(BinaryReader reader) => reader.ReadExactly(_ram);
}

/// <summary>
/// KIM-4 motherboard: six card slots, and the decode logic from the KIM-4 manual (section 3.3):
/// addresses $0400–$13FF and $2000–$FFF7 go to the motherboard and DECODE ENABLE switches the KIM-1
/// off, so the KIM-1's 8K image no longer repeats through memory, while $FFF8–$FFFF still reach the
/// KIM-1 so the reset/NMI/IRQ vectors stay under the monitor's control.
/// </summary>
public sealed class Kim4Motherboard : KimCard
{
    public const int SlotCount = 6;

    private readonly KimCard?[] _slots = new KimCard?[SlotCount];

    public override string Name => "KIM-4 Motherboard";

    public IReadOnlyList<KimCard?> Slots => _slots;

    public void Insert(int slot, KimCard? card) => _slots[slot] = card;

    public override IEnumerable<AddressRange> Ranges => _slots.OfType<KimCard>().SelectMany(c => c.Ranges);

    public static bool IsMotherboardAddress(ushort address) =>
        address is >= 0x0400 and <= 0x13FF || address is >= 0x2000 and <= 0xFFF7;

    public override bool DisablesOnboardDecode(ushort address) => IsMotherboardAddress(address);

    public override void Tick()
    {
        foreach (var card in _slots) card?.Tick();
    }

    public override void Reset()
    {
        foreach (var card in _slots) card?.Reset();
    }

    public override bool IrqAsserted => _slots.Any(c => c?.IrqAsserted == true);

    public override bool NmiAsserted => _slots.Any(c => c?.NmiAsserted == true);

    public override bool TryRead(ushort address, out byte value)
    {
        if (IsMotherboardAddress(address))
        {
            foreach (var card in _slots)
            {
                if (card is not null && card.TryRead(address, out value)) return true;
            }
        }

        value = 0;
        return false;
    }

    public override bool TryWrite(ushort address, byte value)
    {
        if (!IsMotherboardAddress(address)) return false;
        foreach (var card in _slots)
        {
            if (card is not null && card.TryWrite(address, value)) return true;
        }

        return false;
    }

    public override void SaveState(BinaryWriter writer)
    {
        foreach (var card in _slots)
        {
            writer.Write(card?.Name ?? "");
            card?.SaveState(writer);
        }
    }

    public override void LoadState(BinaryReader reader)
    {
        foreach (var card in _slots)
        {
            string name = reader.ReadString();
            if (name != (card?.Name ?? ""))
                throw new InvalidDataException("This state was saved with different cards in the KIM-4.");
            card?.LoadState(reader);
        }
    }
}
