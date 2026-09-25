using System.Collections.Immutable;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Modularity.Planning.Bridge;

/// <summary>A safe role, not a physical or source file name.</summary>
public sealed record CompositionHandoffFile(string Role, int Ordinal);

/// <summary>A label for one reviewed local artifact; deployment and activation remain unchecked.</summary>
public sealed record CandidateHandoff(
    string SchemaVersion,
    string CandidateId,
    string Host,
    string Shell,
    string Environment,
    CatalogPin Catalog,
    ImmutableArray<string> AcceptedFeatureIds,
    ImmutableArray<CompositionHandoffFile> IncludedFiles,
    ImmutableArray<string> Unresolved,
    string DeploymentIntegrity,
    string Activation,
    DateTimeOffset CreatedAt);

/// <summary>Projects a complete local candidate without serializing source bytes or change tokens.</summary>
public static class CompositionHandoff
{
    public static CandidateHandoff Create(
        string hostAlias,
        SourceSnapshot source,
        CatalogPin catalog,
        IEnumerable<string> acceptedFeatureIds,
        IReadOnlyDictionary<string, byte[]> candidateFiles,
        IEnumerable<string> findingCodes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(acceptedFeatureIds);
        ArgumentNullException.ThrowIfNull(candidateFiles);
        ArgumentNullException.ThrowIfNull(findingCodes);

        if (!SafeHostAlias(hostAlias) ||
            !SelectionValueRules.IsSafeReference(source.Selection.ShellId) ||
            !SafeEnvironment(source.Selection.Environment) ||
            !SelectionValueRules.IsSafeReference(catalog.Id) ||
            !SelectionValueRules.IsSafeReference(catalog.Version) ||
            !SelectionValueRules.IsDigest(catalog.Digest) ||
            candidateFiles.Count == 0 ||
            !source.FileNames.ToHashSet(StringComparer.Ordinal).SetEquals(candidateFiles.Keys))
            throw Incomplete();

        var features = acceptedFeatureIds.ToImmutableArray();
        var findings = findingCodes.ToImmutableArray();
        if (features.Any(id => !SelectionValueRules.IsSafeReference(id)) ||
            features.Length != features.Distinct(StringComparer.Ordinal).Count() ||
            findings.Any(code => !SelectionValueRules.IsSafeReference(code)))
            throw Incomplete();

        var roles = ImmutableArray.CreateBuilder<CompositionHandoffFile>(candidateFiles.Count);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in candidateFiles.Keys.Order(StringComparer.Ordinal))
        {
            if (candidateFiles[name] is null)
                throw Incomplete();
            var role = RoleFor(name, source.Selection);
            counts.TryGetValue(role, out var count);
            counts[role] = ++count;
            roles.Add(new CompositionHandoffFile(role, count));
        }

        return new CandidateHandoff(
            "1",
            Guid.NewGuid().ToString("N"),
            hostAlias,
            source.Selection.ShellId,
            source.Selection.Environment,
            catalog,
            features.Order(StringComparer.Ordinal).ToImmutableArray(),
            roles.ToImmutable(),
            [.. findings.Concat(["package-inventory-unchecked", "connection-target-unchecked", "deployment-unchecked", "host-readiness-unchecked"])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            "external-attestation-required",
            "unchecked",
            DateTimeOffset.UtcNow);
    }

    private static string RoleFor(string name, SourceSelection selection)
    {
        if (name.Equals("shells.json", StringComparison.OrdinalIgnoreCase)) return "shells-base";
        if (name.Equals(selection.ShellOverlayFileName, StringComparison.OrdinalIgnoreCase)) return "shells-selected-overlay";
        if (name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)) return "appsettings-base";
        if (name.Equals(selection.AppsettingsOverlayFileName, StringComparison.OrdinalIgnoreCase)) return "appsettings-selected-overlay";
        if (SafeSiblingName(name)) return "unselected-copy";
        throw Incomplete();
    }

    private static bool SafeSiblingName(string name) =>
        (name.StartsWith("shells.", StringComparison.OrdinalIgnoreCase) || name.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)) &&
        name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
        SafeEnvironment(name[(name.IndexOf('.') + 1)..^5]);

    private static bool SafeEnvironment(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');

    private static bool SafeHostAlias(string? value) => value is { Length: > 0 and <= 80 } &&
        char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.');

    private static CompositionImportException Incomplete() =>
        new("candidate-incomplete", "The reviewed candidate cannot be handed off safely.");
}
