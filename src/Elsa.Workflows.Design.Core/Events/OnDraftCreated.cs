using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Lifecycle-origination event for a newly-created <c>WorkflowDefinitionDraft</c>. Published by
/// <c>ICreateDraftCommand</c> after the Draft is persisted but before the command returns. Sealed
/// class per framework §2.6.1.
/// </summary>
/// <remarks>
/// This is the single origination marker for a Draft's event stream regardless of how it was born.
/// <see cref="SourceVersionId"/> distinguishes the two origins: <c>null</c> for a fresh Draft (the
/// very first Draft of a definition, with no version to copy from); set to the source
/// <c>WorkflowDefinitionVersion</c> id when the Draft was cloned from an existing version
/// (<c>ICloneDraftFromVersionCommand</c> delegates to <c>ICreateDraftCommand</c>). A separate
/// <c>OnDraftClonedFromVersion</c> event is therefore unnecessary — the provenance lives here.
/// </remarks>
public sealed class OnDraftCreated(string draftId, string workflowDefinitionId, string? sourceVersionId = null) : IEvent
{
    public string DraftId { get; } = draftId;
    public string WorkflowDefinitionId { get; } = workflowDefinitionId;

    /// <summary>
    /// The <c>WorkflowDefinitionVersion</c> this Draft was cloned from, or <c>null</c> when the
    /// Draft was created fresh (no source version).
    /// </summary>
    public string? SourceVersionId { get; } = sourceVersionId;
}
