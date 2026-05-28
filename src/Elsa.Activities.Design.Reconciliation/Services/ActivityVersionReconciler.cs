using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Extensions;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;

namespace Elsa.Activities.Design.Reconciliation.Services;

public sealed class ActivityVersionReconciler(
    ILogger<ActivityVersionReconciler> logger,
    IDomainEventSender sender,
    IOptions<ActivityVersionReconcilerOptions> options,
    IIdentityGenerator identityGenerator,
    IQueries<ActivityDefinition> definitionQueries,
    IQueries<ActivityDefinitionVersion> versionQueries,
    IAddActivityDefinitionCommand addNewDefinitionCommand,
    IAddCommand<ActivityDefinitionVersion> addVersionCommand,
    IDeleteCommand<ActivityDefinitionVersion> deleteVersion
)
    : IActivityVersionReconciler
{
    public async Task Reconcile(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var versions = new Collection<IActivityDefinitionVersion>();
        var @event = new OnActivityVersionsReconciling(versions);
        await sender.Send(@event, cancellationToken);

        foreach (var version in versions)
        {
            await ReconcileVersion(version, cancellationToken);
        }
    }

    private async Task ReconcileVersion(IActivityDefinitionVersion version, CancellationToken cancellationToken)
    {
        var mappedVersion = Map(version);
        var mappedDefinition = Map(version.Definition);

        var definition = await FindDefinition(version.Definition.Id, version.Definition.ActivityTypeKey, version.Definition.SourceKind, version.Definition.SourceId, cancellationToken);
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
            logger.LogInformation("Skipping outdated activity definition '{def}' v{v}", definitionId, version);
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

    private async Task<ActivityDefinition?> FindDefinition(string definitionId, string activityTypeKey, string sourceKind, string sourceId, CancellationToken cancellationToken)
    {
        var definition = await definitionQueries.Find(
            x => x.Id == definitionId
                 || (x.SourceKind == sourceKind && x.SourceId == sourceId && x.ActivityTypeKey == activityTypeKey),
            cancellationToken
        );

        if (definition is null)
        {
            return null;
        }

        var identityMatches = definition.Id == definitionId
            || (definition.SourceKind == sourceKind && definition.SourceId == sourceId && definition.ActivityTypeKey == activityTypeKey);

        if (!identityMatches)
        {
            throw new InvalidOperationException(
                $"Activity definition identity mismatch. Trying to reconcile definition (id = '{definitionId}', SourceKind = '{sourceKind}', SourceId = '{sourceId}', ActivityTypeKey = '{activityTypeKey}'); found existing definition (id = '{definition.Id}', SourceKind = '{definition.SourceKind}', SourceId = '{definition.SourceId}', ActivityTypeKey = '{definition.ActivityTypeKey}')"
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
            ActivityTypeKey = definition.ActivityTypeKey,
            SourceKind = definition.SourceKind,
            SourceId = definition.SourceId,
            ProvisionedAt = definition.ProvisionedAt,
            ProvisionedBy = definition.ProvisionedBy,
            Category = definition.Category,
            Description = definition.Description,
            DisplayName = definition.DisplayName
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
            ActivityTypeKey = version.ActivityTypeKey,
            ImplementationKind = version.ImplementationKind,
            ImplementationDescriptor = version.ImplementationDescriptor,
            Outputs = version.Outputs,
            Inputs = version.Inputs,
            Ports = version.Ports
        };
    }
}
