namespace Elsa.Agent.Specialists.Core.Models;

/// <summary>An explicit conversation and base revision; ownership is derived from verified native provenance.</summary>
public sealed record SpecialistConversationAdmission(string ConversationId, long ExpectedRevision);

/// <summary>
/// A conversation's owner in its ordinary tenant and persistence scope, bound to the exact specialist version and template.
/// <see cref="WorkflowExecutionId"/> is the owning native workflow, or, for the root of an activity draft Test Run, the
/// exact draft candidate's workspace, so successive playground runs of one saved revision can continue one conversation.
/// </summary>
public sealed record SpecialistConversationOwner(string? TenantId, string PersistencePartition, string WorkflowExecutionId,
    string DefinitionVersionId, string TemplateHash)
{
    /// <summary>Derives the owner from runtime-verified provenance, never business input.</summary>
    public static SpecialistConversationOwner FromInvocation(SpecialistInvocationIdentity identity) =>
        new(identity.TenantId, identity.PersistencePartition, identity.ConversationWorkspace ?? identity.WorkflowExecutionId,
            identity.DefinitionVersionId, identity.TemplateHash);
}

/// <summary>The immutable accepted operation used to restore history; it does not authorize an unrelated delivery.</summary>
public sealed record SpecialistAcceptedTurnReference(string Handle, SpecialistInvocationIdentity Identity, string ChildInvocationId);

/// <summary>Scoped revision admission and the last accepted history reference, committed atomically with turn state.</summary>
public sealed record SpecialistConversationDocument(string Id, SpecialistConversationOwner Owner, long Revision,
    string? ActiveTurnHandle, string? ActiveFence, bool OutcomeUnknown, SpecialistAcceptedTurnReference? AcceptedTurn)
{
    /// <summary>Current claim generation, zero only when inactive or in a legacy unleased record.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int ActiveAdmissionGeneration { get; init; }
}
