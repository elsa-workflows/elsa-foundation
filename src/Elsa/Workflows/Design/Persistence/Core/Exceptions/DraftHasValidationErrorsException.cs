namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>
/// Thrown by <c>IPromoteDraftToVersionCommand</c> (Unit D's allocation) when called against a
/// <c>WorkflowDefinitionDraft</c> whose <c>WorkflowDefinitionDraftValidation</c> sibling holds a
/// non-empty error set. Per Unit C FR-024: validation errors are compile errors for a workflow
/// definition; promotion is a compile-success precondition. The gate enforces this at execution
/// time; bypassing it (e.g. inserting a Version row directly through
/// <c>IAddCommand&lt;WorkflowDefinitionVersion&gt;</c>) is forbidden by domain contract.
/// </summary>
public sealed class DraftHasValidationErrorsException(string draftId, int errorCount)
    : Exception($"Cannot promote draft '{draftId}' to a version: {errorCount} validation error(s) present.")
{
    public string DraftId { get; } = draftId;
    public int ErrorCount { get; } = errorCount;
}
