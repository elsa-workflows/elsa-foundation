namespace Elsa.Architecture.Tests;

/// <summary>
/// The repository root every guard sweeps from, and the one test for build output a sweep skips.
/// </summary>
internal static class RepoPaths
{
    /// <summary>The nearest ancestor of the test output directory that holds <c>Elsa.Server.slnx</c>.</summary>
    internal static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>Existing files in the repository's Git index, excluding untracked checkouts and build leftovers.</summary>
    internal static IReadOnlyList<string> TrackedFiles()
    {
        var (exitCode, output) = ChildProcess.Run("git", ["ls-files", "--cached", "-z"], RepoRoot);
        if (exitCode != 0)
            throw new InvalidOperationException($"git ls-files failed with exit {exitCode}:\n{output}");

        var files = output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => File.Exists(Path.Join(RepoRoot, path)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
            throw new InvalidOperationException("The architecture guard found no tracked files in this checkout.");
        return files;
    }

    /// <summary><see cref="IsBuildOutput(string, string)"/> judged against <see cref="RepoRoot"/>.</summary>
    internal static bool IsBuildOutput(string path) => IsBuildOutput(RepoRoot, path);

    /// <summary>
    /// Whether <paramref name="path"/> lies under a <c>bin/</c> or <c>obj/</c> directory, judged by <paramref name="path"/>'s
    /// location relative to <paramref name="repoRoot"/> rather than the directories above the repository.
    /// </summary>
    /// <remarks>
    /// Build output (AssemblyInfo, <c>GlobalUsings.g.cs</c>, EF and source-generator scaffolds) is not source;
    /// scanning it would make a sweep depend on build state. The check is relative to <paramref name="repoRoot"/>,
    /// not the absolute path, because a checkout that itself sits under a directory named <c>bin</c> or <c>obj</c>
    /// (for example <c>~/bin/elsa-foundation</c>) would otherwise make every file look like build output; the sweep
    /// would then scan nothing and the guard would pass having checked nothing.
    /// </remarks>
    internal static bool IsBuildOutput(string repoRoot, string path) =>
        HasSegment(repoRoot, path, "bin") || HasSegment(repoRoot, path, "obj");

    /// <summary>
    /// Whether a directory named <paramref name="segment"/> appears in <paramref name="path"/>'s location relative
    /// to <paramref name="repoRoot"/> (not in the directories above the repository).
    /// </summary>
    internal static bool HasSegment(string repoRoot, string path, string segment)
    {
        var relative = Path.IsPathRooted(path) ? Path.GetRelativePath(repoRoot, path) : path;
        return $"/{relative.Replace(Path.DirectorySeparatorChar, '/')}".Contains($"/{segment}/", StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
