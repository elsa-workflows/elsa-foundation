using Elsa.Activities.Design.Persistence.Groundwork;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Constants;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

/// <summary>
/// Creates an activity definition and its first immutable version in one replay-safe Groundwork unit of work.
/// The durable operation marker is included in the same commit scope as both documents, so a retry can return
/// the original accepted identities without staging the caller's new entity instances.
/// </summary>
public sealed class GroundworkAddActivityDefinitionCommand(
    IPayloadSerializer payloadSerializer,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IActivityDefinitionStore definitionStore,
    IDistributedLockProvider lockProvider,
    ISystemClock clock,
    IDesignAtomicWriter atomicWrite)
    : IAddActivityDefinitionCommand
{
    private const string OperationKind = "activity.definition.create.v1";

    public async Task<ActivityDefinitionCreated> Execute(
        DesignOperationKey operationKey,
        ActivityDefinition definition,
        ActivityDefinitionVersion version,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(operationKey);

        // The Groundwork document writer does not auto-stamp entity timestamps,
        // so a create would otherwise persist
        // CreatedAt/LastModifiedAt as DateTimeOffset.MinValue — the studio then renders "Updated 01/01/1".
        var now = clock.UtcNow;
        definition.CreatedAt = definition.LastModifiedAt = now;
        version.CreatedAt = version.LastModifiedAt = now;

        var accessContext = accessContextAccessor.Current;
        accessContext.EnsureTenantScope(definition.TenantId);
        accessContext.EnsureTenantScope(version.TenantId);

        var versionJson = GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer);
        var requestMaterial = new CreateRequestMaterial(
            new DefinitionMaterial(
                definition.ActivityTypeKey,
                definition.Category,
                definition.DisplayName,
                definition.Description),
            VersionMaterial.From(version));
        var lockKey = ActivityDesignPersistenceLockKeys.DefinitionTypeKey(
            definition.TenantId,
            definition.ActivityTypeKey);
        await using var lockHandle = await lockProvider.AcquireLockAsync(
            lockKey,
            null,
            cancellation);

        var result = await GroundworkDesignAtomicCommand.ExecuteAsync(
            atomicWrite,
            operationKey,
            OperationKind,
            requestMaterial,
            [
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind
            ],
            async (context, cancellationToken) =>
            {
                await context.SaveAsync(
                    GroundworkV2ActivityDesignDocumentWriter.ToTenantScopedSaveRequest(
                        ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                        ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
                        ActivitiesDesignStorageManifest.SchemaVersion,
                        definition,
                        GroundworkActivitiesDesignJson.Options,
                        accessContext,
                        persistenceDomain: DesignPersistenceDomain.Activity) with
                    { ExpectedVersion = 0 },
                    cancellationToken);
                await context.SaveAsync(
                    GroundworkV2ActivityDesignDocumentWriter.ToTenantScopedSaveRequest(
                        ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
                        ActivitiesDesignStorageManifest.ActivityDefinitionVersionCollection,
                        ActivitiesDesignStorageManifest.SchemaVersion,
                        version,
                        versionJson,
                        accessContext,
                        persistenceDomain: DesignPersistenceDomain.Activity) with
                    { ExpectedVersion = 0 },
                    cancellationToken);

                return new ActivityDefinitionCreated(definition.Id, version.Id, version.Version, version.Hash);
            },
            jsonOptions: versionJson,
            cancellationToken: cancellation,
            beforeAttempt: async token =>
            {
                if (await definitionStore.ExistsByActivityTypeKeyAsync(definition.ActivityTypeKey, token))
                {
                    throw new ArgumentException(
                        $"Activity definition with ActivityTypeKey='{definition.ActivityTypeKey}' already exists");
                }
            });
        return result.Value;
    }

    // This deliberately excludes generated IDs, tenant/scope values, timestamps, SemVerSortKey, and Hash.
    // DefinitionId is likewise excluded because this command creates that definition and its initial version.
    private sealed record CreateRequestMaterial(DefinitionMaterial Catalog, VersionMaterial Version);

    private sealed record DefinitionMaterial(
        string ActivityTypeKey,
        string Category,
        string? DisplayName,
        string? Description);

    private sealed record VersionMaterial(
        string Version,
        string ProviderKey,
        string ProviderSchemaVersion,
        string ConsumerKey,
        string ConsumerSchemaVersion,
        JsonElement? DescriptorPayload,
        IEnumerable<InputDefinition> Inputs,
        IEnumerable<OutputDefinition> Outputs,
        IEnumerable<ActivityDesignFacet> DesignFacets,
        ActivityExecutionType ExecutionType,
        string SourceKind,
        string SourceId)
    {
        public static VersionMaterial From(ActivityDefinitionVersion version) => new(
            version.Version,
            version.ProviderKey,
            version.ProviderSchemaVersion,
            version.ConsumerKey,
            version.ConsumerSchemaVersion,
            version.DescriptorPayload.ValueKind == JsonValueKind.Undefined ? null : version.DescriptorPayload,
            version.Inputs,
            version.Outputs,
            version.DesignFacets,
            version.ExecutionType,
            version.SourceKind,
            version.SourceId);
    }
}
