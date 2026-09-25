namespace Kimulator.Kim1;

/// <summary>
/// Keys on the KIM-1 keypad. Values 0x00-0x14 are the codes the monitor's GETKEY returns;
/// <see cref="Stop"/> and <see cref="Reset"/> are wired to NMI and RES instead of the matrix.
/// </summary>
public enum Kim1Key
{
    D0 = 0x00, D1, D2, D3, D4, D5, D6, D7, D8, D9, DA, DB, DC, DD, DE, DF,
    Address = 0x10, // AD
    Data = 0x11,    // DA
    Plus = 0x12,    // +
    Go = 0x13,      // GO
    Pc = 0x14,      // PC
    Stop = 0x20,    // ST -> NMI
    Reset = 0x21,   // RS -> RES
}

public static class Kim1KeyExtensions
{
    /// <summary>True for keys read through the 3×7 keypad matrix.</summary>
    public static bool IsMatrixKey(this Kim1Key key) => key <= Kim1Key.Pc;

    /// <summary>Keypad row (74145 output 0-2) for a matrix key.</summary>
    public static int Row(this Kim1Key key) => (int)key / 7;

    /// <summary>Port A bit (column) for a matrix key: code = row*7 + (6 - bit).</summary>
    public static int Column(this Kim1Key key) => 6 - (int)key % 7;
}
