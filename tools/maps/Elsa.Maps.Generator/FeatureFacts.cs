using System.Text.RegularExpressions;

namespace Elsa.Maps.Generator;

/// <summary>One CShells feature class discovered in source.</summary>
public sealed record FeatureFacts(
    string Domain,
    string Project,
    string ClassName,
    string FeatureId,
    bool IsAbstract,
    string BaseText,
    string RelativePath,
    IReadOnlyList<string> Properties,
    IReadOnlyList<string> OptionSignals);

/// <summary>Finds feature classes and the configuration evidence the dependency map reports.</summary>
public static partial class FeatureScanner
{
    [GeneratedRegex(@"^public (abstract )?((sealed|partial) )*class (?<name>[A-Za-z0-9_]+)\s*:\s*", RegexOptions.Compiled)]
    private static partial Regex FeatureClassPattern { get; }

    [GeneratedRegex(@"I(?:Web)?ShellFeature|FastEndpointsFeatureBase|EFCore.*FeatureBase|FeatureBase", RegexOptions.Compiled)]
    private static partial Regex FeatureBasePattern { get; }

    [GeneratedRegex("name:\\s*\"(?<id>[^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex ShellFeatureNamedArgumentPattern { get; }

    [GeneratedRegex("^\\s*\"(?<id>[^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex ShellFeaturePositionalArgumentPattern { get; }

    [GeneratedRegex(@"^\s*public\s+(?<type>.+)\s+(?<name>[A-Za-z0-9_]+)\s*\{\s*get;\s*set;\s*\}", RegexOptions.Compiled)]
    private static partial Regex AutoPropertyPattern { get; }

    /// <summary>The whitespace-insensitive signal probes the shell version applied to each feature file.</summary>
    private static readonly (string Label, Regex Pattern)[] OptionSignalProbes =
    [
        ("code default", BuildProbe(@"Path\.GetTempPath|DefaultConnectionString|TimeSpan\.From|=\s*true|=\s*false|=\s*100|=\s*string\.Empty")),
        ("filesystem path signal", BuildProbe("FolderPath|Directory|FilePath|LocalCacheDirectory|LocksFolderPath")),
        ("sensitive or deployment-specific value signal", BuildProbe("ConnectionString|Secret|Password|Token|Key")),
        ("type-name selection signal", BuildProbe(@"Type\s*\{|TypeName|Type\s*=|GetLoadedType")),
        ("validation/requiredness guard in code", BuildProbe("FeatureConfigurationException|requires exactly one|requires a non-empty|must specify|not configured"))
    ];

    private static Regex BuildProbe(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Scans every <c>src</c> source file for feature classes, ordered by domain, project, class.</summary>
    public static IReadOnlyList<FeatureFacts> Scan(RepoContext repo, IReadOnlyList<ProjectFacts> projects)
    {
        var sourceProjects = projects.Where(project => project.Kind == "source").ToArray();

        return repo.ListFiles("src/*.cs")
            .SelectMany(relativePath => ScanFile(repo, sourceProjects, relativePath))
            .OrderBy(feature => feature.Domain, StringComparer.Ordinal)
            .ThenBy(feature => feature.Project, StringComparer.Ordinal)
            .ThenBy(feature => feature.ClassName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<FeatureFacts> ScanFile(RepoContext repo, IReadOnlyList<ProjectFacts> projects, string relativePath)
    {
        var text = File.ReadAllText(repo.Absolute(relativePath));
        if (!FeatureBasePattern.IsMatch(text)) yield break;

        var owner = ProjectGraph.OwningProject(projects, relativePath, project => project.RelativePath);
        if (owner is null) yield break;

        var lines = text.Split('\n');
        var properties = ReadProperties(lines);
        var signals = ReadOptionSignals(text);

        // [ShellFeature(...)] may wrap across lines, so accumulate its argument text until the closing ")]".
        var attribute = string.Empty;
        var collecting = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');

            if (line.Contains("[ShellFeature(", StringComparison.Ordinal))
            {
                attribute = line[(line.IndexOf("[ShellFeature(", StringComparison.Ordinal) + "[ShellFeature(".Length)..];
                collecting = true;
            }
            else if (collecting)
            {
                attribute += " " + line;
            }

            if (collecting && line.Contains(")]", StringComparison.Ordinal))
                collecting = false;

            var normalized = Regex.Replace(line, @"[ \t\r]+", " ").Trim();
            var match = FeatureClassPattern.Match(normalized);
            if (!match.Success) continue;

            var baseText = BaseText(normalized);
            if (!FeatureBasePattern.IsMatch(baseText)) continue;

            yield return new FeatureFacts(
                ProjectGraph.DomainGroup(owner.Name),
                owner.Name,
                match.Groups["name"].Value,
                ShellFeatureId(attribute),
                normalized.StartsWith("public abstract ", StringComparison.Ordinal),
                baseText,
                relativePath,
                properties,
                signals);

            attribute = string.Empty;
            collecting = false;
        }
    }

    /// <summary>Everything after the last colon and before the body, matching the shell's greedy trim.</summary>
    private static string BaseText(string normalized)
    {
        var colon = normalized.LastIndexOf(':');
        var text = colon < 0 ? normalized : normalized[(colon + 1)..];

        var brace = text.IndexOf('{');
        if (brace >= 0) text = text[..brace];

        return text.Trim();
    }

    private static string ShellFeatureId(string attributeArguments)
    {
        if (attributeArguments.Length == 0) return "-";

        var named = ShellFeatureNamedArgumentPattern.Match(attributeArguments);
        if (named.Success) return named.Groups["id"].Value;

        var positional = ShellFeaturePositionalArgumentPattern.Match(attributeArguments.Trim());
        return positional.Success ? positional.Groups["id"].Value : "-";
    }

    private static IReadOnlyList<string> ReadProperties(IEnumerable<string> lines) =>
        lines.Select(line => AutoPropertyPattern.Match(line.TrimEnd('\r')))
            .Where(match => match.Success)
            .Select(match => $"{match.Groups["name"].Value}: {match.Groups["type"].Value.TrimEnd()}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadOptionSignals(string text)
    {
        var flattened = text.Replace('\n', ' ');
        return OptionSignalProbes.Where(probe => probe.Pattern.IsMatch(flattened)).Select(probe => probe.Label).ToArray();
    }

}
