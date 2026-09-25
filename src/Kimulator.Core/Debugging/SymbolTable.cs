using System.Globalization;

namespace Kimulator.Core.Debugging;

/// <summary>Address ↔ label mapping used by the disassembler and debugger.</summary>
public sealed class SymbolTable
{
    private readonly Dictionary<ushort, string> _byAddress = [];
    private readonly Dictionary<string, ushort> _byName = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byName.Count;

    public IEnumerable<KeyValuePair<string, ushort>> Symbols => _byName;

    /// <summary>Adds a label. The first label added for an address is the one shown in disassembly.</summary>
    public void Add(string name, ushort address)
    {
        _byName[name] = address;
        _byAddress.TryAdd(address, name);
    }

    public string? NameOf(ushort address) => _byAddress.GetValueOrDefault(address);

    public bool TryGetAddress(string name, out ushort address) => _byName.TryGetValue(name, out address);

    public void Merge(SymbolTable other)
    {
        foreach (var (name, address) in other._byName) Add(name, address);
    }

    /// <summary>Parses "NAME HEXADDR" lines; ';' starts a comment.</summary>
    public static SymbolTable Parse(string text)
    {
        var table = new SymbolTable();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split(';')[0].Trim();
            if (line.Length == 0) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && ushort.TryParse(parts[1].TrimStart('$'), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort address))
                table.Add(parts[0], address);
        }

        return table;
    }
}
