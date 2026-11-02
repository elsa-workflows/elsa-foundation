using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Models;
using Microsoft.Extensions.Logging;

namespace Elsa3.Activities.Design.Import.Reconciliation.Services;

/// <summary>What happened to one imported workflow definition's newest version.</summary>
internal enum WorkflowPublicationStatus
{
    /// <summary>This start published it.</summary>
    Published,

    /// <summary>Its target slot published it before (this start, another node, or an earlier start); left alone.</summary>
    AlreadyPublished,

    /// <summary>Publishing it failed; the failure is logged and the next start tries again.</summary>
    Failed
}

/// <summary>
/// Publishes the newest imported version of each workflow definition at most once (spec 235 research R-6). A version
/// its target slot has published before (an <see cref="PublicationStatus.Active"/> or <see cref="PublicationStatus.Retired"/>
/// record, or the record the slot serves) is left alone, so a start never takes back a slot an operator moved, and a
/// re-exported newer version is published. A published definition without a draft gets one cloned from that version,
/// so it opens in the designer; one that has a draft keeps it.
/// </summary>
internal sealed class ImportedWorkflowPublisher(
    IPublicationPolicyStore policyStore,
    IPublicationPolicyResolver policyResolver,
    IWorkflowActivationAuthority activationAuthority,
    IPublicationRecordStore recordStore,
    IRequestSender requestSender,
    IWorkflowDefinitionDraftStore draftStore,
    ICloneDraftFromVersionCommand cloneDraft,
    TimeProvider timeProvider,
    ILogger<ImportedWorkflowPublisher> logger)
{
    public async Task<IReadOnlyDictionary<string, WorkflowPublicationStatus>> PublishAsync(
        IEnumerable<ReusableActivityImportedWorkflowVersion> versions,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, WorkflowPublicationStatus>(StringComparer.Ordinal);
        foreach (var newest in versions
                     .GroupBy(version => version.WorkflowDefinitionId, StringComparer.Ordinal)
                     .Select(group => group.OrderByDescending(version => SemVer(version.Version)).First()))
        {
            var status = await PublishOnceAsync(newest, tenantId, cancellationToken);
            if (status != WorkflowPublicationStatus.Failed)
                await EnsureDraftAsync(newest, cancellationToken);
            results[newest.SourceVersionId] = status;
        }
        return results;
    }

    private async Task EnsureDraftAsync(ReusableActivityImportedWorkflowVersion version, CancellationToken cancellationToken)
    {
        try
        {
            if (await draftStore.FindByWorkflowDefinitionIdAsync(version.WorkflowDefinitionId, cancellationToken) is not null)
                return;
            await cloneDraft.Execute(new DesignOperationKey($"elsa3-import-draft:{version.WorkflowVersionId}"), version.WorkflowVersionId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Creating a draft of imported workflow '{DefinitionId}' version {Version} failed; it runs, but does not open in the designer: {Message}",
                version.WorkflowDefinitionId, version.Version, exception.Message);
        }
    }

    private async Task<WorkflowPublicationStatus> PublishOnceAsync(
        ReusableActivityImportedWorkflowVersion version,
        string tenantId,
        CancellationToken cancellationToken)
    {
        try
        {
            var slot = await FindTargetSlotAsync(version.WorkflowDefinitionId, version.WorkflowVersionId, cancellationToken);
            if (await WasPublishedAsync(slot, version.WorkflowVersionId, cancellationToken))
                return WorkflowPublicationStatus.AlreadyPublished;

            var published = await requestSender.Send(
                new PublishWorkflow(version.WorkflowVersionId, ExpectedPublicationId: slot?.ActiveActivationId, TenantId: tenantId),
                cancellationToken);
            // When the slot already serves this artifact (another node got there first), PublishWorkflow answers with the
            // existing publication instead of refusing.
            return published.WasCreated ? WorkflowPublicationStatus.Published : WorkflowPublicationStatus.AlreadyPublished;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Another node may have published the same version between the check and the request; its refusal can take
            // several forms (an expected-publication mismatch, a slot revision conflict, an artifact the slot already
            // serves), so the journal decides, not the exception type.
            if (!await PublishedByAnotherNodeAsync(version, cancellationToken))
                return Failed(version, exception);
            logger.LogDebug(exception, "Publishing imported workflow version {VersionId} failed, but its slot's journal now holds it.", version.WorkflowVersionId);
            return WorkflowPublicationStatus.AlreadyPublished;
        }
    }

    private async Task<bool> PublishedByAnotherNodeAsync(ReusableActivityImportedWorkflowVersion version, CancellationToken cancellationToken)
    {
        try
        {
            var slot = await FindTargetSlotAsync(version.WorkflowDefinitionId, version.WorkflowVersionId, cancellationToken);
            if (!await WasPublishedAsync(slot, version.WorkflowVersionId, cancellationToken))
                return false;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Re-reading the publication journal of {VersionId} failed.", version.WorkflowVersionId);
            return false;
        }
    }

    private async Task<bool> WasPublishedAsync(WorkflowActivationSlot? slot, string versionId, CancellationToken cancellationToken)
    {
        if (slot is null)
            return false;
        // A candidate the slot never switched to (a process stopped between saving it and the runtime's commit) was
        // never published, and nothing completes it later: the startup completion pass only visits the publication the
        // slot names. A candidate the slot does serve is published; that pass completes its journal.
        var records = await recordStore.ListBySlotAsync(slot.SlotId, cancellationToken);
        return records.Any(record =>
            StringComparer.Ordinal.Equals(record.WorkflowDefinitionVersionId, versionId) &&
            (record.Status is PublicationStatus.Active or PublicationStatus.Retired ||
             record.Status != PublicationStatus.Failed && StringComparer.Ordinal.Equals(record.PublicationId, slot.ActiveActivationId)));
    }

    // Slot resolution copied from PublishReconciledWorkflowVersions (Elsa.Workflows.Publishing, ResolveTargetSlotName
    // and the slot lookup of HasActivePublicationOf), where it is private; it mirrors what PublishWorkflowRequestHandler
    // does for a request with neither an action nor a slot name: workflow policy first, host policy second, and the
    // synthesized host default when none is stored.
    private async Task<WorkflowActivationSlot?> FindTargetSlotAsync(string definitionId, string versionId, CancellationToken cancellationToken)
    {
        var workflowPolicy = await policyStore.FindAsync(definitionId, cancellationToken);
        var hostPolicy = await policyStore.FindAsync(workflowDefinitionId: null, cancellationToken)
            ?? new PublicationPolicy(null, PublicationPolicyDefaultAction.ReplaceDefaultSlot, "default", 0, timeProvider.GetUtcNow());

        string slotName;
        try
        {
            slotName = policyResolver.Resolve(definitionId, versionId, null, workflowPolicy, hostPolicy).SlotName;
        }
        catch (PublicationPolicyResolutionException)
        {
            // Unresolvable policy: let PublishWorkflow report its authoritative error.
            return null;
        }

        return await activationAuthority.FindAsync(definitionId, slotName, cancellationToken);
    }

    private static (int, int, int, string) SemVer(string version)
    {
        var core = version.Split('-', '+')[0].Split('.');
        static int Part(string[] parts, int index) => parts.Length > index && int.TryParse(parts[index], out var value) ? value : 0;
        return (Part(core, 0), Part(core, 1), Part(core, 2), version);
    }

    private WorkflowPublicationStatus Failed(ReusableActivityImportedWorkflowVersion version, Exception exception)
    {
        logger.LogWarning(exception,
            "Publishing imported workflow '{DefinitionId}' version {Version} failed: {Message}",
            version.WorkflowDefinitionId, version.Version, exception.Message);
        return WorkflowPublicationStatus.Failed;
    }
}
