using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>Materializes one immutable source-authored workflow version.</summary>
public sealed class GroundworkMaterializeWorkflowDefinitionVersionCommand(
    IDesignAtomicWriter atomicWrite,
    IPayloadSerializer payloadSerializer,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IMaterializeWorkflowDefinitionVersionCommand
{
    private const string OperationKind = "workflow.version.materialize.v1";

    public async Task<WorkflowDefinitionVersionAdded> Execute(
        DesignOperationKey operationKey,
        WorkflowDefinitionVersion version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentNullException.ThrowIfNull(version);
        accessContextAccessor.Current.EnsureTenantScope(version.TenantId);

        var requestMaterial = new MaterializeVersionRequestMaterial(
            version.DefinitionId,
            version.Id,
            version.Version,
            version.SourceDraftId,
            version.SourceCreatedAt,
            GroundworkDesignSerialization.Execute(
                DesignPersistenceDomain.Workflow,
                OperationKind,
                "workflow definition version",
                () => payloadSerializer.Serialize(version.State)));
        GroundworkEntityTimestamps.StampAdded(version, clock.UtcNow);
        var save = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            version,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer),
            accessContextAccessor.Current,
            persistenceDomain: DesignPersistenceDomain.Workflow) with
        { ExpectedVersion = 0 };

        var result = await GroundworkDesignAtomicCommand.ExecuteAsync(
            atomicWrite,
            operationKey,
            OperationKind,
            requestMaterial,
            [WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind],
            async (context, token) =>
            {
                await context.SaveAsync(save, token);
                return new WorkflowDefinitionVersionAdded(
                    version.DefinitionId,
                    version.Id,
                    version.Version);
            },
            cancellationToken: cancellationToken);
        return result.Value;
    }

    private sealed record MaterializeVersionRequestMaterial(
        string DefinitionId,
        string VersionId,
        string Version,
        string? SourceDraftId,
        DateTimeOffset? SourceCreatedAt,
        string StateJson);
}
