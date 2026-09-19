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
    /// Rooted names are rejected rather than combined: <see cref="Path.Combine(string, string)"/> discards
    /// everything before an absolute segment, so an absolute entry would silently redirect a guard's sweep
    /// outside the repository and the guard would keep reporting green about whatever it found there.
    /// </remarks>
    internal static IEnumerable<string> Resolve(string repoRoot, params string[] roots) =>
        roots
            .Select(root => Path.IsPathRooted(root)
                ? throw new InvalidOperationException($"Module root '{root}' must be relative to the repository root.")
                : Path.Combine(repoRoot, root))
            .Where(Directory.Exists);

    /// <summary>Every <c>.cs</c> file under the given roots, excluding build output.</summary>
    internal static IEnumerable<string> SourceFiles(string repoRoot, params string[] roots) =>
        Resolve(repoRoot, roots).SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories));

    /// <summary>Shipping module source: <see cref="Production"/> with each extension's own tests left out.</summary>
    /// <remarks>
    /// A guard that sweeps <see cref="Production"/> raw also sweeps <c>extensions/&lt;bucket&gt;/tests/</c>, because an
    /// extension carries its tests inside its own root rather than under the repository's <c>tests/</c>. Guards that
    /// state a rule about production code — no EF namespace outside the admitted surfaces, no pruned contract
    /// reappearing — then fail on test code that was always allowed to do those things. Every such guard wants this.
    /// </remarks>
    internal static IEnumerable<string> ProductionSourceFiles(string repoRoot) =>
        SourceFiles(repoRoot, Production).Where(IsNotTestFile);

    /// <summary>The same exclusion for a path that is not necessarily a <c>.cs</c> file.</summary>
    internal static bool IsNotTestFile(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
