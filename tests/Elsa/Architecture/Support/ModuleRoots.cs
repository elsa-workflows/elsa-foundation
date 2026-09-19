namespace Elsa.Architecture.Tests;

/// <summary>
/// The repository roots that hold first-party code, and the one way these guards turn a root name into
/// a directory to sweep.
/// </summary>
/// <remarks>
/// Required modules live under <c>src/</c> and optional ones under <c>extensions/</c> (#1815), each
/// extension carrying its own <c>tests/</c> subtree. A guard pinned to a single root keeps compiling and
/// keeps passing once a module moves out of it; it simply stops looking at that module. Resolving roots
/// here means widening a sweep is one edit rather than one per call site.
/// </remarks>
internal static class ModuleRoots
{
    /// <summary>Every root holding project files, production and test alike.</summary>
    internal static readonly string[] All = ["src", "tests", "extensions"];

    /// <summary>The roots holding shipping module code. An extension's own tests live inside these.</summary>
    internal static readonly string[] Production = ["src", "extensions"];

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
        Resolve(repoRoot, roots).SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories));
}
