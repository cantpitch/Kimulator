namespace Kimulator.Kim1.Cards;

public enum CardType { Kim2, Kim3, Kim5, K1008 }

/// <summary>One card and its settings. Switch values: bit 0 = switch 1, set = on.</summary>
public sealed class CardConfig
{
    public const string BuiltInPrefix = "builtin:";

    public CardType Type { get; set; }

    /// <summary>KIM-2/KIM-3 address switch, KIM-5 bank S1, or K-1008 socket S1.</summary>
    public int Switches { get; set; }

    /// <summary>KIM-5 bank S2.</summary>
    public int Switches2 { get; set; }

    /// <summary>KIM-5 sockets U1–U8: "builtin:6540-007" etc., a ROM file path, or null for an empty socket.</summary>
    public string?[] Sockets { get; set; } = new string?[RomCard.SocketCount];

    /// <summary>A card with the settings you'd normally start with.</summary>
    public static CardConfig Default(CardType type) => type switch
    {
        CardType.Kim2 => new CardConfig { Type = type, Switches = 0b0100 },   // $2000
        CardType.Kim3 => new CardConfig { Type = type, Switches = 0b0100 },   // $2000
        CardType.K1008 => new CardConfig { Type = type, Switches = VisibleMemoryCard.SwitchesFor(0x2000) }, // as shipped
        _ => new CardConfig
        {
            Type = type,
            Switches = 0b0111,  // S1: $E000 — the Resident Assembler/Editor
            Switches2 = 0b0011, // S2: $C000
            Sockets = [BuiltInPrefix + "6540-007", BuiltInPrefix + "6540-008", BuiltInPrefix + "6540-009", null, null, null, null, null],
        },
    };

    public static string DisplayName(CardType type) => type switch
    {
        CardType.Kim2 => "KIM-2  4K RAM",
        CardType.Kim3 => "KIM-3  8K RAM",
        CardType.K1008 => "K-1008  Visible Memory (320×200)",
        _ => "KIM-5  ROM (Resident Assembler/Editor)",
    };

    public CardConfig Clone() => new() { Type = Type, Switches = Switches, Switches2 = Switches2, Sockets = [.. Sockets] };
}

/// <summary>A region in the 64K map and who answers there.</summary>
public sealed record MapRegion(AddressRange Range, string Owner, bool OnKim1);

/// <summary>
/// Which KIM system cards are installed. Without a KIM-4 a single KIM-2 or KIM-3 can be cabled
/// straight to the KIM-1's expansion connector; the KIM-4 adds six slots and full address decoding.
/// </summary>
public sealed class ExpansionConfig
{
    public bool Kim4 { get; set; }

    /// <summary>Slot contents: six with a KIM-4, otherwise only the first (the expansion connector) is used.</summary>
    public List<CardConfig?> Slots { get; set; } = [];

    public int SlotCount => Kim4 ? Kim4Motherboard.SlotCount : 1;

    public CardConfig? Slot(int index) => index < Slots.Count ? Slots[index] : null;

    public void SetSlot(int index, CardConfig? card)
    {
        while (Slots.Count <= index) Slots.Add(null);
        Slots[index] = card;
    }

    public IEnumerable<(int Slot, CardConfig Card)> InstalledCards =>
        Enumerable.Range(0, SlotCount).Select(i => (i, Slot(i))).Where(x => x.Item2 is not null).Select(x => (x.i, x.Item2!));

    public ExpansionConfig Clone() => new() { Kim4 = Kim4, Slots = [.. Slots.Select(s => s?.Clone())] };

    /// <summary>Creates the cards for the board. <paramref name="loadRomFile"/> reads a socket's ROM file.</summary>
    public IReadOnlyList<KimCard> Build(Func<string, byte[]> loadRomFile)
    {
        var cards = InstalledCards.Select(c => (c.Slot, Card: BuildCard(c.Card, loadRomFile))).ToList();
        if (!Kim4) return cards.Select(c => c.Card).Take(1).ToList();

        var motherboard = new Kim4Motherboard();
        foreach (var (slot, card) in cards) motherboard.Insert(slot, card);
        return [motherboard];
    }

    public static KimCard BuildCard(CardConfig config, Func<string, byte[]> loadRomFile)
    {
        switch (config.Type)
        {
            case CardType.Kim2: return RamCard.Kim2(config.Switches);
            case CardType.Kim3: return RamCard.Kim3(config.Switches);
            case CardType.K1008: return new VisibleMemoryCard(config.Switches);
            default:
            {
                var rom = new RomCard(config.Switches, config.Switches2);
                for (int s = 0; s < RomCard.SocketCount; s++)
                {
                    if (config.Sockets.ElementAtOrDefault(s) is not { } source) continue;
                    rom.Insert(s, source.StartsWith(CardConfig.BuiltInPrefix)
                        ? Kim1Board.LoadBuiltInRom(source[CardConfig.BuiltInPrefix.Length..])
                        : loadRomFile(source));
                }

                return rom;
            }
        }
    }

    /// <summary>The address ranges a card config occupies (for a KIM-5, only occupied sockets).</summary>
    public static IEnumerable<AddressRange> RangesOf(CardConfig config) => config.Type switch
    {
        CardType.Kim2 => [new(RamCard.Kim2Address(config.Switches), (ushort)(RamCard.Kim2Address(config.Switches) + 0x0FFF))],
        CardType.Kim3 => [new(RamCard.Kim3Address(config.Switches), (ushort)(RamCard.Kim3Address(config.Switches) + 0x1FFF))],
        CardType.K1008 => VisibleMemoryCard.Address(config.Switches) is { } vm ? [new(vm, (ushort)(vm + VisibleMemoryCard.Size - 1))] : [],
        _ => Enumerable.Range(0, RomCard.SocketCount)
            .Where(s => config.Sockets.ElementAtOrDefault(s) is not null)
            .Select(s =>
            {
                ushort start = (ushort)(RamCard.Kim3Address(s < 4 ? config.Switches : config.Switches2) + (s % 4) * RomCard.RomSize);
                return new AddressRange(start, (ushort)(start + RomCard.RomSize - 1));
            }),
    };

    /// <summary>The whole memory map: the KIM-1's own regions and every card range.</summary>
    public IReadOnlyList<MapRegion> MemoryMap()
    {
        var regions = new List<MapRegion>
        {
            new(new(0x0000, 0x03FF), "KIM-1 RAM", true),
            new(new(0x1700, 0x17FF), "KIM-1 6530 I/O and RAM", true),
            new(new(0x1800, 0x1FFF), "KIM-1 monitor ROM", true),
        };
        if (Kim4) regions.Add(new(new(0xFFF8, 0xFFFF), "KIM-1 (vectors)", true));

        foreach (var (slot, card) in InstalledCards)
        {
            string where = Kim4 ? $"slot {slot + 1}" : "expansion connector";
            foreach (var range in RangesOf(card))
                regions.Add(new(range, $"{CardConfig.DisplayName(card.Type).Split("  ")[0]} ({where})", false));
        }

        return [.. regions.OrderBy(r => r.Range.Start)];
    }

    /// <summary>Configuration mistakes the MOS manuals warn about, in plain words. Empty when all is well.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        var cards = InstalledCards.ToList();

        if (!Kim4 && cards.Any(c => c.Card.Type == CardType.Kim5))
            problems.Add("The KIM-5 plugs into a KIM-4 motherboard; turn on the KIM-4 first.");

        foreach (var (slot, card) in cards.Where(c => c.Card.Type == CardType.K1008 && VisibleMemoryCard.Address(c.Card.Switches) is null))
        {
            string where = Kim4 ? $"slot {slot + 1}" : "the expansion connector";
            problems.Add($"The K-1008 in {where} has no address: switches 1–2, 3–4 and 5–6 are pairs, and exactly one switch of each pair must be on.");
        }

        var cardRanges = cards.SelectMany(c => RangesOf(c.Card).Select(r => (c.Slot, c.Card, Range: r))).ToList();
        foreach (var (slot, card, range) in cardRanges)
        {
            string name = CardConfig.DisplayName(card.Type).Split("  ")[0];
            if (range.Overlaps(new AddressRange(0x0000, 0x1FFF)) && !(Kim4 && range.Start is >= 0x0400 && range.End <= 0x13FF))
                problems.Add($"{name} at {range} overlaps the KIM-1's own memory ($0000–$1FFF); the MOS manual says to place expansion memory at $2000 or above.");
            if (!Kim4 && range.Overlaps(new AddressRange(0xFFFA, 0xFFFF)))
                problems.Add($"{name} at {range} covers $FFFA–$FFFF, hiding the KIM-1's reset/NMI/IRQ vectors. Use a KIM-4 (which keeps $FFF8–$FFFF on the KIM-1) or another address.");
        }

        for (int i = 0; i < cardRanges.Count; i++)
        {
            for (int j = i + 1; j < cardRanges.Count; j++)
            {
                if (cardRanges[i].Range.Overlaps(cardRanges[j].Range) && cardRanges[i].Slot != cardRanges[j].Slot)
                    problems.Add($"Cards in slots {cardRanges[i].Slot + 1} and {cardRanges[j].Slot + 1} both answer at {cardRanges[i].Range} / {cardRanges[j].Range}.");
            }
        }

        return [.. problems.Distinct()];
    }
}
