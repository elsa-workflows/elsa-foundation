namespace Elsa.Workflows.Publishing.Core.Models;

public enum PublicationAction
{
    Replace,
    PublishSideBySide
}

public enum PublicationPolicyDefaultAction
{
    ReplaceDefaultSlot,
    RequireExplicitSlot
}

public enum PublicationPolicySource
{
    Request,
    Workflow,
    Host
}

public enum PublicationStatus
{
    Candidate,
    PendingProjection,
    Active,
    Retired,
    Failed
}

public sealed record PublicationRequestIntent(PublicationAction Action, string? SlotName = null);

public sealed record PublicationPolicy(
    string? WorkflowDefinitionId,
    PublicationPolicyDefaultAction DefaultAction,
    string DefaultSlotName,
    long Revision,
    DateTimeOffset UpdatedAt);

public sealed record ResolvedPublicationAction(
    string WorkflowDefinitionId,
    string WorkflowDefinitionVersionId,
    PublicationAction Action,
    string SlotName,
    PublicationPolicySource PolicySource,
    long? PolicyRevision,
    string? ExpectedPublicationId = null);

public sealed class PublicationPolicyResolutionException(string code, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
}

public sealed record PublicationFailure(string Code, string Message);

