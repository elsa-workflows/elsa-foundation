using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Reconciliation;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Reconciliation.Handlers;

/// <summary>
/// Gives a reconciled workflow definition a draft when it has none, cloned from the newest version the pass reconciled,
/// so Studio, which opens a definition through its draft, can edit it (#6070). A draft someone saved is kept, so a
/// restart never replaces work in progress; a draft nobody saved, cloned from an older version, is replaced by a clone
/// of the newer one, so the designer shows what the source changed. The draft lives in the catalog only: nothing is written back to the
/// source, and a version promoted from it is a catalog version the source never overwrites (ADR 0034 D1, D6). The
/// Elsa 3 import clones a draft the same way (spec 235 research R-6).
/// </summary>
/// <remarks>
/// <see cref="WorkflowVersionsReconciled"/> is Sequential and runs inside the reconcile startup task, so the handler
/// never lets an exception escape: a failed clone is logged, the definition still runs, and the next start tries again.
/// The task runs on every node without a lock; the clone's operation key is the same on each node
/// (<see cref="WorkflowReconciliationOperationKeys.DraftClone"/>), so they converge on one draft.
/// </remarks>
public sealed class CloneDraftsOfReconciledWorkflows(
    IWorkflowDefinitionDraftStore draftStore,
    IWorkflowDefinitionVersionStore versionStore,
    ICloneDraftFromVersionCommand cloneDraft,
    IDiscardDraftCommand discardDraft,
    ILogger<CloneDraftsOfReconciledWorkflows> logger) : IEventHandler<WorkflowVersionsReconciled>
{
    public async Task Handle(WorkflowVersionsReconciled domainEvent, CancellationToken cancellationToken)
    {
        // Newest reconciled claim per definition. SemVer sort keys compare ordinally, as in the reconciler. A deleted
        // newest claim deletes the definition (latest wins), so it gets no draft from an older version either.
        var newestClaims = domainEvent.Claims
            .GroupBy(claim => claim.DefinitionId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(claim => claim.SemVerSortKey, StringComparer.Ordinal).First())
            .Where(claim => !claim.Deleted);

        foreach (var claim in newestClaims)
        {
            try
            {
                await EnsureDraft(claim, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Cloning a draft of reconciled workflow definition '{DefinitionId}' v{Version} failed; it runs, but does not open in the designer until a later start clones one.",
                    claim.DefinitionId, claim.Version);
            }
        }
    }

    private async Task EnsureDraft(WorkflowVersionSourceClaim claim, CancellationToken cancellationToken)
    {
        var existing = await draftStore.FindByWorkflowDefinitionIdAsync(claim.DefinitionId, cancellationToken);
        if (existing is not null && !IsUnsaved(existing))
            return;

        var version = (await versionStore.ListByDefinitionAsync(claim.DefinitionId, cancellationToken))
            .FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.SemVerSortKey, claim.SemVerSortKey));
        if (version is null)
        {
            logger.LogWarning(
                "Reconciled workflow definition '{DefinitionId}' v{Version} has no matching version row; no draft was cloned.",
                claim.DefinitionId, claim.Version);
            return;
        }

        if (existing is not null)
        {
            if (existing.SourceVersionId is null || StringComparer.Ordinal.Equals(existing.SourceVersionId, version.Id))
                return;

            // The draft was cloned from an older version and never saved, so it holds nothing but that version: the
            // source changed the workflow since (a newer version in the folder), and the designer must show the new
            // one. A claimed version is never older than the catalog's latest; the reconciler drops those claims.
            await discardDraft.Execute(
                WorkflowReconciliationOperationKeys.DraftDiscard(claim.DefinitionId, claim.SemVerSortKey),
                existing.Id,
                cancellationToken);
            logger.LogInformation(
                "Reconciled workflow definition '{DefinitionId}' has a newer version v{Version}; its unsaved draft '{DraftId}' was discarded.",
                claim.DefinitionId, claim.Version, existing.Id);
        }

        var draftId = await cloneDraft.Execute(
            WorkflowReconciliationOperationKeys.DraftClone(claim.DefinitionId, claim.SemVerSortKey),
            version.Id,
            cancellationToken);

        if (!logger.IsEnabled(LogLevel.Information))
            return;

        // The key replays an earlier clone of this version, so a draft discarded since is not cloned again.
        if (await draftStore.FindByIdAsync(draftId, cancellationToken) is null)
            logger.LogInformation(
                "Reconciled workflow definition '{DefinitionId}' has no draft: the draft cloned from v{Version} was discarded. A newer version from the source gets a new draft.",
                claim.DefinitionId, claim.Version);
        else
            logger.LogInformation(
                "Reconciled workflow definition '{DefinitionId}' had no draft; draft '{DraftId}' was cloned from v{Version}.",
                claim.DefinitionId, draftId, claim.Version);
    }

    // A draft's working copy starts at revision 0 and every save increments it, so 0 means nobody saved the draft.
    private static bool IsUnsaved(WorkflowDefinitionDraft draft) => draft.Source is { Revision: 0 };
}
