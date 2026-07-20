using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkSaveWorkflowDefinitionCommand(
    IDocumentStore store,
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
        accessContextAccessor.Current.EnsureTenantScope(definition.TenantId);
        var requestMaterial = new SaveDefinitionRequestMaterial(
            definition.Id,
            definition.Name,
            definition.Description,
            definition.DeletedAt,
            definition.DeletedReason,
            definition.IsSourceOwned);

        _ = await GroundworkDesignAtomicCommand.ExecuteAsync(
            atomicWrite,
            operationKey,
            OperationKind,
            requestMaterial,
            [WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind],
            async (context, token) =>
            {
                var existing = await new GroundworkWorkflowDefinitionStore(store)
                    .FindByIdAsync(definition.Id, token)
                    ?? throw new InvalidOperationException(
                        $"Workflow definition '{definition.Id}' not found");
                GroundworkEntityTimestamps.StampSaved(definition, existing, clock.UtcNow);
                await context.SaveAsync(
                    GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    definition,
                    GroundworkDesignJson.Options,
                    accessContextAccessor.Current,
                    persistenceDomain: DesignPersistenceDomain.Workflow),
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
