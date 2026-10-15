using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Exceptions;
using Elsa.Primitives.Models;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.Mappings;
using Elsa3.Models;

namespace Elsa3.Mapping.Resolution;

/// <summary>
/// Resolves an Elsa 3 node to the Elsa 4 activity it becomes, for both value-flow lowering and node mapping, so the two
/// cannot disagree (spec FR-011). Order: plan replacement → built-in map → replaced-type alias → verbatim catalog key →
/// "unmapped activity type". A catalog target takes its highest valid-SemVer version; the Elsa 3 node's integer version
/// never selects it. Results are cached per Elsa 3 type name for the conversion scope.
/// </summary>
/// <remarks>Never throws for input or catalog content: a miss is an <see cref="Elsa3ResolutionFailure"/>.</remarks>
internal sealed class Elsa3ActivityResolver(
    IElsa3ActivityMap map,
    IActivityDefinitionLookup activityLookup)
{
    private static readonly JsonSerializerOptions FacetJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, Elsa3Resolution> _byType = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Elsa3Resolution> _byTypeKey = new(StringComparer.Ordinal);

    /// <summary>Resolves <paramref name="node"/>, honouring the plan's <paramref name="replacements"/> first.</summary>
    public async ValueTask<Elsa3Resolution> ResolveAsync(
        Elsa3Activity node,
        IReadOnlyDictionary<string, Elsa3ActivityExactReplacement> replacements,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(replacements);
        if (!string.IsNullOrWhiteSpace(node.NodeId) && replacements.TryGetValue(node.NodeId, out var replacement))
            return Elsa3Resolution.Resolved(FromReplacement(replacement));

        var type = node.Type ?? string.Empty;
        if (_byType.TryGetValue(type, out var cached))
            return cached;

        var resolution = await ResolveTypeAsync(type, cancellationToken);
        _byType[type] = resolution;
        return resolution;
    }

    /// <summary>Resolves an Elsa 4 catalog type key directly, for activities the conversion emits itself (e.g. Complete).</summary>
    public async ValueTask<Elsa3Resolution> ResolveCatalogTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken)
    {
        if (_byTypeKey.TryGetValue(activityTypeKey, out var cached))
            return cached;

        var resolution = await ResolveCatalogAsync(activityTypeKey, Elsa3ResolvedSource.BuiltInMap, null, cancellationToken)
                         ?? Elsa3Resolution.Failed(
                             Elsa3ActivityDiagnosticCodes.VersionUnavailable,
                             $"Elsa 4 activity '{activityTypeKey}' has no valid version in the activity catalog.",
                             "Install the Elsa 4 module that provides this activity and reconcile the catalog before converting.");
        _byTypeKey[activityTypeKey] = resolution;
        return resolution;
    }

    private async ValueTask<Elsa3Resolution> ResolveTypeAsync(string type, CancellationToken cancellationToken)
    {
        var entry = map.Find(type);
        if (entry is not null)
            return await ResolveEntryAsync(entry, cancellationToken);

        if (!string.IsNullOrWhiteSpace(type))
        {
            var verbatim = await ResolveCatalogAsync(type, Elsa3ResolvedSource.Verbatim, null, cancellationToken);
            if (verbatim is not null)
                return verbatim;
        }

        return Elsa3Resolution.Failed(
            Elsa3ActivityDiagnosticCodes.UnmappedType,
            $"Elsa 3 activity type '{type}' is not mapped to an Elsa 4 activity, not claimed by a replaced-type alias and not an Elsa 4 catalog key.",
            "Rebuild the activity for Elsa 4 and mark it [ReplacesActivityType(\"" + type + "\")], or remove the node before converting.");
    }

    private async ValueTask<Elsa3Resolution> ResolveEntryAsync(Elsa3ActivityMapEntry entry, CancellationToken cancellationToken)
    {
        switch (entry.Target)
        {
            case Elsa3ActivityTarget.CatalogActivity catalog:
                return await ResolveCatalogAsync(catalog.ActivityTypeKey, Elsa3ResolvedSource.BuiltInMap, entry, cancellationToken)
                       ?? Elsa3Resolution.Failed(
                           Elsa3ActivityDiagnosticCodes.VersionUnavailable,
                           $"Elsa 3 activity type '{entry.Elsa3TypeName}' maps to Elsa 4 activity '{catalog.ActivityTypeKey}', which has no valid version in the activity catalog.",
                           "Install the Elsa 4 module that provides this activity and reconcile the catalog before converting.");
            case Elsa3ActivityTarget.Intrinsic intrinsic:
                return Elsa3Resolution.Resolved(Elsa3IntrinsicContracts.Resolve(intrinsic.ActivityVersionId, entry));
            case Elsa3ActivityTarget.GraphLevel graph:
                return Elsa3Resolution.Failed(
                    Elsa3ActivityDiagnosticCodes.GraphConversionPending,
                    $"Elsa 3 activity type '{entry.Elsa3TypeName}' needs graph conversion: {graph.Reason}",
                    $"Graph conversion is tracked by {graph.OwningIssue}; its children are still converted node by node.");
            case Elsa3ActivityTarget.NoCounterpart none:
                return Elsa3Resolution.Failed(
                    Elsa3ActivityDiagnosticCodes.NoCounterpart,
                    $"Elsa 3 activity type '{entry.Elsa3TypeName}' has no Elsa 4 counterpart: {none.Reason}",
                    "Redesign this part of the workflow with Elsa 4 activities before converting.");
            default:
                throw new InvalidOperationException($"Unknown activity target '{entry.Target.GetType().Name}'.");
        }
    }

    /// <summary>
    /// The highest valid-SemVer catalog version of <paramref name="idOrTypeKey"/>, or null when the catalog has no such
    /// definition or no valid version.
    /// </summary>
    private async ValueTask<Elsa3Resolution?> ResolveCatalogAsync(
        string idOrTypeKey,
        Elsa3ResolvedSource source,
        Elsa3ActivityMapEntry? entry,
        CancellationToken cancellationToken)
    {
        try
        {
            var definition = await activityLookup.GetDefinition(idOrTypeKey, cancellationToken);
            var versions = await activityLookup.ListVersions(definition.Id, cancellationToken);
            var highest = versions
                .Select(summary => (summary, valid: SemVer.TryParse(summary.Version, out var semVer), semVer))
                .Where(item => item.valid)
                .OrderByDescending(item => item.semVer)
                .ThenBy(item => item.summary.Id, StringComparer.Ordinal)
                .Select(item => item.summary)
                .FirstOrDefault();
            if (highest is null)
                return null;

            var version = await activityLookup.GetVersion(highest.Id, cancellationToken);
            return Elsa3Resolution.Resolved(new Elsa3ResolvedActivity(
                source,
                entry,
                version.Id,
                definition.ActivityTypeKey,
                version.Inputs.ToArray(),
                version.Outputs.ToArray(),
                ReadStructure(version.DesignFacets)));
        }
        catch (EntityNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Elsa3StructureContract? ReadStructure(IEnumerable<ActivityDesignFacet> facets)
    {
        if (!ActivityStructureDesignFacetReader.TryReadSingle(facets, out var facet) || facet is null)
            return null;

        var payload = facet.Payload.Deserialize<ActivityStructureDesignFacetPayload>(FacetJsonOptions);
        return payload is null ? null : new Elsa3StructureContract(facet.Kind, facet.SchemaVersion, payload);
    }

    private static Elsa3ResolvedActivity FromReplacement(Elsa3ActivityExactReplacement replacement) =>
        new(
            Elsa3ResolvedSource.Replacement,
            null,
            replacement.ActivityVersionId,
            null,
            replacement.Inputs.ToArray(),
            replacement.OutputNames
                .Order(StringComparer.Ordinal)
                .Select(name => new OutputDefinition(name, name, new TypeReference("Object"), null, name, null, true))
                .ToArray(),
            null);
}
