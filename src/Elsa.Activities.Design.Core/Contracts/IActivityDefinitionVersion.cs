using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

public interface IActivityDefinitionVersion
{
    string Id { get; }

    string Version { get; }

    string DefinitionId { get; }

    /// <summary>
    /// The descriptor type's <c>FullName</c> (e.g. <c>Elsa.Primitives.Models.TypeInformation</c>,
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

    IActivityDefinition Definition { get; }

    IEnumerable<InputDefinition> Inputs { get; }

    IEnumerable<OutputDefinition> Outputs { get; }

    IEnumerable<ActivityPortDefinition> Ports { get; }

    ActivityExecutionType ExecutionType { get; }

    /// <summary>
    /// Immutable content hash of this version's projection, computed by
    /// <c>IActivityDefinitionHasher</c> at reconciliation time (Model X duplicate-detection).
    /// </summary>
    string? ReconcilliationHash { get; }
}
