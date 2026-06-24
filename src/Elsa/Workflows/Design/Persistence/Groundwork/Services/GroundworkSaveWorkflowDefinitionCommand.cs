using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkSaveWorkflowDefinitionCommand(IDocumentStore store, ISystemClock clock)
    : ISaveWorkflowDefinitionCommand
{
    public async Task Execute(WorkflowDefinition definition, CancellationToken cancellationToken = default)
    {
        var existing = await new GroundworkWorkflowDefinitionStore(store).FindByIdAsync(definition.Id, cancellationToken);
        GroundworkEntityTimestamps.StampSaved(definition, existing, clock.UtcNow);

        await store.SaveAllAsync(
            DocumentCommitScope.Of(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind),
            [
                GroundworkDocumentWriter.ToSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    definition,
                    GroundworkDesignJson.Options)
            ],
            cancellationToken);
    }
}
