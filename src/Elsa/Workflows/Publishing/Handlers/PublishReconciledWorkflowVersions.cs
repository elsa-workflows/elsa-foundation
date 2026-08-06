using Elsa.Events.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Core;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace Elsa.Workflows.Publishing.Handlers;

/// <summary>
/// Publish-on-reconcile (spec 147, #1157): an independent subscriber on the Design-side
/// <see cref="OnWorkflowVersionsReconciled"/> completion event that publishes the latest reconciled
/// version of each definition whose source opted in (<c>PublishOnReconcile</c> →
/// <see cref="WorkflowVersionSourceClaim.PublishRequested"/>), via the in-process
/// <see cref="PublishWorkflow"/> request. The event is Sequential and runs inside the reconcile
/// startup task, so publications exist before shell activation completes — "ready" means executable.
/// </summary>
/// <remarks>
/// Idempotent across restarts: a definition whose publication slot already holds an <em>active</em>
/// publication of the target version is skipped before any compile; <c>PublishWorkflow</c>'s own
/// unchanged-artifact short-circuit (<c>WasCreated = false</c>) is the second net. Failure policy per
/// the event's contract: this handler never lets an exception escape — a per-definition failure is
/// logged as a structured error and the remaining definitions still publish; shell activation is
/// never failed by a publish failure (the reconcile pass itself already completed).
/// </remarks>
public sealed class PublishReconciledWorkflowVersions(
    ILogger<PublishReconciledWorkflowVersions> logger,
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionVersionStore versionStore,
    IPublicationSlotStore slotStore,
    IPublicationRecordStore recordStore,
    IRequestSender requestSender) : IEventHandler<OnWorkflowVersionsReconciled>
{
    public async Task Handle(OnWorkflowVersionsReconciled domainEvent, CancellationToken cancellationToken)
    {
        // Latest claim per definition, opted-in sources only. SemVer sort keys compare ordinally —
        // the same ordering the reconciler and the version store use.
        var latestClaims = domainEvent.Claims
            .Where(claim => claim.PublishRequested)
            .GroupBy(claim => claim.DefinitionId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(claim => claim.SemVerSortKey, StringComparer.Ordinal).First());

        foreach (var claim in latestClaims)
        {
            try
            {
                await PublishLatestVersion(claim, cancellationToken);
            }
            catch (Exception exception)
            {
                // Never escape: a throw here would fail the reconcile pass and shell activation
                // (Sequential delivery). Surface, isolate, continue with the next definition.
                LogPublishFailed(claim, exception);
            }
        }
    }

    private async Task PublishLatestVersion(WorkflowVersionSourceClaim claim, CancellationToken cancellationToken)
    {
        if (claim.Deleted)
        {
            LogSkipDeleted(claim);
            return;
        }

        var definition = await definitionStore.FindByIdAsync(claim.DefinitionId, cancellationToken);
        if (definition is null)
        {
            LogDefinitionMissing(claim);
            return;
        }

        if (definition.DeletedAt is not null)
        {
            LogSkipDeleted(claim);
            return;
        }

        // The claim's version, not the store's latest: a catalog-authored (Studio) version newer than
        // the source's latest contribution must not be hijacked into publication by a file deploy.
        var target = (await versionStore.ListByDefinitionAsync(claim.DefinitionId, cancellationToken))
            .FirstOrDefault(version => version.SemVerSortKey == claim.SemVerSortKey);
        if (target is null)
        {
            LogVersionRowMissing(claim);
            return;
        }

        if (await HasActivePublicationOf(claim.DefinitionId, target.Id, cancellationToken))
        {
            LogSkipAlreadyPublished(claim);
            return;
        }

        var published = await requestSender.Send(new PublishWorkflow(target.Id), cancellationToken);
        LogPublished(claim, published.ArtifactId, published.WasCreated);
    }

    private async Task<bool> HasActivePublicationOf(string definitionId, string versionId, CancellationToken cancellationToken)
    {
        var slots = await slotStore.ListByDefinitionAsync(definitionId, cancellationToken);

        foreach (var publicationId in slots.Where(x => x.ActivePublicationId is not null).Select(x => x.ActivePublicationId!))
        {
            var record = await recordStore.FindAsync(publicationId, cancellationToken);
            if (record is { Status: PublicationStatus.Active } && record.WorkflowDefinitionVersionId == versionId)
                return true;
        }

        return false;
    }

    private void LogSkipDeleted(WorkflowVersionSourceClaim claim)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(
                "Publish-on-reconcile: skipping workflow definition '{DefinitionId}' from source '{SourceId}' — the definition is deleted.",
                claim.DefinitionId, claim.SourceId);
    }

    private void LogDefinitionMissing(WorkflowVersionSourceClaim claim)
    {
        logger.LogWarning(
            "Publish-on-reconcile: workflow definition '{DefinitionId}' (claimed by source '{SourceId}') was not found in the design store after the reconcile pass. Nothing was published for it.",
            claim.DefinitionId, claim.SourceId);
    }

    private void LogVersionRowMissing(WorkflowVersionSourceClaim claim)
    {
        logger.LogWarning(
            "Publish-on-reconcile: workflow definition '{DefinitionId}' v{Version} (claimed by source '{SourceId}') has no matching version row after the reconcile pass. Nothing was published for it.",
            claim.DefinitionId, claim.Version, claim.SourceId);
    }

    private void LogSkipAlreadyPublished(WorkflowVersionSourceClaim claim)
    {
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug(
                "Publish-on-reconcile: workflow definition '{DefinitionId}' v{Version} already has an active publication of that version — skipped (idempotent restart).",
                claim.DefinitionId, claim.Version);
    }

    private void LogPublished(WorkflowVersionSourceClaim claim, string artifactId, bool wasCreated)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(
                "Publish-on-reconcile: workflow definition '{DefinitionId}' v{Version} from source '{SourceId}' is published as artifact '{ArtifactId}' ({Outcome}).",
                claim.DefinitionId, claim.Version, claim.SourceId, artifactId, wasCreated ? "created" : "replayed unchanged");
    }

    private void LogPublishFailed(WorkflowVersionSourceClaim claim, Exception exception)
    {
        logger.LogError(
            exception,
            "Publish-on-reconcile: publishing workflow definition '{DefinitionId}' v{Version} from source '{SourceId}' failed. The definition remains imported but not executable; remaining definitions continue.",
            claim.DefinitionId, claim.Version, claim.SourceId);
    }
}
