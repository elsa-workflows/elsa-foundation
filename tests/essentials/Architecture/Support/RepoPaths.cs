namespace Elsa.Architecture.Tests;

/// <summary>
/// The repository root every guard sweeps from, and the one test for build output a sweep skips.
/// </summary>
internal static class RepoPaths
{
    /// <summary>The nearest ancestor of the test output directory that holds <c>Elsa.Server.slnx</c>.</summary>
    internal static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>
    /// Whether <paramref name="path"/> lies under a <c>bin/</c> or <c>obj/</c> directory. Accepts absolute paths
    /// and repository-relative paths already normalized to <c>/</c>.
    /// </summary>
    /// <remarks>
    /// Build output (AssemblyInfo, <c>GlobalUsings.g.cs</c>, EF and source-generator scaffolds) is not source;
    /// scanning it would make a sweep depend on build state.
    /// </remarks>
    internal static bool IsBuildOutput(string path)
    {
        var normalized = path.Replace(Path.DirectorySeparatorChar, '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal) || normalized.Contains("/obj/", StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
