using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Enums;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Extensions;
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
    IDomainEventSender sender,
    IOptions<WorkflowVersionReconcilerOptions> options,
    IIdentityGenerator identityGenerator,
    IQueries<WorkflowDefinition> definitionQueries,
    IQueries<WorkflowDefinitionVersion> versionQueries,
    IAddCommand<WorkflowDefinition> addDefinitionCommand,
    IAddCommand<WorkflowDefinitionVersion> addVersionCommand
)
    : IWorkflowVersionReconciler
{
    public async Task Reconcile(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var @event = new OnWorkflowVersionsReconciling();
        await sender.Send(@event, cancellationToken);

        foreach (var version in @event.Versions)
        {
            await ReconcileVersion(version, cancellationToken);
        }
    }

    private async Task ReconcileVersion(IWorkflowDefinitionVersion version, CancellationToken cancellationToken)
    {
        var mappedVersion = Map(version);
        var mappedDefinition = Map(version.Definition);

        var definition = await FindDefinition(mappedDefinition.Id, cancellationToken);
        if (definition is null)
        {
            await addDefinitionCommand.Add(mappedDefinition, cancellationToken);
        }

        var latestVersion = await versionQueries.FindLastVersion(mappedDefinition.Id, cancellationToken);
        if (mappedVersion.Version < latestVersion?.Version)
        {
            LogSkipOutdated(mappedDefinition.Id, mappedVersion.Version);
            return;
        }

        var versionExists = await VersionExists(mappedDefinition.Id, version.Version, cancellationToken);
        if (!versionExists)
        {
            await addVersionCommand.Add(mappedVersion, cancellationToken);
            return;
        }

        HandleDuplicate(mappedDefinition, mappedVersion);
    }

    private void HandleDuplicate(WorkflowDefinition def, WorkflowDefinitionVersion version)
    {
        switch (options.Value.DuplicateHandling)
        {
            case DuplicateHandling.Throw:
                throw new InvalidOperationException($"Workflow definition version '{version.DefinitionId}' v{version.Version} already exists");

            case DuplicateHandling.Skip:
                LogSkipDuplicate(def.Id, version.Version);
                break;

            default:
                break;
        }
    }

    private void LogSkipDuplicate(string definitionId, int version)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Skipping duplicate workflow definition '{def}' v{v}", definitionId, version);
    }

    private void LogSkipOutdated(string definitionId, int version)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Skipping outdated workflow definition '{def}' v{v}", definitionId, version);
    }

    private async Task<bool> VersionExists(string definitionId, int version, CancellationToken cancellationToken)
    {
        return await versionQueries.Any(
            x => x.Version == version && x.DefinitionId == definitionId,
            cancellationToken
        );
    }

    private async Task<WorkflowDefinition?> FindDefinition(string definitionId, CancellationToken cancellationToken)
    {
        return await definitionQueries.Find(
            x => x.Id == definitionId,
            cancellationToken
        );
    }

    private WorkflowDefinition Map(IWorkflowDefinition definition)
    {
        var id = !string.IsNullOrWhiteSpace(definition.Id)
            ? definition.Id
            : identityGenerator.Generate();

        return new()
        {
            Id = id,
            Description = definition.Description,
            Name = definition.Name
        };
    }

    private WorkflowDefinitionVersion Map(IWorkflowDefinitionVersion version)
    {
        var id = !string.IsNullOrWhiteSpace(version.Id)
            ? version.Id
            : identityGenerator.Generate();

        return new(version.Definition.Id, version.Version, sourceCreatedAt: version.SourceCreatedAt)
        {
            Id = id,
            State = version.State
        };
    }
}
