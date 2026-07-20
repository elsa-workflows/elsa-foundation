using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Placeholder contract — implementation is Unit D's allocation. Promotes a
/// <c>WorkflowDefinitionDraft</c> to a new <c>WorkflowDefinitionVersion</c>. Unit C declares
/// the marker and the gate contract; Unit D names the command finally (cardinality semantics
/// may shift the verb) and ships the implementation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Promotion gate (Unit C FR-024).</b> The implementation MUST throw
/// <see cref="DraftHasValidationErrorsException"/> when re-running the validators against the
/// Draft in-lock yields a non-empty error set. Validation errors are derived state (not
/// persisted): the gate re-runs the validators at promotion time rather than reading a persisted
/// row. Validation errors are compile errors for a workflow definition; promotion is a
/// compile-success precondition. Bypassing the gate (e.g. inserting a Version row directly via
/// <c>IAddCommand&lt;WorkflowDefinitionVersion&gt;</c>) is forbidden by domain contract.
/// </para>
/// <para>
/// <b>Lock contract (Unit C FR-027b).</b> Promotion MUST acquire the same
/// <c>workflow-draft:{DraftId}</c> distributed lock as mutation commands. Mutations arriving
/// after a promotion completes are not part of the promoted Version — they exist on the Draft
/// only and require a new (higher) Version to be included.
/// </para>
/// </remarks>
public interface IPromoteDraftToVersionCommand
{
    /// <summary>
    /// Promotes the supplied Draft to a new Version. Returns the new Version's id. Throws
    /// <see cref="DraftHasValidationErrorsException"/> when the Draft has unresolved
    /// validation errors.
    /// </summary>
    Task<string> Execute(
        DesignOperationKey operationKey,
        string draftId,
        CancellationToken cancellationToken = default);
}
