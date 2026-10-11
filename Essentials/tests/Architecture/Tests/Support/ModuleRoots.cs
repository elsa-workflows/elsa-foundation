namespace Elsa.Architecture.Tests;

/// <summary>
/// The repository roots that hold first-party code, and the one way these guards turn a root name into
/// a directory to sweep.
/// </summary>
/// <remarks>
/// Required modules and hosts live under <c>Essentials/</c>. Backend extension modules and their tests live under
/// <c>Extensions/&lt;capability&gt;/{src,tests}</c>; Studio projects under the sibling <c>studio/</c> roots are
/// deliberately outside this backend guard inventory. A guard pinned to a single root
/// keeps compiling and keeps passing once a module moves out of it; it simply stops looking at that module.
/// Resolving roots here means widening a sweep is one edit rather than one per call site.
/// </remarks>
internal static class ModuleRoots
{
    /// <summary>Every root holding project files, production and test alike.</summary>
    internal static readonly string[] All =
    [
        "Essentials/src", "Essentials/apps", "Essentials/tests",
        "Extensions/*/src", "Extensions/*/tests"
    ];

    /// <summary>The roots holding shipping backend module and host code; extension tests are separate sibling roots.</summary>
    internal static readonly string[] Production = ["Essentials/src", "Essentials/apps", "Extensions/*/src"];

    /// <summary>
    /// Resolves relative roots, expanding a whole-segment <c>*</c> to one directory level, under <paramref name="repoRoot"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Path.Join(string, string)"/> rather than <c>Path.Combine</c>, and a rejection rather than
    /// a silent fallback. <c>Combine</c> discards everything before an absolute segment, so an absolute root
    /// would redirect a guard's sweep outside the repository and the guard would keep reporting green about
    /// whatever it found there. <c>Join</c> has no such behavior, so the hazard is removed rather than merely
    /// guarded; the explicit rejection stays because an absolute root is a programming error worth failing on
    /// loudly rather than resolving to a nonsense path.
    /// </remarks>
    internal static IEnumerable<string> Resolve(string repoRoot, params string[] roots) =>
        roots.SelectMany(root => ExpandRoot(repoRoot, root));

    private static IEnumerable<string> ExpandRoot(string repoRoot, string root)
    {
        _ = UnderRepoRoot(repoRoot, root);
        var current = new[] { UnderRepoRoot(repoRoot, string.Empty) };
        foreach (var segment in root.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = segment == "*"
                ? current.Where(Directory.Exists).SelectMany(Directory.EnumerateDirectories).ToArray()
                : current.Select(path => Path.Join(path, segment)).Where(Directory.Exists).ToArray();
        }

        return current;
    }

    private static string UnderRepoRoot(string repoRoot, string root)
    {
        if (Path.IsPathRooted(root))
            throw new InvalidOperationException($"Module root '{root}' must be relative to the repository root.");

        return Path.Join(repoRoot, root);
    }

    /// <summary>Whether <paramref name="path"/> lies outside every <c>tests/</c> directory, judged against <see cref="RepoPaths.RepoRoot"/>.</summary>
    internal static bool IsNotTestFile(string path) => IsNotTestFile(RepoPaths.RepoRoot, path);

    /// <summary>
    /// Whether <paramref name="path"/> lies outside every <c>tests/</c> directory, judged by the path's location
    /// relative to <paramref name="repoRoot"/> rather than the directories above the repository. An extension
    /// carries its own tests inside <c>src/</c>, so a production sweep over <see cref="Production"/> needs this.
    /// </summary>
    internal static bool IsNotTestFile(string repoRoot, string path) => !RepoPaths.HasSegment(repoRoot, path, "tests");
}
