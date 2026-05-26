using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Extensions;
using Elsa.Activities.Design.Provisioning.Core;
using Elsa.Activities.Design.Provisioning.Options;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;

namespace Elsa.Activities.Design.Provisioning.Services;

public sealed class ActivityVersionProvisioner(
    ILogger<ActivityVersionProvisioner> logger,
    IDomainEventSender sender,
    IOptions<ActivityVersionProvisionerOptions> options,
    IIdentityGenerator identityGenerator,
    IQueries<ActivityDefinition> definitionQueries,
    IQueries<ActivityDefinitionVersion> versionQueries,
    IAddActivityDefinitionCommand addNewDefinitionCommand,
    IAddCommand<ActivityDefinitionVersion> addVersionCommand,
    IDeleteCommand<ActivityDefinitionVersion> deleteVersion
)
    : IActivityVersionProvisioner
{
    public async Task Provision(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var versions = new Collection<IActivityDefinitionVersion>();
        var @event = new OnActivityVersionsProvisioning(versions);
        await sender.Send(@event, cancellationToken);

        foreach (var version in versions)
        {
            await ProvisionVersion(version, cancellationToken);
        }
    }

    private async Task ProvisionVersion(IActivityDefinitionVersion version, CancellationToken cancellationToken)
    {
        var mappedVersion = Map(version);
        var mappedDefinition = Map(version.Definition);

        var definition = await FindDefinition(version.Definition.Id, version.Definition.UniqueName, cancellationToken);
        if (definition is null)
        {
            await addNewDefinitionCommand.Execute(mappedDefinition, mappedVersion, cancellationToken);
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

    private async Task HandleDuplicate(ActivityDefinition def, ActivityDefinitionVersion version, CancellationToken cancellationToken)
    {
        switch (options.Value.DuplicateHandling)
        {
            case DuplicateHandling.Overwrite:
                await OverwriteVersion(def, version, cancellationToken);
                break;

            case DuplicateHandling.Throw:
                throw new InvalidOperationException($"Activity definition version '{version.DefinitionId}' v{version.Version} already exists");

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
            logger.LogInformation("Skipping duplicate activity definition '{def}' v{v}", definitionId, version);
    }

    private void LogSkipOutdated(string definitionId, int version)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Skipping outdated workflow definition '{def}' v{v}", definitionId, version);
    }

    private async Task OverwriteVersion(ActivityDefinition definition, ActivityDefinitionVersion version, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Overwriting activity definition '{def}' v{v}", definition.Id, version.Version);

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

    private async Task<ActivityDefinition?> FindDefinition(string definitionId, string uniqueName, CancellationToken cancellationToken)
    {
        var definition = await definitionQueries.Find(
            x => x.Id == definitionId || x.UniqueName == uniqueName,
            cancellationToken
        );

        if (definition is null)
        {
            return null;
        }

        if (definition.Id != definitionId || definition.UniqueName != uniqueName)
        {
            throw new InvalidOperationException(
                $"Activity definition identity mismatch. Trying to provision definition (id = '{definitionId}', uniqueName = '{uniqueName}'); found existing definition (id = '{definition.Id}', uniqueName = '{definition.UniqueName}')"
            );
        }

        return definition;
    }

    private ActivityDefinition Map(IActivityDefinition definition)
    {
        var id = !string.IsNullOrWhiteSpace(definition.Id)
            ? definition.Id
            : identityGenerator.Generate();

        return new()
        {
            Id = id,
            UniqueName = definition.UniqueName,
            Category = definition.Category,
            Description = definition.Description,
            DisplayName = definition.DisplayName,
            IsBrowsable = definition.IsBrowsable
        };
    }

    private ActivityDefinitionVersion Map(IActivityDefinitionVersion version)
    {
        var id = !string.IsNullOrWhiteSpace(version.Id)
            ? version.Id
            : identityGenerator.Generate();

        return new(version.Version, version.Definition.Id, kind: version.Kind)
        {
            Id = id,
            Outputs = version.Outputs,
            Inputs = version.Inputs,
            Ports = version.Ports,
            TypeInfo = version.TypeInfo
        };
    }
}
