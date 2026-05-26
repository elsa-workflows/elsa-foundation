using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Enums;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Extensions;
using Elsa.Workflows.Design.Provisioning.Core.Contracts;
using Elsa.Workflows.Design.Provisioning.Core.Events;
using Elsa.Workflows.Design.Provisioning.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;

namespace Elsa.Workflows.Design.Provisioning.Services;

public sealed class WorkflowsVersionProvisioner(
    ILogger<WorkflowsVersionProvisioner> logger,
    IDomainEventSender sender,
    IOptions<WorkflowVersionProvisionerOptions> options,
    IIdentityGenerator identityGenerator,
    IQueries<WorkflowDefinition> definitionQueries,
    IQueries<WorkflowDefinitionVersion> versionQueries,
    IAddCommand<WorkflowDefinition> addDefinitionCommand,
    IAddCommand<WorkflowDefinitionVersion> addVersionCommand,
    IDeleteCommand<WorkflowDefinitionVersion> deleteVersion
)
    : IWorkflowVersionProvisioner
{
    public async Task Provision(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var versions = new Collection<IWorkflowDefinitionVersion>();
        var @event = new OnWorkflowVersionsProvisioning(versions);
        await sender.Send(@event, cancellationToken);

        foreach (var version in versions)
        {
            await ProvisionVersion(version, cancellationToken);
        }
    }

    private async Task ProvisionVersion(IWorkflowDefinitionVersion version, CancellationToken cancellationToken)
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

        await HandleDuplicate(mappedDefinition, mappedVersion, cancellationToken);
    }

    private async Task HandleDuplicate(WorkflowDefinition def, WorkflowDefinitionVersion version, CancellationToken cancellationToken)
    {
        switch (options.Value.DuplicateHandling)
        {
            case DuplicateHandling.Overwrite:
                await OverwriteVersion(def, version, cancellationToken);
                break;

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

    private async Task OverwriteVersion(WorkflowDefinition definition, WorkflowDefinitionVersion version, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Overwriting workflow definition '{def}' v{v}", definition.Id, version.Version);

        await deleteVersion.DeleteWhere(x => x.Id == version.Id, cancellationToken);
        await addVersionCommand.Add(version, cancellationToken);
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
            DraftId = definition.Draft?.Id,
            MetaData = definition.MetaData,
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

