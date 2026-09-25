namespace Kimulator.Tests;

internal static class TestPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string DataFile(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);

    public static string RomFile(string name) => Path.Combine(AppContext.BaseDirectory, "Roms", name);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kimulator.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
