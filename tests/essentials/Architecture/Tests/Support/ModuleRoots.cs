namespace Elsa.Architecture.Tests;

/// <summary>
/// The repository roots that hold first-party code, and the one way these guards turn a root name into
/// a directory to sweep.
/// </summary>
/// <remarks>
/// All module code lives under <c>src/</c>: required modules in <c>src/core</c>, optional ones in
/// <c>src/extensions</c> (#1815), hosts in <c>src/apps</c>. Every test project sits under <c>tests/</c>, extension
/// tests under <c>tests/extensions</c> (#2579). A guard pinned to a single root
/// keeps compiling and keeps passing once a module moves out of it; it simply stops looking at that module.
/// Resolving roots here means widening a sweep is one edit rather than one per call site.
/// </remarks>
internal static class ModuleRoots
{
    /// <summary>Every root holding project files, production and test alike.</summary>
    internal static readonly string[] All = ["src", "tests"];

    /// <summary>The roots holding shipping module code.</summary>
    internal static readonly string[] Production = ["src"];

    /// <summary>
    /// Resolves root names to existing absolute directories under <paramref name="repoRoot"/>.
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
        roots.Select(root => UnderRepoRoot(repoRoot, root)).Where(Directory.Exists);

    private static string UnderRepoRoot(string repoRoot, string root)
    {
        if (Path.IsPathRooted(root))
            throw new InvalidOperationException($"Module root '{root}' must be relative to the repository root.");

        return Path.Join(repoRoot, root);
    }

    /// <summary>Every <c>.cs</c> file under the given roots, excluding build output.</summary>
    internal static IEnumerable<string> SourceFiles(string repoRoot, params string[] roots) =>
        Resolve(repoRoot, roots)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !RepoPaths.IsBuildOutput(repoRoot, file));

    /// <summary>Shipping module source: <see cref="Production"/> with each extension's own tests left out.</summary>
    /// <remarks>
    /// Extension tests moved to <c>tests/extensions</c> (#2579), so <see cref="Production"/> holds no test project
    /// today. The exclusion stays so that a test folder added under <c>src/</c> again cannot make guards that state a
    /// rule about production code (no EF namespace outside the admitted surfaces, no pruned contract reappearing)
    /// fail on test code that was always allowed to do those things.
    /// </remarks>
    internal static IEnumerable<string> ProductionSourceFiles(string repoRoot) =>
        SourceFiles(repoRoot, Production).Where(file => IsNotTestFile(repoRoot, file));

    /// <summary>The same exclusion for a path that is not necessarily a <c>.cs</c> file, judged against <see cref="RepoPaths.RepoRoot"/>.</summary>
    internal static bool IsNotTestFile(string path) => IsNotTestFile(RepoPaths.RepoRoot, path);

    /// <summary>
    /// The same exclusion for a path that is not necessarily a <c>.cs</c> file, judged by the path's location
    /// relative to <paramref name="repoRoot"/> rather than the directories above the repository.
    /// </summary>
    internal static bool IsNotTestFile(string repoRoot, string path) => !RepoPaths.HasSegment(repoRoot, path, "tests");
}
