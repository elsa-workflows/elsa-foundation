using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

public interface IActivityDefinitionVersion
{
    string Id { get; }

    string Version { get; }

    string DefinitionId { get; }

    /// <summary>
    /// The descriptor type's <c>FullName</c> (e.g. <c>Elsa.Primitives.Models.ClrActivityDescriptor</c>,
    /// <c>Elsa.Workflows.Primitives.Models.WorkflowIdentity</c>). The runtime construction registry's
    /// lookup key. The design domain treats this purely as an opaque string — it never resolves it to
    /// a CLR type.
    /// </summary>
    string DescriptorType { get; }

    /// <summary>
    /// The descriptor payload as opaque JSON. The design domain serializes/round-trips this without
    /// ever deserializing it into a concrete descriptor type; only the runtime feature that owns the
    /// descriptor type materializes it. A <see cref="JsonElement"/> (a BCL type) keeps the descriptor
    /// opaque and introduces no descriptor-type dependency (Elsa §E2.2).
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
