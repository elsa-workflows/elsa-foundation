using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Models;
using Microsoft.Extensions.Logging;

namespace Elsa3.Activities.Design.Import.Reconciliation.Services;

/// <summary>What happened to one imported workflow definition's newest version.</summary>
internal enum ImportedWorkflowPublication
{
    Published,
    AlreadyPublishedOnce,
    Failed
}

/// <summary>
/// Publishes the newest imported version of each workflow definition at most once (spec 235 research R-6). A version
/// its target slot has published before, in any status but <see cref="PublicationStatus.Failed"/>, is left alone, so a
/// start never takes back a slot an operator moved, and a re-exported newer version is published.
/// </summary>
internal sealed class ImportedWorkflowPublisher(
    IPublicationPolicyStore policyStore,
    IPublicationPolicyResolver policyResolver,
    IWorkflowActivationAuthority activationAuthority,
    IPublicationRecordStore recordStore,
    IRequestSender requestSender,
    TimeProvider timeProvider,
    ILogger<ImportedWorkflowPublisher> logger)
{
    public async Task<IReadOnlyDictionary<string, ImportedWorkflowPublication>> PublishAsync(
        IEnumerable<ReusableActivityImportedWorkflowVersion> versions,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, ImportedWorkflowPublication>(StringComparer.Ordinal);
        foreach (var newest in versions
                     .GroupBy(version => version.WorkflowDefinitionId, StringComparer.Ordinal)
                     .Select(group => group.OrderByDescending(version => SemVer(version.Version)).First()))
            results[newest.SourceVersionId] = await PublishOnceAsync(newest, tenantId, cancellationToken);
        return results;
    }

    private async Task<ImportedWorkflowPublication> PublishOnceAsync(
        ReusableActivityImportedWorkflowVersion version,
        string tenantId,
        CancellationToken cancellationToken)
    {
        try
        {
            var slot = await FindTargetSlotAsync(version.WorkflowDefinitionId, version.WorkflowVersionId, cancellationToken);
            if (await WasPublishedAsync(slot, version.WorkflowVersionId, cancellationToken))
                return ImportedWorkflowPublication.AlreadyPublishedOnce;

            await requestSender.Send(
                new PublishWorkflow(version.WorkflowVersionId, ExpectedPublicationId: slot?.ActiveActivationId, TenantId: tenantId),
                cancellationToken);
            return ImportedWorkflowPublication.Published;
        }
        catch (Exception exception) when (IsConcurrentPublication(exception))
        {
            // Another node may have published the same version between the check and the request.
            var slot = await FindTargetSlotAsync(version.WorkflowDefinitionId, version.WorkflowVersionId, cancellationToken);
            if (await WasPublishedAsync(slot, version.WorkflowVersionId, cancellationToken))
            {
                logger.LogDebug("Imported workflow version {VersionId} was published by another node.", version.WorkflowVersionId);
                return ImportedWorkflowPublication.AlreadyPublishedOnce;
            }

            LogFailed(version, exception);
            return ImportedWorkflowPublication.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailed(version, exception);
            return ImportedWorkflowPublication.Failed;
        }
    }

    private async Task<bool> WasPublishedAsync(WorkflowActivationSlot? slot, string versionId, CancellationToken cancellationToken)
    {
        if (slot is null)
            return false;
        var records = await recordStore.ListBySlotAsync(slot.SlotId, cancellationToken);
        return records.Any(record =>
            StringComparer.Ordinal.Equals(record.WorkflowDefinitionVersionId, versionId) &&
            record.Status != PublicationStatus.Failed);
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

    private static bool IsConcurrentPublication(Exception exception) => exception switch
    {
        PublicationPolicyResolutionException { Code: PublicationFailureCodes.ExpectedPublicationMismatch } => true,
        PublicationActivationException { Code: PublicationFailureCodes.SlotRevisionConflict or PublicationFailureCodes.ArtifactAlreadyServing } => true,
        _ => false
    };

    private static (int, int, int, string) SemVer(string version)
    {
        var core = version.Split('-', '+')[0].Split('.');
        static int Part(string[] parts, int index) => parts.Length > index && int.TryParse(parts[index], out var value) ? value : 0;
        return (Part(core, 0), Part(core, 1), Part(core, 2), version);
    }

    private void LogFailed(ReusableActivityImportedWorkflowVersion version, Exception exception) =>
        logger.LogWarning(exception,
            "Publishing imported workflow '{DefinitionId}' version {Version} failed: {Message}",
            version.WorkflowDefinitionId, version.Version, exception.Message);
}
