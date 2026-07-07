using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Enums;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
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
    IAddCommand<WorkflowDefinitionVersion> addVersionCommand,
    ISaveWorkflowDefinitionCommand saveDefinitionCommand
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
        else
        {
            await UpdateDefinitionMetadata(definition, version.Definition, cancellationToken);
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

    /// <summary>
    /// Applies the incoming source model's mutable definition-level metadata (name, description) to an
    /// already-persisted definition. Idempotent — writes only when a value actually changed — and never
    /// touches any <see cref="WorkflowDefinitionVersion"/>: versions are immutable and
    /// retention-authoritative, whereas name/description are latest-wins per ADR 0034 (D5). Runs for
    /// every <see cref="Contracts.IWorkflowReconciliationSource"/>, not only git.
    ///
    /// This is the seam <c>specs/085</c> extends: it applies whatever metadata the incoming model
    /// carries, so soft-delete (<c>deleted</c>) propagation can be added by widening the diff here once
    /// the reconciliation model grows a delete flag — no second refactor of the reconciler required.
    /// </summary>
    private async Task UpdateDefinitionMetadata(WorkflowDefinition persisted, IWorkflowDefinition incoming, CancellationToken cancellationToken)
    {
        if (persisted.Name == incoming.Name && persisted.Description == incoming.Description)
            return;

        persisted.Name = incoming.Name;
        persisted.Description = incoming.Description;
        await saveDefinitionCommand.Execute(persisted, cancellationToken);
        LogMetadataUpdated(persisted.Id);
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

    private void LogMetadataUpdated(string definitionId)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Updated metadata for workflow definition '{def}'", definitionId);
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
