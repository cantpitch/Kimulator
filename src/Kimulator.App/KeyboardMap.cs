using Avalonia.Input;
using Kimulator.Kim1;

namespace Kimulator.App;

/// <summary>PC keyboard → KIM-1 keypad.</summary>
public static class KeyboardMap
{
    public const string HelpText =
        """
        0-9, A-F       hex keys (numpad works too)
        L  or F1       AD  (address)
        M  or F2       DA  (data)
        P  or F3       PC
        +  Enter Space +   (next address)
        G  or F5       GO
        Esc            ST  (stop / NMI)
        F12            RS  (reset)
        F9             SST switch on/off

        Ctrl+1 / Ctrl+2  full board / compact view
        Ctrl+T           terminal (TTY) window
        Ctrl+O           load program (.ptp, .hex, .bin)
        Ctrl+M           save memory range
        Ctrl+S / Ctrl+L  save / load state
        F6 / F7          quick save / quick load
        """;

    public static Kim1Key? ToKimKey(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => (Kim1Key)(key - Key.D0),
        >= Key.NumPad0 and <= Key.NumPad9 => (Kim1Key)(key - Key.NumPad0),
        >= Key.A and <= Key.F => (Kim1Key)(0x0A + (key - Key.A)),
        Key.L or Key.F1 => Kim1Key.Address,
        Key.M or Key.F2 => Kim1Key.Data,
        Key.P or Key.F3 => Kim1Key.Pc,
        Key.OemPlus or Key.Add or Key.Enter or Key.Space => Kim1Key.Plus,
        Key.G or Key.F5 => Kim1Key.Go,
        Key.Escape => Kim1Key.Stop,
        Key.F12 => Kim1Key.Reset,
        _ => null,
    };
}
