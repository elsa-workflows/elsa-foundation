namespace Elsa.Maps.Generator;

/// <summary>
/// Where the maps look for code, and how they decide whether a path is test code.
/// </summary>
/// <remarks>
/// Module code lives under one root. <c>src/essentials</c> holds the required modules, <c>src/extensions</c> the
/// optional ones (#1815), and <c>src/apps</c> the deployable hosts; every test project sits under <c>tests/</c>,
/// extension tests under <c>tests/extensions</c> (#2579).
/// <para>
/// The test rule also matches <c>/tests/</c> anywhere, so a test folder added under <c>src/</c> again is still read
/// as test code rather than as shipping features. No production path contains that segment, so the broader rule
/// misclassifies nothing.
/// </para>
/// <para>
/// These are git pathspecs, not filesystem globs: <c>*</c> crosses directory separators, so
/// <c>src/*.csproj</c> matches at any depth beneath the root.
/// </para>
/// </remarks>
public static class RepoLayout
{
    /// <summary>Pathspecs covering every project the maps describe, source and test alike.</summary>
    public static readonly string[] ProjectPathspecs = ["src/*.csproj", "tests/*.csproj"];

    /// <summary>Pathspecs covering the C# sources the feature scanners read.</summary>
    public static readonly string[] SourceFilePathspecs = ["src/*.cs"];

    /// <summary>Pathspecs covering every extension-point catalog outside the repository root.</summary>
    public static readonly string[] CatalogPathspecs = ["src/EXTENSION_POINTS.md", "src/*/EXTENSION_POINTS.md"];

    /// <summary>True when the path belongs to a test project, under either placement.</summary>
    public static bool IsTestPath(string relativePath) =>
        relativePath.StartsWith("tests/", StringComparison.Ordinal) ||
        relativePath.Contains("/tests/", StringComparison.Ordinal);

    /// <summary>The <c>source</c> / <c>test</c> label the maps group by.</summary>
    public static string Kind(string relativePath) => IsTestPath(relativePath) ? "test" : "source";

    /// <summary>True when the path holds first-party module code, as opposed to tests, tools or samples.</summary>
    public static bool IsModulePath(string relativePath) =>
        !IsTestPath(relativePath) && relativePath.StartsWith("src/", StringComparison.Ordinal);
}
