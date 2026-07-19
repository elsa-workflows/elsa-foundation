using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkSaveWorkflowDefinitionCommand(
    IDocumentStore store,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : ISaveWorkflowDefinitionCommand
{
    public async Task Execute(WorkflowDefinition definition, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.Current.EnsureTenantScope(definition.TenantId);
        var existing = await new GroundworkWorkflowDefinitionStore(store).FindByIdAsync(definition.Id, cancellationToken);
        GroundworkEntityTimestamps.StampSaved(definition, existing, clock.UtcNow);

        await store.SaveAllAsync(
            DocumentCommitScope.Of(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind),
            [
                GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    definition,
                    GroundworkDesignJson.Options,
                    accessContextAccessor.Current)
            ],
            cancellationToken);
    }
}
