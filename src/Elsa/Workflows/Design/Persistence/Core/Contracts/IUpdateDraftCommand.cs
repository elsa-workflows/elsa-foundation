using Elsa.Persistence.Core.Design;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// The single coarse Draft-mutation command (Unit 2). Supersedes Unit C's 20 granular
/// mutation commands: the caller submits the complete desired Draft state in
/// <see cref="UpdateDraftRequest"/>; the command — under the per-Draft
/// <c>workflow-draft:{DraftId}</c> distributed lock — loads stored state, diffs the desired
/// state against it, emits one per-concept mutation event per detected difference, runs the
/// validation gate, and persists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Command, not query (G18).</b> Returns <see cref="Task"/> — no queryable result. Mutation
/// effects are observed through the persisted Draft and the published events.
/// </para>
/// <para>
/// <b>Last-writer-wins (FR-022).</b> No optimistic-concurrency / version check. A desired
/// state built on a stale read overwrites concurrent edits wholesale; the diff emits the
/// resulting events. By design.
/// </para>
/// <para>
/// The 4 lifecycle commands (<see cref="ICreateDraftCommand"/>,
/// <see cref="ICloneDraftFromVersionCommand"/>, <see cref="IDiscardDraftCommand"/>,
/// <see cref="IPromoteDraftToVersionCommand"/>) are NOT mutations and remain distinct (FR-003).
/// </para>
/// </remarks>
public interface IUpdateDraftCommand
{
    Task Execute(
        DesignOperationKey operationKey,
        UpdateDraftRequest request,
        CancellationToken cancellationToken = default);
}
