namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>
/// Thrown by <c>IPromoteDraftToVersionCommand</c> when the draft it reads under the promotion lock is not the content
/// the caller read: its <c>WorkflowDraftStateHash</c> differs from the expected one, because the draft changed in
/// between. Nothing is written. The caller reads the draft again and retries (a conflict, 409).
/// </summary>
/// <remarks>
/// This is a storage-integrity compare-and-set on the row's content, not a rule: it knows nothing about what the
/// caller checked, only that the content changed.
/// </remarks>
public sealed class WorkflowDraftChangedException(string draftId)
    : InvalidOperationException($"Workflow draft '{draftId}' changed after it was read for promotion. Read the draft again and retry.")
{
    public string DraftId { get; } = draftId;
}
