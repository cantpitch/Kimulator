using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Kimulator.App;

/// <summary>Open/save pickers that remember the last folder used.</summary>
public static class FileDialogs
{
    public static readonly FilePickerFileType PaperTape = new("MOS paper tape") { Patterns = ["*.ptp", "*.pap"] };
    public static readonly FilePickerFileType IntelHex = new("Intel HEX") { Patterns = ["*.hex", "*.ihx"] };
    public static readonly FilePickerFileType Binary = new("Raw binary") { Patterns = ["*.bin"] };
    public static readonly FilePickerFileType SaveState = new("Kimulator save state") { Patterns = ["*.kimstate"] };
    public static readonly FilePickerFileType Text = new("Text") { Patterns = ["*.txt"] };
    public static readonly FilePickerFileType All = new("All files") { Patterns = ["*"] };
    public static readonly FilePickerFileType Assembly = new("6502 assembly") { Patterns = ["*.asm", "*.s", "*.a65", "*.inc"] };
    public static readonly FilePickerFileType Wav = new("WAV audio") { Patterns = ["*.wav"] };
    public static readonly FilePickerFileType Rom = new("ROM image") { Patterns = ["*.bin", "*.rom"] };
    public static readonly FilePickerFileType Png = new("PNG image") { Patterns = ["*.png"] };

    public static readonly FilePickerFileType Programs = new("Programs (.ptp, .hex, .bin)")
    {
        Patterns = ["*.ptp", "*.pap", "*.hex", "*.ihx", "*.bin"],
    };

    public static IReadOnlyList<FilePickerFileType> ProgramTypes { get; } = [Programs, PaperTape, IntelHex, Binary, All];
    public static IReadOnlyList<FilePickerFileType> PaperTapeTypes { get; } = [PaperTape, All];
    public static IReadOnlyList<FilePickerFileType> TextTypes { get; } = [Text, All];
    public static IReadOnlyList<FilePickerFileType> SaveStateTypes { get; } = [SaveState, All];
    public static IReadOnlyList<FilePickerFileType> AssemblyTypes { get; } = [Assembly, All];
    public static IReadOnlyList<FilePickerFileType> WavTypes { get; } = [Wav, All];
    public static IReadOnlyList<FilePickerFileType> RomTypes { get; } = [Rom, All];
    public static IReadOnlyList<FilePickerFileType> PngTypes { get; } = [Png];

    /// <summary>Like <see cref="OpenAsync"/> but also returns the local path (needed to save back to the same file).</summary>
    public static async Task<(string Path, byte[] Content)?> OpenPathAsync(
        Window owner, AppSettings settings, string title, IReadOnlyList<FilePickerFileType> types)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types,
            SuggestedStartLocation = await StartFolder(owner, settings),
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return null;
        Remember(settings, files[0]);
        return (path, await File.ReadAllBytesAsync(path));
    }

    /// <summary>Asks for a destination, writes <paramref name="content"/> and returns the local path.</summary>
    public static async Task<string?> SavePathAsync(
        Window owner, AppSettings settings, string title, string suggestedName,
        IReadOnlyList<FilePickerFileType> types, byte[] content)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = types,
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await StartFolder(owner, settings),
        });
        if (file?.TryGetLocalPath() is not { } path) return null;
        Remember(settings, file);
        await File.WriteAllBytesAsync(path, content);
        return path;
    }

    public static async Task<(string Name, byte[] Content)?> OpenAsync(
        Window owner, AppSettings settings, string title, IReadOnlyList<FilePickerFileType> types)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types,
            SuggestedStartLocation = await StartFolder(owner, settings),
        });
        if (files.Count == 0) return null;

        var file = files[0];
        Remember(settings, file);
        await using var stream = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return (file.Name, buffer.ToArray());
    }

    /// <summary>Asks for a destination and writes the content produced by <paramref name="content"/> for the chosen file name.</summary>
    public static async Task<string?> SaveAsync(
        Window owner, AppSettings settings, string title, string suggestedName,
        IReadOnlyList<FilePickerFileType> types, Func<string, byte[]> content)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = types,
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await StartFolder(owner, settings),
        });
        if (file is null) return null;

        Remember(settings, file);
        byte[] bytes = content(file.Name);
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await stream.WriteAsync(bytes);
        return file.Name;
    }

    public static Task<string?> SaveAsync(
        Window owner, AppSettings settings, string title, string suggestedName,
        IReadOnlyList<FilePickerFileType> types, byte[] content) =>
        SaveAsync(owner, settings, title, suggestedName, types, _ => content);

    private static async Task<IStorageFolder?> StartFolder(Window owner, AppSettings settings) =>
        settings.LastDirectory is { } dir && Directory.Exists(dir)
            ? await owner.StorageProvider.TryGetFolderFromPathAsync(dir)
            : null;

    private static void Remember(AppSettings settings, IStorageItem file)
    {
        if (file.TryGetLocalPath() is { } path && Path.GetDirectoryName(path) is { } dir)
            settings.LastDirectory = dir;
    }
}
