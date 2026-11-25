using System.Text.Json;

namespace Elsa.Architecture.Tests;

/// <summary>Portable labels for exact evaluated Compile items supplied by restored packages. Never source authentication.</summary>
internal static class JsonHashExternalSources
{
    internal static IReadOnlyDictionary<string, string> Read(string assetsFile, IReadOnlySet<string> compileSources)
    {
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsFile));
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var result = new Dictionary<string, string>(comparer);
        var folders = assets.RootElement.GetProperty("packageFolders").EnumerateObject().Select(p => Path.GetFullPath(p.Name)).ToArray();
        foreach (var library in assets.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package") continue;
            var packagePath = library.Value.GetProperty("path").GetString()!;
            foreach (var folder in folders)
            {
                var package = Path.GetFullPath(Path.Join(folder, packagePath));
                if (!Within(folder, package)) throw new InvalidOperationException("Package source path escaped its restored folder.");
                foreach (var entry in library.Value.GetProperty("files").EnumerateArray())
                {
                    var relative = entry.GetString()!;
                    var full = Path.GetFullPath(Path.Join(package, relative));
                    if (!Within(package, full)) throw new InvalidOperationException("Package source file escaped its restored package.");
                    if (!compileSources.Contains(full)) continue;
                    var identity = "nuget:" + library.Name.ToLowerInvariant() + "/" + relative.Replace('\\', '/');
                    if (result.TryGetValue(full, out var prior) && prior != identity)
                        throw new InvalidOperationException("Ambiguous package source identity: " + full);
                    result[full] = identity;
                }
            }
        }
        return result;
    }

    private static bool Within(string root, string path)
    {
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
