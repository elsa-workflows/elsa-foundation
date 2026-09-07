namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>Admits benchmark artifacts only into a canonical tree disjoint from the source checkout.</summary>
public static class ArtifactOutputAdmission
{
    public static string RequireExternal(string outputDirectory, string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        try
        {
            var output = CanonicalPath(outputDirectory);
            var repository = CanonicalPath(repositoryRoot);
            if (IsWithin(output, repository) || IsWithin(repository, output))
                throw new PerformanceContractException(
                    $"Benchmark output must live outside and must not contain the repository worktree: {output}");
            return output;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new PerformanceContractException(
                $"Benchmark output path could not be admitted: {exception.Message}");
        }
    }

    public static string RequireWithin(string path, string outputDirectory, string repositoryRoot)
    {
        var output = RequireExternal(outputDirectory, repositoryRoot);
        try
        {
            var candidate = CanonicalPath(path);
            if (!IsWithin(candidate, output))
                throw new PerformanceContractException(
                    $"Benchmark result path must remain within the admitted output directory: {candidate}");
            return candidate;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new PerformanceContractException(
                $"Benchmark result path could not be admitted: {exception.Message}");
        }
    }

    public static string Canonicalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            return CanonicalPath(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new PerformanceContractException($"Path could not be canonicalized: {exception.Message}");
        }
    }

    private static bool IsWithin(string path, string root)
    {
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return string.Equals(path, root, PathComparison) || path.StartsWith(rootPrefix, PathComparison);
    }

    private static string CanonicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var unresolved = new Stack<string>();
        var existing = fullPath;
        while (!Directory.Exists(existing) && !File.Exists(existing))
        {
            var name = Path.GetFileName(existing);
            if (string.IsNullOrEmpty(name))
                break;
            unresolved.Push(name);
            existing = Path.GetDirectoryName(existing)
                       ?? throw new PerformanceContractException($"Path '{path}' has no resolvable ancestor.");
        }

        var root = Path.GetPathRoot(existing)
                   ?? throw new PerformanceContractException($"Path '{path}' has no root.");
        var resolved = root;
        var relativeExisting = Path.GetRelativePath(root, existing);
        if (relativeExisting != ".")
        {
            foreach (var segment in relativeExisting.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(resolved, segment);
                FileSystemInfo info = Directory.Exists(candidate)
                    ? new DirectoryInfo(candidate)
                    : new FileInfo(candidate);
                resolved = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidate;
            }
        }

        while (unresolved.TryPop(out var segment))
            resolved = Path.Combine(resolved, segment);
        return Path.GetFullPath(resolved);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
