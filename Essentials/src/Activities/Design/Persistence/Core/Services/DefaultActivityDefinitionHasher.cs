using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elsa.Activities.Design.Persistence.Core.Services;

/// <summary>
/// Default implementation of <see cref="IActivityDefinitionHasher"/>. Computes a SHA-256
/// hash over a canonicalised JSON serialisation of the (definition, version) pair —
/// "canonical" meaning every object's properties are sorted alphabetically (ordinal) at
/// every level. Native .NET only: no third-party canonicalisation library.
/// </summary>
/// <remarks>
/// This is the sanctioned exception to the "all JSON via IPayloadSerializer" rule: hashing needs a
/// canonical sorted-key serialisation that the payload serializer does not produce, and the JSON is
/// consumed only here (it is never persisted or read by another component — only the SHA-256 is).
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
