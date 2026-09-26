using System.Text.RegularExpressions;

namespace Elsa.Maps.Generator;

/// <summary>
/// Resolves the version a <c>PackageReference</c> actually gets, honouring central package management.
/// </summary>
/// <remarks>
/// The repo uses <c>ManagePackageVersionsCentrally</c>, so most <c>PackageReference</c> elements carry no
/// version and the value comes from <c>Directory.Packages.props</c>. A handful of pins are conditioned on
/// <c>MSBuildProjectName</c>; when several entries match, the last one wins, mirroring MSBuild.
/// </remarks>
public sealed partial class PackageVersions
{
    [GeneratedRegex("""<PackageVersion\s[^>]*""", RegexOptions.Compiled)]
    private static partial Regex PackageVersionElementPattern { get; }

    private readonly IReadOnlyList<(string Id, string Version, string Condition)> _central;

    private PackageVersions(IReadOnlyList<(string, string, string)> central) => _central = central;

    /// <summary>Reads <c>Directory.Packages.props</c>, or an empty set when it is absent.</summary>
    public static PackageVersions Load(RepoContext repo)
    {
        var path = Path.Combine(repo.Root, "Directory.Packages.props");
        if (!File.Exists(path)) return new PackageVersions([]);

        var entries = PackageVersionElementPattern.Matches(File.ReadAllText(path))
            .Select(match => (
                Id: Attribute(match.Value, "Include"),
                Version: Attribute(match.Value, "Version"),
                Condition: Attribute(match.Value, "Condition")))
            .Where(entry => entry.Id.Length > 0)
            .ToArray();

        return new PackageVersions(entries);
    }

    /// <summary>
    /// The version a <c>PackageReference</c> to <paramref name="id"/> gets in <paramref name="projectName"/>:
    /// its inline version when it declares one, otherwise the matching central entry; null when neither exists.
    /// </summary>
    public string? Resolve(string id, string inlineVersion, string projectName)
    {
        if (inlineVersion.Length > 0) return inlineVersion;

        var selected = string.Empty;
        foreach (var entry in _central)
            if (entry.Id == id && ConditionMatches(entry.Condition, projectName))
                selected = entry.Version;

        return selected.Length > 0 ? selected : null;
    }

    /// <summary>
    /// Evaluates the only condition shapes the repo uses today: equality and inequality on
    /// <c>MSBuildProjectName</c>. Anything else is treated as non-matching rather than guessed at.
    /// </summary>
    private static bool ConditionMatches(string condition, string projectName)
    {
        if (condition.Length == 0) return true;

        const string equals = "'$(MSBuildProjectName)' == '";
        const string notEquals = "'$(MSBuildProjectName)' != '";

        if (condition.StartsWith(equals, StringComparison.Ordinal))
            return Trailing(condition, equals) == projectName;

        if (condition.StartsWith(notEquals, StringComparison.Ordinal))
            return Trailing(condition, notEquals) != projectName;

        return false;
    }

    private static string Trailing(string condition, string prefix) => condition[prefix.Length..].TrimEnd('\'');

    private static string Attribute(string element, string name)
    {
        var match = Regex.Match(element, $"{name}=\"([^\"]*)\"");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }
}
