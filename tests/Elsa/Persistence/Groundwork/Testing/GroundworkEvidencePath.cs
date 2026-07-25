namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Resolves existing symbolic links before comparing or writing evidence paths.
/// </summary>
public static class GroundworkEvidencePath
{
    public static bool IsWithin(string path, string root)
    {
        var canonicalPath = Canonicalize(path);
        var canonicalRoot = Canonicalize(root);
        return IsWithinCanonical(canonicalPath, canonicalRoot);
    }

    public static string ResolveWithin(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathFullyQualified(relativePath))
            throw new ArgumentException("A relative path is required.", nameof(relativePath));

        var canonicalRoot = Canonicalize(root);
        var candidate = Canonicalize(Path.Combine(
            canonicalRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithinCanonical(candidate, canonicalRoot))
            throw new InvalidOperationException("Evidence path escaped the publication root.");

        return candidate;
    }

    public static string Canonicalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
                   ?? throw new InvalidOperationException($"Could not determine the root for '{path}'.");
        var current = root;
        var segments = fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            var link = LinkAt(current);
            if (link is null)
                continue;

            var target = link.ResolveLinkTarget(returnFinalTarget: true)
                         ?? throw new InvalidOperationException($"Could not resolve symbolic link '{current}'.");
            current = Canonicalize(target.FullName);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }

    private static FileSystemInfo? LinkAt(string path)
    {
        var directory = new DirectoryInfo(path);
        if (directory.LinkTarget is not null)
            return directory;

        var file = new FileInfo(path);
        return file.LinkTarget is not null ? file : null;
    }

    private static bool IsWithinCanonical(string path, string root)
    {
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(path, root, comparison) || path.StartsWith(rootPrefix, comparison);
    }
}
