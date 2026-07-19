using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
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
        WorkflowDefinitionConstraints.Validate(definition);
        accessContextAccessor.Current.EnsureTenantScope(definition.TenantId);
        var existingEnvelope = await store.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            definition.Id,
            cancellationToken);
        var existingDocument = existingEnvelope is null
            ? null
            : GroundworkWorkflowDefinitionDocuments.Deserialize(existingEnvelope);
        GroundworkEntityTimestamps.StampSaved(definition, existingDocument?.Entity, clock.UtcNow);

        await store.SaveAllAsync(
            DocumentCommitScope.Of(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind),
            [
                GroundworkWorkflowDefinitionDocuments.Save(
                    definition,
                    existingDocument?.MarkerProjection ?? GroundworkWorkflowDefinitionTagStore.MarkerProjection([]),
                    existingEnvelope?.Version,
                    existingDocument?.ControlledProjection ?? GroundworkWorkflowDefinitionTagStore.ControlledProjection([]))
            ],
            cancellationToken);
    }
}
