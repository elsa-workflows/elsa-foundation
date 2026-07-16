using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

public interface IActivityDefinitionVersion
{
    string Id { get; }

    string Version { get; }

    string DefinitionId { get; }

    /// <summary>Stable Design provider identity; never a CLR type name.</summary>
    string ProviderKey { get; }

    /// <summary>Schema understood by the Design provider that owns the source manifest.</summary>
    string ProviderSchemaVersion { get; }

    /// <summary>Stable Runtime consumer identity; never a CLR type name.</summary>
    string ConsumerKey { get; }

    /// <summary>Schema understood by the matching Runtime consumer.</summary>
    string ConsumerSchemaVersion { get; }

    /// <summary>
    /// The descriptor payload as opaque JSON. The design domain serializes/round-trips this without
    /// ever deserializing it into a concrete runtime type; only the Runtime consumer identified above
    /// materializes it. A <see cref="JsonElement"/> keeps the descriptor opaque across the seam.
    /// </summary>
    JsonElement DescriptorPayload { get; }

    /// <summary>
    /// Provenance: the kind of source that produced this version (e.g. <c>"CLR"</c>, <c>"Json"</c>,
    /// <c>"Workflow"</c>). Carried on the contribution so the reconciler can persist it. Write-once.
    /// </summary>
    string SourceKind { get; }

    /// <summary>
    /// Provenance: the source-side asset identity that produced this version. Write-once.
    /// </summary>
    string SourceId { get; }

    IActivityDefinition Definition { get; }

    IEnumerable<InputDefinition> Inputs { get; }

    IEnumerable<OutputDefinition> Outputs { get; }

    IEnumerable<ActivityDesignFacet> DesignFacets { get; }

    ActivityExecutionType ExecutionType { get; }

    /// <summary>
    /// Content hash of this version's projection, generated at construction by the version factory
    /// via <see cref="IActivityDefinitionHasher"/>. Reconciliation compares a candidate's hash to the
    /// persisted value to detect source-side changes (Model X duplicate-detection). Always set by the
    /// version factory; nullable only to accommodate directly-constructed entities in tests/fixtures.
    /// </summary>
    string? Hash { get; }
}
