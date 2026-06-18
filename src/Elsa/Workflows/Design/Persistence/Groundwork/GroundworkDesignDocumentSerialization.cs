using System.Text.Json;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Entities;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>
/// JSON serialization settings for the <b>rich</b> workflow-design Groundwork documents (those carrying
/// authored <see cref="WorkflowDefinitionState"/>). It applies the shared
/// <see cref="GroundworkDocumentSerialization"/> domain-projection model with the workflow-design specifics:
/// the authored <see cref="WorkflowDefinitionState"/> is delegated to <see cref="IPayloadSerializer"/>, and the
/// EF shadow <c>StateSource</c>, navigation (<c>Definition</c>/<c>WorkflowDefinition</c>) and relational
/// <c>RowNumber</c> members are excluded from the document.
/// </summary>
public static class GroundworkDesignDocumentSerialization
{
    private static readonly string[] ExcludedMembers =
    [
        nameof(Entity.RowNumber),
        "StateSource",
        "Definition",
        "WorkflowDefinition",
    ];

    private static readonly Type[] PayloadDelegatedTypes =
    [
        typeof(WorkflowDefinitionState),
    ];

    /// <summary>Creates options for the rich workflow-design entities, bound to the host's payload serializer.</summary>
    public static JsonSerializerOptions Create(IPayloadSerializer payloadSerializer) =>
        GroundworkDocumentSerialization.Create(payloadSerializer, ExcludedMembers, PayloadDelegatedTypes);
}
