using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa3.Mapping.Tests.Support;

/// <summary>
/// Resolves a converted graph's dependencies the way the activity template compiler does for published activities
/// (spec 246 R-11): the version, contract and declared structure of each node's exact version. Built-ins come from
/// <see cref="RealActivityCatalog"/> with the contract source-owned publication derives from a catalog row; imported
/// reusable siblings come from their converted contract. Template ids and hashes are placeholders, which the provider
/// does not read.
/// </summary>
internal static class CatalogResolvedDependencies
{
    private const string JsonStorage = "elsa.json";

    public static async Task<IReadOnlyList<ActivityResolvedDependency>> ResolveAsync(
        RealActivityCatalog catalog,
        IEnumerable<ActivityTemplateDependencyDeclaration> declarations,
        IReadOnlyDictionary<string, ImportedSibling>? siblings = null)
    {
        var resolved = new List<ActivityResolvedDependency>();
        foreach (var declaration in declarations)
        {
            if (siblings?.GetValueOrDefault(declaration.DefinitionVersionId) is { } sibling)
            {
                resolved.Add(Dependency(declaration, sibling.DefinitionId, sibling.Version, sibling.Contract, null));
                continue;
            }

            var version = await catalog.FindVersion(declaration.DefinitionVersionId)
                          ?? throw new InvalidOperationException($"The catalog has no version '{declaration.DefinitionVersionId}'.");
            ActivityStructureContract? structure = null;
            if (ActivityStructureDesignFacetReader.TryReadSingle(version.DesignFacets, out var facet) && facet is not null)
                ActivityStructureDesignFacetReader.TryReadContract(facet, out structure);
            resolved.Add(Dependency(declaration, version.DefinitionId, version.Version, ToContract(version), structure));
        }

        return resolved;
    }

    private static ActivityResolvedDependency Dependency(
        ActivityTemplateDependencyDeclaration declaration,
        string definitionId,
        string version,
        ActivityContract contract,
        ActivityStructureContract? structure) => new(
        definitionId,
        declaration.DefinitionVersionId,
        version,
        $"template-{declaration.DefinitionVersionId}",
        $"sha256:{declaration.DefinitionVersionId}",
        contract,
        ActivityDefinitionVersionLifecycle.Active,
        null,
        declaration.OccurrenceId,
        declaration.NodeOrigin,
        declaration.ParentOccurrenceId,
        declaration.ChildSlotName,
        declaration.ChildIndex,
        declaration.MemberUsage,
        structure);

    /// <summary>
    /// A copy of <c>SourceOwnedActivityVersionPublisher.ToContract</c>, which is private to Publishing's API: the contract
    /// a built-in activity is published with, outcomes from its <c>elsa.outcomes</c> facet and <c>Done</c> without one.
    /// </summary>
    private static ActivityContract ToContract(IActivityDefinitionVersion version) => new(
        "1",
        version.Inputs.Select(input => new ActivityInputContract(
            input.ReferenceKey,
            input.Name,
            input.Type,
            input.IsRequired,
            input.IsNullable,
            input.DefaultValue is { } value ? new(input.DefaultSyntax ?? "Literal", value) : null,
            JsonStorage,
            DisplayName: input.DisplayName,
            Description: input.Description,
            Category: input.Category,
            Order: input.Order,
            UiHint: input.UiHint,
            UiSpecifications: input.UISpecifications)).ToArray(),
        version.Outputs.Select(output => new ActivityOutputContract(
            output.ReferenceKey,
            output.Name,
            output.Type,
            output.IsRequired,
            output.IsNullable,
            JsonStorage,
            DisplayName: output.DisplayName,
            Description: output.Description,
            Category: output.Category,
            Order: output.Order,
            UiHint: output.UiHint,
            UiSpecifications: output.UISpecifications,
            SourceRepresentation: output.SourceRepresentation)).ToArray(),
        ToOutcomes(version));

    private static IReadOnlyList<ActivityOutcomeContract> ToOutcomes(IActivityDefinitionVersion version)
    {
        var outcomes = new List<ActivityOutcomeContract>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var facet in version.DesignFacets.Where(facet =>
                     StringComparer.Ordinal.Equals(facet.Kind, "elsa.outcomes") &&
                     facet.Payload.ValueKind == JsonValueKind.Object &&
                     facet.Payload.TryGetProperty("ports", out var ports) &&
                     ports.ValueKind == JsonValueKind.Array))
        {
            foreach (var port in facet.Payload.GetProperty("ports").EnumerateArray())
            {
                if (port.ValueKind != JsonValueKind.Object ||
                    !port.TryGetProperty("name", out var name) ||
                    name.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(name.GetString()))
                    continue;
                var referenceKey = port.TryGetProperty("referenceKey", out var key) && key.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(key.GetString())
                    ? key.GetString()!
                    : name.GetString()!;
                if (seen.Add(referenceKey))
                    outcomes.Add(new(referenceKey, name.GetString()!, true));
            }
        }

        return outcomes.Count == 0 ? [new("Done", "Done", true)] : outcomes;
    }
}

/// <summary>An imported reusable version another one in the same collection uses, resolved from its conversion.</summary>
internal sealed record ImportedSibling(string DefinitionId, string Version, ActivityContract Contract);
