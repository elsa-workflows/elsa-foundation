using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkSubmitWorkflowDefinitionCommand(
    IIdentityGenerator identityGenerator,
    IDocumentStore store,
    GroundworkDesignAtomicWrite atomicWrite,
    IPayloadSerializer payloadSerializer,
    IActivityStructureService activityStructureService,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : ISubmitWorkflowDefinitionCommand
{
    private const string InitialVersion = "1.0.0";
    private const string OperationKind = "workflow.definition.submit.v1";

    public async Task<SubmittedWorkflowDefinition> Execute(
        DesignOperationKey operationKey,
        string name,
        string? description,
        WorkflowDefinitionState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(state);
        SubmittedActivityTreeValidator.Validate(state.RootActivity, activityStructureService);
        var requestMaterial = new SubmitDefinitionRequestMaterial(
            name,
            description,
            GroundworkDesignSerialization.Execute(
                DesignPersistenceDomain.Workflow,
                OperationKind,
                "workflow definition",
                () => payloadSerializer.Serialize(state)));

        var definitionId = identityGenerator.Generate();
        var draftId = identityGenerator.Generate();
        var versionId = identityGenerator.Generate();
        var tenantId = accessContextAccessor.Current.Scope?.Value;

        var definition = new WorkflowDefinition
        {
            Id = definitionId,
            TenantId = tenantId,
            Name = name,
            Description = description
        };

        var draft = new WorkflowDefinitionDraft
        {
            Id = draftId,
            TenantId = tenantId,
            WorkflowDefinitionId = definitionId,
            State = state
        };

        var version = new WorkflowDefinitionVersion(definitionId, InitialVersion)
        {
            Id = versionId,
            TenantId = tenantId,
            State = state
        };

        var versionLayout = new WorkflowDefinitionVersionLayout
        {
            Id = identityGenerator.Generate(),
            TenantId = tenantId,
            WorkflowDefinitionVersionId = versionId,
            Records = []
        };

        var draftDocuments = new GroundworkWorkflowDefinitionDraftDocumentStore(
            store,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer),
            accessContextAccessor);
        var now = clock.UtcNow;
        GroundworkEntityTimestamps.StampAdded(definition, now);
        GroundworkEntityTimestamps.StampAdded(draft, now);
        GroundworkEntityTimestamps.StampAdded(version, now);
        GroundworkEntityTimestamps.StampAdded(versionLayout, now);

        var definitionSave = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            definition,
            GroundworkDesignJson.Options,
            accessContextAccessor.Current,
            persistenceDomain: DesignPersistenceDomain.Workflow) with
        { ExpectedVersion = 0 };
        var draftSave = draftDocuments.ToSaveRequest(draft, []) with { ExpectedVersion = 0 };
        var versionSave = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            version,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer),
            accessContextAccessor.Current,
            persistenceDomain: DesignPersistenceDomain.Workflow) with
        { ExpectedVersion = 0 };
        var layoutSave = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            versionLayout,
            GroundworkDesignJson.Options,
            accessContextAccessor.Current,
            persistenceDomain: DesignPersistenceDomain.Workflow) with
        { ExpectedVersion = 0 };

        var outcome = await GroundworkDesignAtomicCommand.ExecuteAsync(
            atomicWrite,
            operationKey,
            OperationKind,
            requestMaterial,
            [
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind
            ],
            async (context, token) =>
            {
                await context.SaveAsync(definitionSave, token);
                await context.SaveAsync(draftSave, token);
                await context.SaveAsync(versionSave, token);
                await context.SaveAsync(layoutSave, token);
                return new SubmittedWorkflowDefinition(definitionId, draftId, versionId);
            },
            cancellationToken: cancellationToken);
        return outcome.Value;
    }

    private sealed record SubmitDefinitionRequestMaterial(
        string Name,
        string? Description,
        string StateJson);
}
