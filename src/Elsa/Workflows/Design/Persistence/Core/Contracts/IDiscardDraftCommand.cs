using Elsa.Persistence.Core.Design;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Atomically deletes a <c>WorkflowDefinitionDraft</c> and cascades its layout sibling
/// (<c>WorkflowDefinitionDraftLayout</c>). Per Unit C FR-029. Validation errors are derived state
/// (not persisted), so there is no validation sibling to cascade.
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomicity.</b> The Draft + its layout sibling are removed in a single transaction via the
/// EF cascade rules configured on the relationship (research item R5). No
/// <c>WorkflowDefinitionVersion</c> is touched — versions are never deleted by domain contract.
/// </para>
/// <para>
/// <b>Lock contract.</b> Acquires the per-Draft distributed lock
/// (<c>workflow-draft:{DraftId}</c>) for the full deletion pipeline so the user cannot
/// accidentally Discard a Draft mid-mutation.
/// </para>
/// <para>
/// <b>Idempotency.</b> A second Discard on the same DraftId is a no-op — the lock acquires,
/// the load returns <c>null</c>, the command exits cleanly without re-publishing
/// <c>OnDraftDiscarded</c>.
/// </para>
/// <para>
/// <b>Parent Definition id.</b> Read directly off
/// <c>WorkflowDefinitionDraft.WorkflowDefinitionId</c> after loading; the impl supplies it to
/// <c>OnDraftDiscarded</c>. The caller does not pass it — the FK on the Draft is the single
/// source of truth.
/// </para>
/// </remarks>
public interface IDiscardDraftCommand
{
    Task Execute(
        DesignOperationKey operationKey,
        string draftId,
        CancellationToken cancellationToken = default);
}
