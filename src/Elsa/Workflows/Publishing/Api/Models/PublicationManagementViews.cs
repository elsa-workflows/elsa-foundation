using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Models;

public sealed record PublicationView(
    string PublicationId,
    string DefinitionId,
    string VersionId,
    string ArtifactId,
    string SlotName,
    string? SourceReferenceId,
    PublicationStatusView Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? RetiredAt,
    PublicationFailure? Failure)
{
    public static PublicationView From(PublicationRecord publication) =>
        new(
            publication.PublicationId,
            publication.WorkflowDefinitionId,
            publication.WorkflowDefinitionVersionId,
            publication.ArtifactId,
            publication.SlotName,
            publication.SourceReferenceId,
            PublicationContract.ToView(publication.Status),
            publication.CreatedAt,
            publication.ActivatedAt,
            publication.RetiredAt,
            publication.Failure);
}

public sealed record PublicationSlotView(
    string SlotId,
    string DefinitionId,
    string SlotName,
    string? ActivePublicationId,
    long Revision,
    DateTimeOffset UpdatedAt,
    PublicationStatusView? Status,
    PublicationView? Publication)
{
    public static PublicationSlotView From(PublicationSlot slot, PublicationRecord? publication) =>
        new(
            slot.SlotId,
            slot.WorkflowDefinitionId,
            slot.SlotName,
            slot.ActivePublicationId,
            slot.Revision,
            slot.UpdatedAt,
            publication is null ? null : PublicationContract.ToView(publication.Status),
            publication is null ? null : PublicationView.From(publication));
}

public sealed record PublicationSlotListResponse(IReadOnlyCollection<PublicationSlotView> Items);

public sealed record PublicationPolicyView(
    string DefinitionId,
    PublicationPolicyDefaultActionView DefaultAction,
    string DefaultSlotName,
    PublicationPolicySourceView Source,
    long Revision,
    DateTimeOffset UpdatedAt)
{
    public static PublicationPolicyView From(string workflowDefinitionId, PublicationPolicy policy, PublicationPolicySource source) =>
        new(workflowDefinitionId, PublicationPolicyContract.ToView(policy.DefaultAction), policy.DefaultSlotName, PublicationContract.ToView(source), policy.Revision, policy.UpdatedAt);
}

public enum PublicationPolicyDefaultActionView
{
    Replace,
    RequireExplicitSlot
}

public static class PublicationPolicyContract
{
    public static PublicationPolicyDefaultActionView ToView(PublicationPolicyDefaultAction action) => action switch
    {
        PublicationPolicyDefaultAction.ReplaceDefaultSlot => PublicationPolicyDefaultActionView.Replace,
        PublicationPolicyDefaultAction.RequireExplicitSlot => PublicationPolicyDefaultActionView.RequireExplicitSlot,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported publication policy action.")
    };

    public static PublicationPolicyDefaultAction ToModel(PublicationPolicyDefaultActionView action) => action switch
    {
        PublicationPolicyDefaultActionView.Replace => PublicationPolicyDefaultAction.ReplaceDefaultSlot,
        PublicationPolicyDefaultActionView.RequireExplicitSlot => PublicationPolicyDefaultAction.RequireExplicitSlot,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported publication policy action.")
    };
}

public enum PublicationActionView
{
    Replace,
    SideBySide
}

public enum PublicationStatusView
{
    Preparing,
    Pending,
    Active,
    Retiring,
    Retired,
    Failed
}

public enum PublicationPolicySourceView
{
    Request,
    Workflow,
    Host
}

public static class PublicationContract
{
    public static PublicationStatusView ToView(PublicationStatus status) => status switch
    {
        PublicationStatus.Candidate => PublicationStatusView.Preparing,
        PublicationStatus.PendingProjection => PublicationStatusView.Pending,
        PublicationStatus.Active => PublicationStatusView.Active,
        PublicationStatus.Retired => PublicationStatusView.Retired,
        PublicationStatus.Failed => PublicationStatusView.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported publication status.")
    };

    public static PublicationActionView ToView(PublicationAction action) => action switch
    {
        PublicationAction.Replace => PublicationActionView.Replace,
        PublicationAction.PublishSideBySide => PublicationActionView.SideBySide,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported publication action.")
    };

    public static PublicationPolicySourceView ToView(PublicationPolicySource source) => source switch
    {
        PublicationPolicySource.Request => PublicationPolicySourceView.Request,
        PublicationPolicySource.Workflow => PublicationPolicySourceView.Workflow,
        PublicationPolicySource.Host => PublicationPolicySourceView.Host,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unsupported publication policy source.")
    };
}

public static class PublicationIntentContract
{
    public static PublicationAction ToModel(PublicationActionView action) => action switch
    {
        PublicationActionView.Replace => PublicationAction.Replace,
        PublicationActionView.SideBySide => PublicationAction.PublishSideBySide,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported publication action.")
    };
}

public sealed record PublicationPreflightView(
    string DefinitionId,
    string VersionId,
    string SlotName,
    PublicationActionView ResolvedAction,
    PublicationPolicySourceView PolicySource,
    long? PolicyRevision,
    bool CanActivate,
    IReadOnlyCollection<PublicationTriggerChange> Changes,
    IReadOnlyCollection<PublicationTriggerConflict> Conflicts);
