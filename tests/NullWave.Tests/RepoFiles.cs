namespace NullWave.Tests.Support;

internal static class RepoFiles
{
    private static readonly string[] SourceFolders =
        { "Views", "Services", "ViewModels", "Helpers", "Models", "Themes" };

    private static readonly Lazy<string> RootLazy = new(FindRoot);

    public static string Root => RootLazy.Value;

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NullWave.csproj"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find NullWave.csproj above " + AppContext.BaseDirectory);
    }

    public static IEnumerable<string> Enumerate(string pattern)
    {
        foreach (var file in Directory.EnumerateFiles(Root, pattern, SearchOption.TopDirectoryOnly))
            yield return file;

        foreach (var folder in SourceFolders)
        {
            var path = Path.Combine(Root, folder);
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories))
                yield return file;
        }
    }

    public static IEnumerable<string> SourceFiles() => Enumerate("*.cs");

    public static string Find(string fileName)
    {
        var matches = Enumerate(fileName).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException($"Expected exactly one '{fileName}' under the source folders, found {matches.Count}.");
        return matches[0];
    }
}