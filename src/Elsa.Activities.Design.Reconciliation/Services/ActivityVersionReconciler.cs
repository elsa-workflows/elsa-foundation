using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Reconciliation.Services;

/// <summary>
/// Model X reconciliation policy (Unit C 2026-05-28; pending 2026-06-01 architecture
/// review). For each candidate version contributed by source modules:
///
/// 1. Find the parent definition by its surrogate Id or by the natural key
///    <c>(SourceKind, SourceId, ActivityTypeKey)</c>.
/// 2. If no parent exists, persist a fresh Definition + Version with immutable provenance.
///    The version carries an immutable <see cref="ActivityDefinitionVersion.ReconcilliationHash"/>
///    computed from its content.
/// 3. If the parent exists but the specific Version number does not, append the Version
///    with immutable provenance (including ProvisioningHash).
/// 4. If <c>(DefinitionId, Version)</c> is already persisted, compute the incoming hash
///    and compare to the persisted ProvisioningHash:
///    - Hashes differ → <see cref="ActivityVersionHashMismatchException"/> (the source is
///      broken — same logical identity, different content).
///    - Hashes match → skip or throw per the <see cref="DuplicateHandling"/> option.
///
/// No per-pass mutating state is carried by any entity in this domain. Source disappearance
/// is intentionally not tracked. Versions are never deleted.
/// </summary>
public sealed class ActivityVersionReconciler(
    ILogger<ActivityVersionReconciler> logger,
    IDomainEventSender sender,
    IOptions<ActivityVersionReconcilerOptions> options,
    IIdentityGenerator identityGenerator,
    IActivityDefinitionHasher hasher,
    IQueries<ActivityDefinition> definitionQueries,
    IQueries<ActivityDefinitionVersion> versionQueries,
    IAddActivityDefinitionCommand addNewDefinitionCommand,
    IAddCommand<ActivityDefinitionVersion> addVersionCommand
)
    : IActivityVersionReconciler
{
    public async Task Reconcile(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Method-based contribution per framework §2.6.1's "intent-revealing methods" sub-rule.
        // Handlers contribute via OnActivityVersionsReconciling.AddVersion(...); the dispatcher
        // reads the accumulated contributions via the public Versions read-only property
        // (typed IReadOnlyList — no caller can replace or mutate the list).
        var @event = new OnActivityVersionsReconciling();
        await sender.Send(@event, cancellationToken);

        foreach (var version in @event.Versions)
        {
            await ReconcileVersion(version, cancellationToken);
        }
    }

    private async Task ReconcileVersion(IActivityDefinitionVersion incomingVersion, CancellationToken cancellationToken)
    {
        var incomingHash = hasher.Hash(incomingVersion.Definition, incomingVersion);

        var definition = await FindDefinition(
            incomingVersion.Definition.Id,
            incomingVersion.Definition.ActivityTypeKey,
            cancellationToken
        );

        if (definition is null)
        {
            // Fresh definition + first version.
            var mappedDef = Map(incomingVersion.Definition);
            var mappedVer = Map(incomingVersion, mappedDef.Id, incomingHash);
            await addNewDefinitionCommand.Execute(mappedDef, mappedVer, cancellationToken);
            return;
        }

        // Definition exists. Lookup the specific version by (DefinitionId, Version).
        var existingVersion = await versionQueries.Find(
            v => v.DefinitionId == definition.Id && v.Version == incomingVersion.Version,
            cancellationToken
        );

        if (existingVersion is null)
        {
            // Definition exists but this Version number doesn't. Append.
            var mappedVer = Map(incomingVersion, definition.Id, incomingHash);
            await addVersionCommand.Add(mappedVer, cancellationToken);
            return;
        }

        // (DefinitionId, Version) is already persisted — Model X duplicate path.
        if (!string.Equals(existingVersion.ReconcilliationHash, incomingHash, StringComparison.Ordinal))
        {
            // Same identity, different content — the source is broken. Throw loudly.
            throw new ActivityVersionHashMismatchException(
                definitionId: definition.Id,
                activityTypeKey: definition.ActivityTypeKey,
                version: incomingVersion.Version,
                persistedHash: existingVersion.ReconcilliationHash,
                incomingHash: incomingHash
            );
        }

        // Hash matches → genuine duplicate.
        HandleHashMatchedDuplicate(definition, incomingVersion.Version);
    }

    private void HandleHashMatchedDuplicate(ActivityDefinition definition, int version)
    {
        switch (options.Value.DuplicateHandling)
        {
            case DuplicateHandling.Throw:
                throw new InvalidOperationException(
                    $"Activity definition '{definition.ActivityTypeKey}' (id = '{definition.Id}') already has version {version} persisted with matching content hash.");

            case DuplicateHandling.Skip:
                LogSkipDuplicate(definition.Id, definition.ActivityTypeKey, version);
                break;

            default:
                break;
        }
    }

    private void LogSkipDuplicate(string definitionId, string activityTypeKey, int version)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(
                "Reconciliation: skipping hash-matched duplicate. Definition '{key}' (id = '{id}'), version {v}.",
                activityTypeKey, definitionId, version);
    }

    private async Task<ActivityDefinition?> FindDefinition(
        string definitionId,
        string activityTypeKey,
        CancellationToken cancellationToken)
    {
        var definition = await definitionQueries.Find(
            x => x.Id == definitionId || x.ActivityTypeKey == activityTypeKey,
            cancellationToken
        );

        if (definition is null)
            return null;

        // Sanity check: if the surrogate Id matched but the natural key disagrees (or vice
        // versa), that's a real identity collision. Throw — the source has produced a row
        // whose logical identity contradicts an existing row's.
        var idMatches = definition.Id == definitionId;
        var naturalKeyMatches = definition.ActivityTypeKey == activityTypeKey;

        if (!idMatches && !naturalKeyMatches)
        {
            throw new InvalidOperationException(
                $"Activity definition identity mismatch. Trying to reconcile definition (id = '{definitionId}', ActivityTypeKey = '{activityTypeKey}'); found existing definition (id = '{definition.Id}', ActivityTypeKey = '{definition.ActivityTypeKey}').");
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
            Category = definition.Category,
            Description = definition.Description,
            DisplayName = definition.DisplayName,
        };
    }

    private ActivityDefinitionVersion Map(IActivityDefinitionVersion version, string definitionId, string hash)
    {
        var id = !string.IsNullOrWhiteSpace(version.Id)
            ? version.Id
            : identityGenerator.Generate();

        return new(version.Version, definitionId, executionType: version.ExecutionType)
        {
            Id = id,
            ImplementationKind = version.ImplementationKind,
            ImplementationDescriptor = version.ImplementationDescriptor,
            Outputs = version.Outputs,
            Inputs = version.Inputs,
            Ports = version.Ports,
            ReconcilliationHash = hash,
        };
    }
}
