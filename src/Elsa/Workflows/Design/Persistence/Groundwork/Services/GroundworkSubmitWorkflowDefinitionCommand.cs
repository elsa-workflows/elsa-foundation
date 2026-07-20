using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkSubmitWorkflowDefinitionCommand(
    IIdentityGenerator identityGenerator,
    IDocumentStore store,
    IPayloadSerializer payloadSerializer,
    IActivityStructureService activityStructureService,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : ISubmitWorkflowDefinitionCommand
{
    private const string InitialVersion = "1.0.0";

    public async Task<SubmittedWorkflowDefinition> Execute(
        string name,
        string? description,
        WorkflowDefinitionState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(state);
        SubmittedActivityTreeValidator.Validate(state.RootActivity, activityStructureService);

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

        await store.SaveAllAsync(
            DocumentCommitScope.Of(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind),
            [
                GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    definition,
                    GroundworkDesignJson.Options,
                    accessContextAccessor.Current),
                draftDocuments.ToSaveRequest(draft, []),
                GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    version,
                    GroundworkDesignDocumentSerialization.Create(payloadSerializer),
                    accessContextAccessor.Current),
                GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    versionLayout,
                    GroundworkDesignJson.Options,
                    accessContextAccessor.Current)
            ],
            cancellationToken);

        return new SubmittedWorkflowDefinition(definitionId, draftId, versionId);
    }
}
