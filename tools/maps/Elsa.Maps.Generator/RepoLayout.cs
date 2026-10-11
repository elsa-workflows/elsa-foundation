namespace Elsa.Maps.Generator;

/// <summary>Where backend maps look for code, and how they classify its paths.</summary>
/// <remarks>
/// Required modules, tests and deployable hosts live under <c>Essentials/</c>; optional backend modules live under
/// <c>Extensions/&lt;capability&gt;/src</c> with their tests beside them. Studio has its own tree under
/// <c>Extensions/&lt;capability&gt;/studio</c> and is deliberately excluded from these backend maps. Matching project
/// pathspecs at the module roots keeps that exclusion explicit instead of sweeping the whole <c>Extensions/</c> tree.
/// </remarks>
public static class RepoLayout
{
    /// <summary>Pathspecs covering every backend project the maps describe, source and test alike.</summary>
    public static readonly string[] ProjectPathspecs =
    [
        "Essentials/src/*.csproj",
        "Essentials/apps/*.csproj",
        "Essentials/tests/*.csproj",
        "Extensions/*/src/*.csproj",
        "Extensions/*/tests/*.csproj"
    ];

    /// <summary>Pathspecs covering production C# source, excluding tests and Studio projects.</summary>
    public static readonly string[] SourceFilePathspecs =
    [
        "Essentials/src/*.cs",
        "Essentials/apps/*.cs",
        "Extensions/*/src/*.cs"
    ];

    /// <summary>Pathspecs covering extension-point catalogs in backend module roots.</summary>
    public static readonly string[] CatalogPathspecs =
    [
        "Essentials/src/*.md",
        "Essentials/apps/*.md",
        "Extensions/*/src/*.md"
    ];

    /// <summary>Tracked C# files in backend production roots, excluding any nested test subtree.</summary>
    public static IReadOnlyList<string> SourceFiles(RepoContext repo) =>
        repo.ListFiles(SourceFilePathspecs).Where(IsModulePath).ToArray();

    /// <summary>True when the path belongs to backend tests, including extension-owned test trees.</summary>
    public static bool IsTestPath(string relativePath) =>
        relativePath.StartsWith("Essentials/tests/", StringComparison.Ordinal) ||
        relativePath.Contains("/tests/", StringComparison.Ordinal);

    /// <summary>True when a project file belongs to a backend test-project root, never an embedded Studio tree.</summary>
    public static bool IsTestProjectPath(string relativePath)
    {
        if (relativePath.StartsWith("Essentials/tests/", StringComparison.Ordinal))
            return true;

        var segments = relativePath.Split('/');
        return segments.Length >= 4 && segments[0] == "Extensions" && segments[2] == "tests";
    }

    /// <summary>The <c>source</c> / <c>test</c> label the maps group by.</summary>
    public static string Kind(string relativePath) => IsTestPath(relativePath) ? "test" : "source";

    /// <summary>True when the path belongs to an Essentials or extension backend production root.</summary>
    public static bool IsModulePath(string relativePath) =>
        !IsTestPath(relativePath) &&
        (relativePath.StartsWith("Essentials/src/", StringComparison.Ordinal) ||
         relativePath.StartsWith("Essentials/apps/", StringComparison.Ordinal) ||
         IsExtensionSourcePath(relativePath));

    /// <summary>True for a backend extension source path; Studio's sibling subtree is intentionally outside this domain.</summary>
    public static bool IsExtensionSourcePath(string relativePath)
    {
        var segments = relativePath.Split('/');
        return segments.Length >= 4 && segments[0] == "Extensions" && segments[2] == "src";
    }
}
