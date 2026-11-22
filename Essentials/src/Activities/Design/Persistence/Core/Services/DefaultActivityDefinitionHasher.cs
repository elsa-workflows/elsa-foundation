using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elsa.Activities.Design.Persistence.Core.Services;

/// <summary>
/// Default implementation of <see cref="IActivityDefinitionHasher"/>. Computes a SHA-256
/// hash of the (definition, version) projection through the shared JsonHashInput exact canonical form.
/// </summary>
/// <remarks>
/// Hash projections use JsonHashInput; canonical hash bytes are never persisted as domain data.
/// Excluded from the hash: <c>LastModifiedAt</c>/<c>CreatedAt</c>/<c>RowNumber</c>, provenance,
/// identity fields such as <c>Id</c> and <c>Version</c>, nested versions on the parent, and the
/// presentation-only <c>elsa.activity-icon</c> design facet. Input <c>Tier</c> is hashed so a
/// changed disclosure group re-reconciles the stored version.
/// </remarks>
public sealed class DefaultActivityDefinitionHasher : IActivityDefinitionHasher
{
    private static readonly HashSet<string> ExcludedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Primitives.Entities.Entity.LastModifiedAt),
        nameof(Primitives.Entities.Entity.CreatedAt),
        nameof(Primitives.Entities.Entity.RowNumber),
    };
    public string Hash(IActivityDefinition definition, IActivityDefinitionVersion version)
    {
        var payload = new
        {
            Definition = ProjectDefinition(definition),
            Version = ProjectVersion(version),
        };

        return Elsa.Serialization.Core.JsonHashInput.Hash(payload).ToUpperInvariant();
    }

    // Projects the definition to a plain object containing only the content-bearing fields.
    // Provenance (SourceKind/SourceId) is deliberately excluded — it identifies where a row came
    // from, not what it contains; including it would let a re-source defeat duplicate detection.
    private static object ProjectDefinition(IActivityDefinition d) => new
    {
        d.ActivityTypeKey,
        d.Category,
        d.DisplayName,
        d.Description,
    };

    // Version is deliberately excluded here: it identifies the version row, not its projected content.
    private static object ProjectVersion(IActivityDefinitionVersion v) => new
    {
        v.ProviderKey,
        v.ProviderSchemaVersion,
        v.ConsumerKey,
        v.ConsumerSchemaVersion,
        v.DescriptorPayload,
        v.ExecutionType,
        v.Inputs,
        v.Outputs,
        DesignFacets = ExcludePresentationFacets(v.DesignFacets),
    };

    private static IEnumerable<ActivityDesignFacet> ExcludePresentationFacets(IEnumerable<ActivityDesignFacet> facets) =>
        facets.Where(facet => !string.Equals(facet.Kind, ActivityIconName.FacetKind, StringComparison.Ordinal)
            && !string.Equals(facet.Kind, "elsa.activity-palette", StringComparison.Ordinal));

}
