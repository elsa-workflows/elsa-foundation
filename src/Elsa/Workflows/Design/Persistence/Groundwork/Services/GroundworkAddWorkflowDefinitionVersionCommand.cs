using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkAddWorkflowDefinitionVersionCommand(
    IDocumentStore store,
    IPayloadSerializer payloadSerializer,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IAddCommand<WorkflowDefinitionVersion>
{
    public Task Add(WorkflowDefinitionVersion entity, CancellationToken cancellationToken = default)
    {
        var save = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            entity,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer),
            accessContextAccessor.Current);

        return store.SaveAsync(save, cancellationToken);
    }
}
