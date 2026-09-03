using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkSaveWorkflowDefinitionCommand(
    GroundworkDesignStorage storage,
    IDesignAtomicWriter atomicWrite,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : ISaveWorkflowDefinitionCommand
{
    private const string OperationKind = "workflow.definition.save.v1";

    public async Task Execute(
        DesignOperationKey operationKey,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(storage);
        accessContextAccessor.Current.EnsureTenantScope(definition.TenantId);
        var requestMaterial = new SaveDefinitionRequestMaterial(
            definition.Id,
            definition.Name,
            definition.Description,
            definition.DeletedAt,
            definition.DeletedReason,
            definition.IsSourceOwned);

        await GroundworkDesignAtomicCommand.ExecuteAsync(
            atomicWrite,
            operationKey,
            OperationKind,
            requestMaterial,
            [WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind],
            async (context, token) =>
            {
                var existingEntry = context.Storage.Read(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, definition.Id);
                var existing = existingEntry is null ? null : context.Storage.MapDefinition(existingEntry);
                if (existing is null)
                    throw new InvalidOperationException($"Workflow definition '{definition.Id}' not found");
                GroundworkEntityTimestamps.StampSaved(definition, existing, clock.UtcNow);
                await context.SaveAsync(
                    GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    definition,
                    GroundworkDesignJson.Options,
                    accessContextAccessor.Current,
                    persistenceDomain: DesignPersistenceDomain.Workflow) with
                    { ExpectedVersion = existingEntry!.Entry.Version },
                    token);
                return definition.Id;
            },
            cancellationToken: cancellationToken);
    }

    private sealed record SaveDefinitionRequestMaterial(
        string DefinitionId,
        string Name,
        string? Description,
        DateTimeOffset? DeletedAt,
        string? DeletedReason,
        bool IsSourceOwned);
}
