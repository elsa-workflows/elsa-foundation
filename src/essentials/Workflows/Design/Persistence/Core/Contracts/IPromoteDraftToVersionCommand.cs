using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;

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
/// compile-success precondition. Bypassing the gate (e.g. inserting a Version row directly) is
/// forbidden by domain contract.
/// </para>
/// <para>
/// <b>Lock contract (Unit C FR-027b).</b> Promotion MUST acquire the same
/// <c>workflow-draft:{DraftId}</c> distributed lock as mutation commands. Mutations arriving
/// after a promotion completes are not part of the promoted Version — they exist on the Draft
/// only and require a new (higher) Version to be included.
/// </para>
/// <para>
/// <b>Content precondition (spec 188, research R7).</b> The caller passes the
/// <see cref="WorkflowDraftStateHash"/> of the draft it read and checked. Inside the promotion lock, before anything is
/// written, the implementation MUST compare it with the hash of the draft it reads there and throw
/// <see cref="WorkflowDraftChangedException"/> when they differ, so a promotion writes exactly the content its caller
/// saw. The hash is required, and it is not part of the request an operation key identifies: a replay of an
/// already-succeeded promotion returns the original version id and writes nothing, whatever hash it carries.
/// </para>
/// </remarks>
public interface IPromoteDraftToVersionCommand
{
    /// <summary>
    /// Promotes the supplied Draft to a new Version and returns the new Version's id. An omitted
    /// <paramref name="requestedVersion"/> retains the automatic next-major policy; a supplied request is checked for
    /// semantic validity, forward precedence, and identity availability inside the promotion lock. Throws
    /// <see cref="ArgumentException"/> for a null or blank <paramref name="expectedStateHash"/> before reading or
    /// writing anything, <see cref="WorkflowDraftChangedException"/> when the draft no longer has that content, and
    /// <see cref="DraftHasValidationErrorsException"/> when the Draft has unresolved validation errors.
    /// </summary>
    Task<string> Execute(
        DesignOperationKey operationKey,
        string draftId,
        string? requestedVersion,
        string expectedStateHash,
        CancellationToken cancellationToken = default);
}
