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
    ISystemClock clock,
    IActivityDefinitionHasher hasher,
    IQueries<ActivityDefinition> definitionQueries,
    IQueries<ActivityDefinitionVersion> versionQueries,
    IQueries<ActivityDefinitionReconciliationState> stateQueries,
    IAddActivityDefinitionCommand addNewDefinitionCommand,
    IAddCommand<ActivityDefinitionVersion> addVersionCommand,
    ISaveCommand<ActivityDefinitionReconciliationState> saveReconciliationState
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
            await UpdateReconciliationState(mappedDefinition, version, mappedVersion, versionAppended: true, cancellationToken);
            return;
        }

        var latestVersion = await versionQueries.FindLastVersion(definition.Id, cancellationToken);
        if (mappedVersion.Version < latestVersion?.Version)
        {
            LogSkipOutdated(definition.Id, mappedVersion.Version);
            await UpdateReconciliationState(definition, version, mappedVersion, versionAppended: false, cancellationToken);
            return;
        }

        var versionExists = await VersionExists(definition.Id, version.Version, cancellationToken);
        if (!versionExists)
        {
            await addVersionCommand.Add(WithDefinitionId(mappedVersion, definition.Id), cancellationToken);
            await UpdateReconciliationState(definition, version, mappedVersion, versionAppended: true, cancellationToken);
            return;
        }

        await HandleDuplicate(definition, mappedVersion, cancellationToken);
        await UpdateReconciliationState(definition, version, mappedVersion, versionAppended: false, cancellationToken);
    }

    private async Task HandleDuplicate(ActivityDefinition def, ActivityDefinitionVersion version, CancellationToken cancellationToken)
    {
        switch (options.Value.DuplicateHandling)
        {
            case DuplicateHandling.Throw:
                throw new InvalidOperationException($"Activity definition version '{version.DefinitionId}' v{version.Version} already exists");

            case DuplicateHandling.Skip:
                LogSkipDuplicate(def.Id, version.Version);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Upsert the reconciliation-state sibling for the given parent. Refresh <c>LastSeenAt</c>
    /// on every observation; refresh hash + <c>LastProvisionedAt</c> only when content
    /// actually changed (hash mismatch OR a new version was appended). This keeps the parent
    /// row's <c>LastModifiedAt</c> stable across no-op reconciliation passes (SC-013 idempotency).
    /// </summary>
    private async Task UpdateReconciliationState(
        IActivityDefinition definition,
        IActivityDefinitionVersion incomingVersion,
        ActivityDefinitionVersion mappedVersion,
        bool versionAppended,
        CancellationToken cancellationToken)
    {
        var newHash = hasher.Hash(definition, incomingVersion);
        var existing = await stateQueries.Find(s => s.ActivityDefinitionId == definition.Id, cancellationToken);
        var now = clock.UtcNow;

        var state = existing ?? new ActivityDefinitionReconciliationState
        {
            Id = identityGenerator.Generate(),
            ActivityDefinitionId = definition.Id,
            TenantId = definition.TenantId(),
            LastProvisionedAt = now,
            LastProvisionedBy = definition.ProvisionedBy,
        };

        state.LastSeenAt = now;

        var contentChanged = versionAppended || existing is null || !string.Equals(existing.ProvisioningHash, newHash, StringComparison.Ordinal);
        if (contentChanged)
        {
            state.ProvisioningHash = newHash;
            state.LastProvisionedAt = now;
            state.LastProvisionedBy = definition.ProvisionedBy;
            state.SourceVersion = mappedVersion.Version.ToString();
        }

        await saveReconciliationState.SaveAsync(state, cancellationToken);
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

        return new(version.Version, version.Definition.Id, executionType: version.ExecutionType)
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

    private static ActivityDefinitionVersion WithDefinitionId(ActivityDefinitionVersion version, string definitionId)
    {
        // Re-bind the version's FK to the resolved parent id, which may differ from the
        // incoming candidate's nominal Definition.Id when find-by-(SourceKind, SourceId,
        // ActivityTypeKey) matches a row whose generated Id is different.
        return new ActivityDefinitionVersion(version.Version, definitionId, executionType: version.ExecutionType)
        {
            Id = version.Id,
            ActivityTypeKey = version.ActivityTypeKey,
            ImplementationKind = version.ImplementationKind,
            ImplementationDescriptor = version.ImplementationDescriptor,
            Inputs = version.Inputs,
            Outputs = version.Outputs,
            Ports = version.Ports,
            InputsSource = version.InputsSource,
            OutputsSource = version.OutputsSource,
            PortsSource = version.PortsSource,
        };
    }
}

internal static class ActivityDefinitionReadExtensions
{
    /// <summary>
    /// Tenant-id accessor over the read contract — <see cref="IActivityDefinition"/> doesn't
    /// expose it (per the read-contract pin in spec FR-008), but the concrete entity does
    /// via <c>TenantEntity</c>. Safe cast: every persisted <c>IActivityDefinition</c> is an
    /// <c>ActivityDefinition</c> in this code path.
    /// </summary>
    internal static string? TenantId(this IActivityDefinition definition)
        => (definition as ActivityDefinition)?.TenantId;
}
