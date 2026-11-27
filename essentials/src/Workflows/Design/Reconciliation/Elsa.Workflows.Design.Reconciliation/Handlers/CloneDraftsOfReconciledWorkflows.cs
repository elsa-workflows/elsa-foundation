using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Reconciliation;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Reconciliation.Handlers;

/// <summary>
/// Gives a reconciled workflow definition a draft when it has none, cloned from the newest version the pass reconciled,
/// so Studio, which opens a definition through its draft, can edit it (#6070). A definition that has a draft keeps it,
/// so a restart never replaces work in progress. The draft lives in the catalog only: nothing is written back to the
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
    ILogger<CloneDraftsOfReconciledWorkflows> logger) : IEventHandler<WorkflowVersionsReconciled>
{
    public async Task Handle(WorkflowVersionsReconciled domainEvent, CancellationToken cancellationToken)
    {
        // Newest reconciled claim per definition. SemVer sort keys compare ordinally, as in the reconciler.
        var newestClaims = domainEvent.Claims
            .Where(claim => !claim.Deleted)
            .GroupBy(claim => claim.DefinitionId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(claim => claim.SemVerSortKey, StringComparer.Ordinal).First());

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
        if (await draftStore.FindByWorkflowDefinitionIdAsync(claim.DefinitionId, cancellationToken) is not null)
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
}
