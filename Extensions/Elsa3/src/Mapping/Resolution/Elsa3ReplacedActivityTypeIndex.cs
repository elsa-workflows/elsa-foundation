using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Versioning;
using Elsa3.Models;

namespace Elsa3.Mapping.Resolution;

/// <summary>
/// Which catalog activity replaces an Elsa 3 type (spec FR-009, research R-2). CLR reconciliation records an
/// activity's <c>[ReplacesActivityType]</c> declarations as design facet <c>elsa.replaced-activity-types</c>; the index
/// reads each definition's highest valid-SemVer version once per conversion scope, so removing the declaration in a
/// new version ends the alias. Two definitions claiming one type make it ambiguous. A removed definition claims
/// nothing.
/// </summary>
/// <remarks>The store is optional: a host without an activity version store has no aliases.</remarks>
internal sealed class Elsa3ReplacedActivityTypeIndex(
    IActivityDefinitionVersionStore? store = null,
    IActivityDefinitionLookup? lookup = null)
{
    /// <summary>The facet the CLR scanner writes (<c>ClrAssemblyScanner.ReplacedActivityTypesFacetKind</c>).</summary>
    internal const string FacetKind = "elsa.replaced-activity-types";

    private IReadOnlyDictionary<string, string[]>? _definitionsByType;

    /// <summary>The definition that replaces <paramref name="elsa3Type"/>, none, or why the alias cannot be used.</summary>
    public async ValueTask<Elsa3AliasLookup> FindAsync(string elsa3Type, CancellationToken cancellationToken)
    {
        _definitionsByType ??= await BuildAsync(cancellationToken);
        if (!_definitionsByType.TryGetValue(elsa3Type, out var definitionIds))
            return Elsa3AliasLookup.None;
        if (definitionIds.Length == 1)
            return new Elsa3AliasLookup(definitionIds[0], null);

        return new Elsa3AliasLookup(null, new Elsa3ResolutionFailure(
            Elsa3ActivityDiagnosticCodes.AmbiguousAlias,
            $"Elsa 3 activity type '{elsa3Type}' is claimed by more than one activity: {string.Join(", ", definitionIds.Select(id => $"'{id}'"))}.",
            "Remove the [ReplacesActivityType] declaration from all but one activity, in a new version, and reconcile."));
    }

    private async ValueTask<IReadOnlyDictionary<string, string[]>> BuildAsync(CancellationToken cancellationToken)
    {
        if (store is null)
            return new Dictionary<string, string[]>(StringComparer.Ordinal);

        var versions = await store.ListAsync(cancellationToken);
        // The store keeps removed definitions' versions; the lookup lists only definitions that are not removed.
        var active = lookup is null
            ? null
            : (await lookup.ListDefinitions(cancellationToken: cancellationToken)).Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
        var highest = versions
            .Where(version => active is null || active.Contains(version.DefinitionId))
            .Select(version => (version, valid: SemVer.TryParse(version.Version, out var semVer), semVer))
            .Where(item => item.valid)
            .GroupBy(item => item.version.DefinitionId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.semVer).ThenBy(item => item.version.Id, StringComparer.Ordinal).First().version);

        var claims = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var version in highest)
        {
            foreach (var typeName in TypeNames(version.DesignFacets))
            {
                if (!claims.TryGetValue(typeName, out var definitions))
                    claims[typeName] = definitions = new SortedSet<string>(StringComparer.Ordinal);
                definitions.Add(version.DefinitionId);
            }
        }

        return claims.ToDictionary(item => item.Key, item => item.Value.ToArray(), StringComparer.Ordinal);
    }

    private static IEnumerable<string> TypeNames(IEnumerable<Elsa.Activities.Design.Core.Models.ActivityDesignFacet> facets) =>
        facets
            .Where(facet => StringComparer.Ordinal.Equals(facet.Kind, FacetKind) &&
                            facet.Payload.ValueKind == JsonValueKind.Object &&
                            facet.Payload.TryGetProperty("typeNames", out var names) &&
                            names.ValueKind == JsonValueKind.Array)
            .SelectMany(facet => facet.Payload.GetProperty("typeNames").EnumerateArray())
            .Where(name => name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
            .Select(name => name.GetString()!.Trim());
}

/// <summary>An alias lookup: the replacing definition, nothing, or why the alias cannot be used.</summary>
internal sealed record Elsa3AliasLookup(string? DefinitionId, Elsa3ResolutionFailure? Failure)
{
    public static readonly Elsa3AliasLookup None = new(null, null);
}
