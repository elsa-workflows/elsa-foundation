using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>
/// Thrown by <c>IPromoteDraftToVersionCommand</c> (Unit D's allocation) when called against a
/// <c>WorkflowDefinitionDraft</c> for which the in-lock validation gate re-derives a non-empty
/// error set. Per Unit C FR-024: validation errors are compile errors for a workflow definition;
/// promotion is a compile-success precondition. Errors are derived state (not persisted) — the
/// promotion gate re-runs the validators at promotion time. The gate enforces this at execution
/// time; bypassing it (e.g. inserting a Version row directly) is forbidden by domain contract.
/// </summary>
/// <remarks>
/// The exception carries the full derived <see cref="Errors"/> list (not just the count) so the API
/// surface can enrich the 409 ProblemDetails with the actual violations — clients no longer have to
/// make a second call to discover what is wrong. <see cref="ErrorCount"/> and the message text are
/// retained unchanged for backward compatibility.
/// </remarks>
public sealed class DraftHasValidationErrorsException(string draftId, IReadOnlyList<ValidationError> errors)
    : Exception($"Cannot promote draft '{draftId}' to a version: {errors.Count} validation error(s) present.")
{
    public string DraftId { get; } = draftId;
    public int ErrorCount { get; } = errors.Count;

    /// <summary>The full set of validation errors that blocked promotion. Derived state, never persisted.</summary>
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}
