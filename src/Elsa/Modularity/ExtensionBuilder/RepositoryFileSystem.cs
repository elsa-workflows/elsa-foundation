using Elsa.Git;

namespace Elsa.Modularity.ExtensionBuilder;

/// <summary>
/// Repository-path-scoped file-system surface for the Extension Builder: builds the repository tree,
/// enumerates and reads working-tree files (skipping the private <c>.git</c> directory), resolves and
/// validates safe relative paths, selects solutions, and parses <c>git status</c> output into the
/// source-control view. It is stateless over a repository path — it holds no <c>RootPath</c> and no
/// persisted state — and reads Git state through <see cref="RepositoryInspector"/> / <see cref="GitClient"/>.
/// </summary>
internal sealed class RepositoryFileSystem(IGitClient git, RepositoryInspector inspector)
{
    public RepositoryTree BuildTree(string workspaceId, string repositoryPath, string? selectedSolutionPath)
    {
        var repositoryState = inspector.GetRepositoryState(repositoryPath);
        var dirtyPaths = inspector.GetDirtyRepositoryPaths(repositoryPath);
        var solutionPaths = EnumerateFiles(repositoryPath, "*.sln*")
            .Select(path => Path.GetRelativePath(repositoryPath, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selectedSolution = SelectSolution(solutionPaths, selectedSolutionPath);
        var solutions = solutionPaths
            .Select(path => new RepositorySolutionSummary(path, Path.GetFileNameWithoutExtension(path), string.Equals(path, selectedSolution, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var entries = EnumerateEntries(repositoryPath, dirtyPaths);
        return new(workspaceId, repositoryState.ActiveBranch, repositoryState.IsDirty, solutions, entries);
    }

    public async Task<RepositoryFile> ReadFileAsync(string repositoryPath, string filePath, string relativePath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var info = new FileInfo(filePath);
        return new(relativePath, content, GetFileKind(relativePath), info.Length, IsPathDirty(relativePath, inspector.GetDirtyRepositoryPaths(repositoryPath)), info.LastWriteTimeUtc);
    }

    public RepositoryFileSummary BuildFileSummary(string repositoryPath, string physicalPath, string relativePath, ISet<string> dirtyPaths)
    {
        var info = new FileInfo(physicalPath);
        return new(relativePath, GetFileKind(relativePath), info.Length, IsPathDirty(relativePath, dirtyPaths), info.LastWriteTimeUtc);
    }

    public SourceControlStatus GetSourceControlStatus(string workspaceId, string repositoryPath)
    {
        var repositoryState = inspector.GetRepositoryState(repositoryPath);
        var changedFiles = git.RunOrDefault(repositoryPath, "status", "--porcelain", "--untracked-files=all")
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseSourceControlStatus)
            .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new(
            workspaceId,
            repositoryState.ActiveBranch,
            changedFiles.Length > 0,
            changedFiles,
            changedFiles.Where(x => x.IsStaged).ToArray(),
            changedFiles.Where(x => x.IsUnstaged).ToArray());
    }

    public string? ResolveBuildTarget(string repositoryPath, string? targetPath)
    {
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            var normalized = NormalizeRelativePath(targetPath);
            var filePath = ResolveFilePath(repositoryPath, normalized);
            if (!File.Exists(filePath))
                throw new ArgumentException($"Build target '{normalized}' was not found.", nameof(targetPath));
            return filePath;
        }

        return EnumerateFiles(repositoryPath, "*.sln*")
            .Where(path => Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Concat(EnumerateFiles(repositoryPath, "*.*proj").Order(StringComparer.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    public string ResolveFilePath(string repositoryPath, string relativePath)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        var path = Path.GetFullPath(Path.Combine(repositoryPath, normalizedPath));
        if (!path.StartsWith(repositoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The resolved repository file path is outside the repository root.");
        return path;
    }

    public static string NormalizeRelativePath(string path)
    {
        var normalized = (path ?? "").Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".." || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A safe relative repository file path is required.", nameof(path));
        return normalized;
    }

    public void DeleteEmptyParentDirectories(string repositoryPath, string? startPath)
    {
        var root = Path.GetFullPath(repositoryPath);
        var current = string.IsNullOrWhiteSpace(startPath) ? null : Path.GetFullPath(startPath);
        while (!string.IsNullOrWhiteSpace(current) &&
               !string.Equals(current, root, StringComparison.OrdinalIgnoreCase) &&
               current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(current) &&
               !Directory.EnumerateFileSystemEntries(current).Any())
        {
            Directory.Delete(current);
            current = Path.GetDirectoryName(current);
        }
    }

    public ISet<string> GetDirtyRepositoryPaths(string repositoryPath) => inspector.GetDirtyRepositoryPaths(repositoryPath);

    private RepositoryFileSummary[] EnumerateEntries(string repositoryPath, ISet<string> dirtyPaths)
    {
        if (!Directory.Exists(repositoryPath))
            return [];

        var directories = EnumerateDirectories(repositoryPath)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(repositoryPath, path).Replace(Path.DirectorySeparatorChar, '/');
                return new RepositoryFileSummary(relative, RepositoryFileKind.Folder, 0, false, Directory.GetLastWriteTimeUtc(path));
            });
        var files = EnumerateFiles(repositoryPath, "*")
            .Select(path =>
            {
                var info = new FileInfo(path);
                var relative = Path.GetRelativePath(repositoryPath, path).Replace(Path.DirectorySeparatorChar, '/');
                return new RepositoryFileSummary(relative, GetFileKind(relative), info.Length, IsPathDirty(relative, dirtyPaths), info.LastWriteTimeUtc);
            });

        return directories.Concat(files)
            .OrderBy(x => x.Kind is RepositoryFileKind.Folder ? 0 : 1)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SourceControlFileStatus ParseSourceControlStatus(string line)
    {
        var indexStatus = line.Length > 0 ? line[0] : ' ';
        var workTreeStatus = line.Length > 1 ? line[1] : ' ';
        var path = line.Length > 3 ? line[3..].Trim().Trim('"').Replace('\\', '/') : "";
        var renameSeparator = path.IndexOf(" -> ", StringComparison.Ordinal);
        if (renameSeparator >= 0)
            path = path[(renameSeparator + 4)..];

        return new(
            path,
            $"{indexStatus}{workTreeStatus}".Trim(),
            indexStatus is not ' ' and not '?',
            workTreeStatus is not ' ' || indexStatus is '?');
    }

    private static bool IsPathDirty(string path, ISet<string> dirtyPaths) =>
        dirtyPaths.Contains(path) || dirtyPaths.Any(dirtyPath => dirtyPath.StartsWith(path.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));

    private static string? SelectSolution(IReadOnlyList<string> solutionPaths, string? selectedSolutionPath)
    {
        if (solutionPaths.Count == 0)
            return null;
        if (!string.IsNullOrWhiteSpace(selectedSolutionPath))
        {
            var normalized = NormalizeRelativePath(selectedSolutionPath);
            if (solutionPaths.Any(path => string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase)))
                return normalized;
        }

        return solutionPaths.Count == 1 ? solutionPaths[0] : null;
    }

    private static RepositoryFileKind GetFileKind(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            return RepositoryFileKind.Solution;
        if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase))
            return RepositoryFileKind.Project;
        return RepositoryFileKind.File;
    }

    private static bool IsPrivatePath(string repositoryPath, string path)
    {
        var relative = Path.GetRelativePath(repositoryPath, path).Replace(Path.DirectorySeparatorChar, '/');
        return relative.Split('/').Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateDirectories(string repositoryPath)
    {
        foreach (var directory in EnumerateDirectoriesCore(repositoryPath))
            yield return directory;
    }

    private static IEnumerable<string> EnumerateFiles(string repositoryPath, string searchPattern)
    {
        foreach (var file in Directory.EnumerateFiles(repositoryPath, searchPattern, SearchOption.TopDirectoryOnly).Where(path => !IsPrivatePath(repositoryPath, path)))
            yield return file;

        foreach (var directory in EnumerateDirectoriesCore(repositoryPath))
        {
            foreach (var file in Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly))
                yield return file;
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesCore(string repositoryPath)
    {
        var pending = new Stack<string>(Directory.EnumerateDirectories(repositoryPath, "*", SearchOption.TopDirectoryOnly).Reverse());
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (IsPrivatePath(repositoryPath, directory))
                continue;

            yield return directory;
            foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly).Reverse())
                pending.Push(child);
        }
    }
}
