namespace Elsa.Cli;

/// <summary>Publishes one accepted authored document as a fresh local file.</summary>
public static class CompositionFilePublisher
{
    public static void PublishAuthored(
        string destinationPath,
        string sourceDirectory,
        ReadOnlyMemory<byte> authoredJson,
        Action recheck,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinationPath);
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentNullException.ThrowIfNull(recheck);

        string? stagingPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(sourceDirectory))
                throw CliRefusal.Resolution("bridge-source-missing", "The selected host source directory is missing.");

            var sourceRoot = ResolveExistingDirectory(sourceDirectory);
            var destinationFullPath = Path.GetFullPath(destinationPath);
            var destinationName = Path.GetFileName(destinationFullPath);
            var destinationParent = Path.GetDirectoryName(destinationFullPath);
            if (string.IsNullOrEmpty(destinationName) || string.IsNullOrEmpty(destinationParent) || !Directory.Exists(destinationParent))
                throw OutputFailed();

            var resolvedParent = ResolveExistingDirectory(destinationParent);
            var resolvedDestination = Path.Combine(resolvedParent, destinationName);
            if (IsSameOrInside(sourceRoot, resolvedDestination))
                throw OutputExists();
            if (PathExists(resolvedDestination))
                throw OutputExists();

            stagingPath = Path.Combine(resolvedParent, $".{destinationName}.{Guid.NewGuid():N}.tmp");
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            using (var stream = new FileStream(stagingPath, options))
            {
                stream.Write(authoredJson.Span);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            recheck();
            cancellationToken.ThrowIfCancellationRequested();

            File.Move(stagingPath, resolvedDestination, overwrite: false);
            stagingPath = null;
        }
        catch (CliRefusal)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw CliRefusal.Usage("bridge-review-required", "The authored file was not published because review was cancelled.");
        }
        catch (Exception)
        {
            if (PathExistsSafely(destinationPath))
                throw OutputExists();
            throw OutputFailed();
        }
        finally
        {
            if (stagingPath is not null)
            {
                try
                {
                    File.Delete(stagingPath);
                }
                catch (IOException)
                {
                    // Do not replace the original safe refusal or expose a filesystem path in diagnostics.
                }
                catch (UnauthorizedAccessException)
                {
                    // Best-effort cleanup; the primary result remains the safe refusal below.
                }
            }
        }
    }

    private static string ResolveExistingDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw OutputFailed();

        var root = Path.GetPathRoot(fullPath) ?? throw OutputFailed();
        var current = root;
        var remainder = fullPath[root.Length..];
        var components = remainder.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        foreach (var component in components)
        {
            var info = new DirectoryInfo(Path.Combine(current, component));
            if (!info.Exists)
                throw OutputFailed();
            var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
            current = resolved?.FullName ?? info.FullName;
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }

    private static bool IsSameOrInside(string parent, string path)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedParent = Path.TrimEndingDirectorySeparator(parent);
        if (string.Equals(normalizedParent, path, comparison))
            return true;
        var prefix = Path.EndsInDirectorySeparator(normalizedParent)
            ? normalizedParent
            : normalizedParent + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison) ||
               (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar &&
                path.StartsWith(normalizedParent + Path.AltDirectorySeparatorChar, comparison));
    }

    private static bool PathExists(string path) =>
        File.Exists(path) || Directory.Exists(path) ||
        new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null;

    private static bool PathExistsSafely(string path)
    {
        try
        {
            return PathExists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static CliRefusal OutputExists() =>
        CliRefusal.Resolution("bridge-output-exists", "The authored output already exists or overlaps its source.");

    private static CliRefusal OutputFailed() =>
        CliRefusal.Resolution("bridge-output-failed", "The authored output could not be published.");
}
