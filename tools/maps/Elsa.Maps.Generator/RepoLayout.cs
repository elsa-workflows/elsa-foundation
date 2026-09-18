namespace Elsa.Maps.Generator;

/// <summary>
/// Where the maps look for code, and how they decide whether a path is test code.
/// </summary>
/// <remarks>
/// Issue #1815 adds a second source root: optional modules move to <c>extensions/&lt;Name&gt;/src|tests</c>
/// while required ones stay under <c>src/</c> and <c>tests/</c>. Both roots are scanned here so a module
/// keeps its place in every generated map when it moves, rather than silently disappearing from them.
/// <para>
/// The test rule is the part that cannot stay as it was. It used to be a bare <c>tests/</c> prefix, which
/// would classify <c>extensions/Bpmn/tests/…</c> as production code: the test map would undercount its
/// suites, and the feature scan would read test fixtures as shipping features. Matching <c>/tests/</c>
/// anywhere covers both layouts. No path under <c>src/</c>, <c>tools/</c> or <c>samples/</c> contains that
/// segment today, so the broader rule reclassifies nothing that exists now.
/// </para>
/// <para>
/// These are git pathspecs, not filesystem globs: <c>*</c> crosses directory separators, so
/// <c>extensions/*.csproj</c> matches at any depth beneath the root.
/// </para>
/// </remarks>
public static class RepoLayout
{
    /// <summary>Pathspecs covering every project the maps describe, source and test alike.</summary>
    public static readonly string[] ProjectPathspecs = ["src/*.csproj", "tests/*.csproj", "extensions/*.csproj"];

    /// <summary>Pathspecs covering the C# sources the feature scanners read.</summary>
    public static readonly string[] SourceFilePathspecs = ["src/*.cs", "extensions/*.cs"];

    /// <summary>Pathspecs covering every extension-point catalog outside the repository root.</summary>
    public static readonly string[] CatalogPathspecs =
        ["src/EXTENSION_POINTS.md", "src/*/EXTENSION_POINTS.md", "extensions/*/EXTENSION_POINTS.md"];

    /// <summary>True when the path belongs to a test project under either source root.</summary>
    public static bool IsTestPath(string relativePath) =>
        relativePath.StartsWith("tests/", StringComparison.Ordinal) ||
        relativePath.Contains("/tests/", StringComparison.Ordinal);

    /// <summary>The <c>source</c> / <c>test</c> label the maps group by.</summary>
    public static string Kind(string relativePath) => IsTestPath(relativePath) ? "test" : "source";

    /// <summary>True when the path holds first-party module code, as opposed to tests, tools or samples.</summary>
    public static bool IsModulePath(string relativePath) =>
        !IsTestPath(relativePath) &&
        (relativePath.StartsWith("src/", StringComparison.Ordinal) ||
         relativePath.StartsWith("extensions/", StringComparison.Ordinal));
}
