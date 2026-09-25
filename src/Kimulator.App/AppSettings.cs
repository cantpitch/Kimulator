using System.Text.Json;

namespace Kimulator.App;

/// <summary>User preferences, stored as JSON in the per-user application data folder.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kimulator");

    private static string FilePath => Path.Combine(Directory, "settings.json");

    public bool CompactView { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }

    /// <summary>Emulation speed multiplier; 0 = unthrottled.</summary>
    public double Speed { get; set; } = 1;

    public bool PresetInterruptVectors { get; set; } = true;
    public bool TtyMode { get; set; }
    public int BaudRate { get; set; } = 1200;
    public bool AutoCalibrateTty { get; set; } = true;
    public bool TerminalUppercase { get; set; } = true;
    public bool TerminalOpen { get; set; }
    public string? LastDirectory { get; set; }

    /// <summary>File open in the assembler window.</summary>
    public string? AssemblerFile { get; set; }

    /// <summary>Unsaved assembler text, kept so closing the window never loses work.</summary>
    public string? AssemblerScratch { get; set; }

    public bool AssemblerOpen { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable settings fall back to defaults rather than blocking startup.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to save preferences is not worth interrupting the user for.
        }
    }
}
