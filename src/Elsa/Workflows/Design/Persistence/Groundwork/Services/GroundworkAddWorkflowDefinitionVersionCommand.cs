using Elsa.Locking.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Primitives.Versioning;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkAddWorkflowDefinitionVersionCommand(
    IDesignAtomicWriter atomicWrite,
    IPayloadSerializer payloadSerializer,
    IWorkflowDefinitionVersionFactory versionFactory,
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionVersionStore versionStore,
    IDistributedLockProvider lockProvider,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IAddWorkflowDefinitionVersionCommand
{
    private const string OperationKind = "workflow.version.add.v1";

    public async Task<WorkflowDefinitionVersionAdded> Execute(
        DesignOperationKey operationKey,
        string definitionId,
        WorkflowDefinitionState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentNullException.ThrowIfNull(state);

        var lockKey = WorkflowDesignPersistenceLockKeys.DefinitionKey(definitionId);
        await using var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken);

        var requestMaterial = new AddVersionRequestMaterial(
            definitionId,
            GroundworkDesignSerialization.Execute(
                DesignPersistenceDomain.Workflow,
                OperationKind,
                "workflow definition version",
                () => payloadSerializer.Serialize(state)));
        WorkflowDefinitionVersion? version = null;

        var result = await GroundworkDesignAtomicCommand.ExecuteAsync(
            atomicWrite,
            operationKey,
            OperationKind,
            requestMaterial,
            [WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind],
            async (context, token) =>
            {
                var acceptedVersion = version ?? throw new InvalidOperationException(
                    "Workflow version allocation did not complete before staging.");
                var save = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    acceptedVersion,
                    GroundworkDesignDocumentSerialization.Create(payloadSerializer),
                    accessContextAccessor.Current,
                    persistenceDomain: DesignPersistenceDomain.Workflow) with
                { ExpectedVersion = 0 };
                await context.SaveAsync(save, token);
                return new WorkflowDefinitionVersionAdded(
                    acceptedVersion.DefinitionId,
                    acceptedVersion.Id,
                    acceptedVersion.Version);
            },
            cancellationToken: cancellationToken,
            beforeAttempt: async token =>
            {
                // Let EntityNotFoundException through. WorkflowsDesignApi maps it to 404; wrapping it
                // reached the generic 500 handler instead, so a definition that is merely absent -- or
                // that belongs to another tenant, which the scope-bound store reports the same way --
                // was answered as a server error. 404 is also the non-leaking answer for the
                // cross-tenant case: it says nothing about whether the id exists elsewhere.
                var definition = await definitionStore.GetAsync(definitionId, token);
                accessContextAccessor.Current.EnsureTenantScope(definition.TenantId);
                var lastVersion = await versionStore.FindLatestVersionAsync(definitionId, token);
                var nextVersion = WorkflowVersionNumbering.NextMajor(lastVersion?.Version);
                var nextSortKey = SemVer.ToSortKey(nextVersion);
                if (await versionStore.ExistsAsync(definitionId, nextSortKey, token))
                    throw new WorkflowDefinitionVersionConflictException(definitionId, nextVersion);

                version = WorkflowDefinitionVersion.From(
                    versionFactory.Create(definition, nextVersion, state));
                GroundworkEntityTimestamps.StampAdded(version, clock.UtcNow);
            });
        return result.Value;
    }

    private sealed record AddVersionRequestMaterial(
        string DefinitionId,
        string StateJson);
}
