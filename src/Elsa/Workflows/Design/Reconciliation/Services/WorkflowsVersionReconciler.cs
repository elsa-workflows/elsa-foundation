using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Enums;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Core;
using Elsa.Workflows.Design.Reconciliation.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Reconciliation.Services;

/// <summary>
/// Workflow-side reconciler. Each pass publishes <see cref="OnWorkflowVersionsReconciling"/>
/// to gather candidate versions from source modules, then upserts the catalog. Mirrors the
/// Activities-side reconciler pattern but without the content-hash check — workflow-version
/// provenance fields (<c>SourceKind</c> / <c>SourceId</c> / hash) are Unit D's allocation per
/// FR-016a; until they land, duplicate detection falls back to the configured
/// <see cref="DuplicateHandling"/> mode.
/// </summary>
public sealed class WorkflowsVersionReconciler(
    ILogger<WorkflowsVersionReconciler> logger,
    IInlineEventPublisher eventPublisher,
    IOptions<WorkflowVersionReconcilerOptions> options,
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionVersionStore versionStore,
    IAddCommand<WorkflowDefinition> addDefinitionCommand,
    IAddCommand<WorkflowDefinitionVersion> addVersionCommand
)
    : IWorkflowVersionReconciler
{
    public async Task Reconcile(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var @event = new OnWorkflowVersionsReconciling();
        await eventPublisher.Publish(@event, cancellationToken);

        foreach (var version in @event.Versions)
        {
            await ReconcileVersion(version, cancellationToken);
        }
    }

    private async Task ReconcileVersion(IWorkflowDefinitionVersion version, CancellationToken cancellationToken)
    {
        var definitionId = version.DefinitionId;

        var definition = await FindDefinition(definitionId, cancellationToken);
        if (definition is null)
        {
            await addDefinitionCommand.Add(WorkflowDefinition.From(version.Definition), cancellationToken);
        }

        var candidateSortKey = SemVer.ToSortKey(version.Version);

        var latestVersion = await versionStore.FindLatestVersionAsync(definitionId, cancellationToken);
        if (latestVersion is not null && string.CompareOrdinal(candidateSortKey, latestVersion.SemVerSortKey) < 0)
        {
            LogSkipOutdated(definitionId, version.Version);
            return;
        }

        var versionExists = await VersionExists(definitionId, candidateSortKey, cancellationToken);
        if (!versionExists)
        {
            await addVersionCommand.Add(WorkflowDefinitionVersion.From(version), cancellationToken);
            return;
        }

        HandleDuplicate(definitionId, version.Version);
    }

    private void HandleDuplicate(string definitionId, string version)
    {
        switch (options.Value.DuplicateHandling)
        {
            case DuplicateHandling.Throw:
                throw new InvalidOperationException($"Workflow definition version '{definitionId}' v{version} already exists");

            case DuplicateHandling.Skip:
                LogSkipDuplicate(definitionId, version);
                break;

            default:
                break;
        }
    }

    private void LogSkipDuplicate(string definitionId, string version)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Skipping duplicate workflow definition '{def}' v{v}", definitionId, version);
    }

    private void LogSkipOutdated(string definitionId, string version)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Skipping outdated workflow definition '{def}' v{v}", definitionId, version);
    }

    private async Task<bool> VersionExists(string definitionId, string sortKey, CancellationToken cancellationToken)
    {
        return await versionStore.ExistsAsync(definitionId, sortKey, cancellationToken);
    }

    private async Task<WorkflowDefinition?> FindDefinition(string definitionId, CancellationToken cancellationToken)
    {
        return await definitionStore.FindByIdAsync(definitionId, cancellationToken);
    }
}
