using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>Creates one source-owned workflow definition record without an authored draft.</summary>
public sealed class GroundworkMaterializeWorkflowDefinitionCommand(
    IDesignAtomicWriter atomicWrite,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IMaterializeWorkflowDefinitionCommand
{
    private const string OperationKind = "workflow.definition.materialize.v1";

    public async Task<string> Execute(
        DesignOperationKey operationKey,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentNullException.ThrowIfNull(definition);
        accessContextAccessor.Current.EnsureTenantScope(definition.TenantId);

        var requestMaterial = new MaterializeRequestMaterial(
            definition.Id,
            definition.Name,
            definition.Description,
            definition.DeletedAt,
            definition.DeletedReason,
            definition.IsSourceOwned);
        GroundworkEntityTimestamps.StampAdded(definition, clock.UtcNow);
        var save = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            definition,
            GroundworkDesignJson.Options,
            accessContextAccessor.Current,
            persistenceDomain: DesignPersistenceDomain.Workflow) with
        { ExpectedVersion = 0 };

        var result = await GroundworkDesignAtomicCommand.ExecuteAsync(
            atomicWrite,
            operationKey,
            OperationKind,
            requestMaterial,
            [WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind],
            async (context, token) =>
            {
                await context.SaveAsync(save, token);
                return definition.Id;
            },
            cancellationToken: cancellationToken);
        return result.Value;
    }

    private sealed record MaterializeRequestMaterial(
        string DefinitionId,
        string Name,
        string? Description,
        DateTimeOffset? DeletedAt,
        string? DeletedReason,
        bool IsSourceOwned);
}
