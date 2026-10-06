namespace Elsa.Cli;

/// <summary>Publishes reviewed composition output without replacing source or existing output.</summary>
public static class CompositionFilePublisher
{
    /// <summary>Publishes a complete reviewed candidate file set as a fresh local directory.</summary>
    public static void PublishCandidate(
        string destinationDirectory,
        string sourceDirectory,
        IReadOnlyDictionary<string, byte[]> candidateFiles,
        Action recheck,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinationDirectory);
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentNullException.ThrowIfNull(candidateFiles);
        ArgumentNullException.ThrowIfNull(recheck);

        PublishDirectory(destinationDirectory, [sourceDirectory], () => ValidateCandidateFiles(candidateFiles, portable: false)
            .Select(file => new OutputFile(file.Key, file.Value)).ToArray(), recheck, cancellationToken,
            () =>
            {
                if (!Directory.Exists(sourceDirectory))
                    throw CliRefusal.Resolution("bridge-source-missing", "The selected host source directory is missing.");
            });
    }

    /// <summary>Publishes public composition bytes and an opaque private receipt as one fresh artifact directory.</summary>
    public static void PublishPortableComposition(
        string destinationDirectory,
        ReadOnlyMemory<byte> compositionJson,
        ReadOnlyMemory<byte> privateReceiptJson,
        IEnumerable<string> protectedRoots,
        Action recheck,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinationDirectory);
        ArgumentNullException.ThrowIfNull(protectedRoots);
        ArgumentNullException.ThrowIfNull(recheck);

        PublishDirectory(destinationDirectory, protectedRoots,
            () =>
            [
                new OutputFile("public/composition.json", compositionJson),
                new OutputFile("private/input-receipt.json", privateReceiptJson)
            ], recheck, cancellationToken);
    }

    /// <summary>Publishes supported host files and an opaque private candidate receipt as one fresh directory.</summary>
    public static void PublishPortableCandidate(
        string destinationDirectory,
        IReadOnlyDictionary<string, byte[]> candidateFiles,
        ReadOnlyMemory<byte> privateReceiptJson,
        IEnumerable<string> protectedRoots,
        Action recheck,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinationDirectory);
        ArgumentNullException.ThrowIfNull(candidateFiles);
        ArgumentNullException.ThrowIfNull(protectedRoots);
        ArgumentNullException.ThrowIfNull(recheck);

        PublishDirectory(destinationDirectory, protectedRoots, () =>
        {
            var files = ValidateCandidateFiles(candidateFiles, portable: true);
            if (files.Count == 0)
                throw OutputFailed();

            var outputs = files.Select(file => new OutputFile($"candidate/{file.Key}", file.Value)).ToList();
            outputs.Add(new OutputFile("private/candidate-receipt.json", privateReceiptJson));
            return outputs;
        }, recheck, cancellationToken);
    }

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

        PublishAuthoredCore(destinationPath, sourceDirectory, authoredJson, recheck, cancellationToken);
    }

    /// <summary>Publishes a reviewed authored composition beside its inputs without replacing any file.</summary>
    public static void PublishReviewedAuthored(
        string destinationPath,
        ReadOnlyMemory<byte> authoredJson,
        Action recheck,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinationPath);
        ArgumentNullException.ThrowIfNull(recheck);

        PublishAuthoredCore(destinationPath, null, authoredJson, recheck, cancellationToken);
    }

    private static void PublishAuthoredCore(
        string destinationPath,
        string? sourceDirectory,
        ReadOnlyMemory<byte> authoredJson,
        Action recheck,
        CancellationToken cancellationToken)
    {
        string? stagingPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? sourceRoot = null;
            if (sourceDirectory is not null)
            {
                if (!Directory.Exists(sourceDirectory))
                    throw CliRefusal.Resolution("bridge-source-missing", "The selected host source directory is missing.");
                sourceRoot = ResolveExistingDirectory(sourceDirectory);
            }
            var destinationFullPath = Path.GetFullPath(destinationPath);
            var destinationName = Path.GetFileName(destinationFullPath);
            var destinationParent = Path.GetDirectoryName(destinationFullPath);
            if (string.IsNullOrEmpty(destinationName) || string.IsNullOrEmpty(destinationParent) || !Directory.Exists(destinationParent))
                throw OutputFailed();

            var resolvedParent = ResolveExistingDirectory(destinationParent);
            var resolvedDestination = Path.Combine(resolvedParent, destinationName);
            if (sourceRoot is not null && IsSameOrInside(sourceRoot, resolvedDestination))
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

    private static void PublishDirectory(
        string destinationDirectory,
        IEnumerable<string> protectedRoots,
        Func<IReadOnlyList<OutputFile>> filesFactory,
        Action recheck,
        CancellationToken cancellationToken,
        Action? preflight = null)
    {
        string? stagingDirectory = null;
        string? resolvedDestination = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            preflight?.Invoke();
            var destinationFullPath = Path.GetFullPath(destinationDirectory);
            var destinationName = Path.GetFileName(destinationFullPath);
            var destinationParent = Path.GetDirectoryName(destinationFullPath);
            if (string.IsNullOrEmpty(destinationName) || string.IsNullOrEmpty(destinationParent) || !Directory.Exists(destinationParent))
                throw OutputFailed();

            var resolvedParent = ResolveExistingDirectory(destinationParent);
            resolvedDestination = Path.Combine(resolvedParent, destinationName);
            var roots = protectedRoots.ToArray();
            if (roots.Length == 0 || roots.Any(string.IsNullOrWhiteSpace))
                throw OutputFailed();

            foreach (var protectedRoot in roots)
            {
                var resolvedProtectedRoot = ResolveProtectedPath(protectedRoot);
                if (IsSameOrInside(resolvedProtectedRoot, resolvedDestination) ||
                    IsSameOrInside(resolvedDestination, resolvedProtectedRoot))
                    throw OutputExists();
            }

            if (PathExists(resolvedDestination))
                throw OutputExists();

            var files = filesFactory();
            stagingDirectory = Path.Combine(resolvedParent, $".{destinationName}.{Guid.NewGuid():N}.tmp");
            CreatePrivateDirectory(stagingDirectory);
            foreach (var (relativePath, contents) in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = CreatePrivateParentDirectories(stagingDirectory, relativePath);
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.WriteThrough
                };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

                using var stream = new FileStream(filePath, options);
                stream.Write(contents.Span);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            recheck();
            cancellationToken.ThrowIfCancellationRequested();
            if (PathExists(resolvedDestination))
                throw OutputExists();

            Directory.Move(stagingDirectory, resolvedDestination);
            stagingDirectory = null;
        }
        catch (CliRefusal)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw CliRefusal.Usage("bridge-review-required", "The candidate was not published because review was cancelled.");
        }
        catch (Exception)
        {
            if ((resolvedDestination is not null && PathExistsSafely(resolvedDestination)) || PathExistsSafely(destinationDirectory))
                throw OutputExists();
            throw OutputFailed();
        }
        finally
        {
            if (stagingDirectory is not null)
            {
                try
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
                catch (IOException)
                {
                    // Preserve the primary safe refusal and remove only task-owned staging.
                }
                catch (UnauthorizedAccessException)
                {
                    // Preserve the primary safe refusal and remove only task-owned staging.
                }
            }
        }
    }

    private static string CreatePrivateParentDirectories(string stagingDirectory, string relativePath)
    {
        var segments = relativePath.Split('/');
        if (segments.Length == 0 || segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
                                                            segment.IndexOfAny(['\\', ':', '\0']) >= 0))
            throw OutputFailed();

        var current = stagingDirectory;
        foreach (var segment in segments[..^1])
        {
            current = Path.Combine(current, segment);
            CreatePrivateDirectory(current);
        }

        return Path.Combine(current, segments[^1]);
    }

    private static string ResolveProtectedPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
            return ResolveExistingDirectory(fullPath);

        var fileInfo = new FileInfo(fullPath);
        fileInfo.Refresh();
        if (fileInfo.LinkTarget is not null || fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw OutputFailed();

        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parent))
            throw OutputFailed();

        var resolvedParent = Directory.Exists(parent) ? ResolveExistingDirectory(parent) : Path.GetFullPath(parent);
        return Path.Combine(resolvedParent, Path.GetFileName(fullPath));
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

    private static IReadOnlyList<KeyValuePair<string, byte[]>> ValidateCandidateFiles(
        IReadOnlyDictionary<string, byte[]> candidateFiles, bool portable)
    {
        var files = new List<KeyValuePair<string, byte[]>>(candidateFiles.Count);
        var names = new HashSet<string>(portable || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        foreach (var (fileName, contents) in candidateFiles)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".." ||
                Path.IsPathRooted(fileName) || fileName.IndexOfAny(['/', '\\', ':', '\0']) >= 0 ||
                contents is null || !names.Add(fileName) ||
                (portable && !CompositionFileSource.IsSupportedFileName(fileName)))
                throw OutputFailed();

            files.Add(new KeyValuePair<string, byte[]>(fileName, contents));
        }

        return files;
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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
        CliRefusal.Resolution("bridge-output-exists", "The output already exists or overlaps its source.");

    private static CliRefusal OutputFailed() =>
        CliRefusal.Resolution("bridge-output-failed", "The output could not be published.");

    private readonly record struct OutputFile(string RelativePath, ReadOnlyMemory<byte> Contents);
}
