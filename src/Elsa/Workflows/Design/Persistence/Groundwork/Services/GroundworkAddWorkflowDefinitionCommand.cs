using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork (document) implementation of <see cref="IAddWorkflowDefinitionCommand"/>, the document-store
/// counterpart of the EF Core <c>AddWorkflowDefinition</c>. It stages the <c>workflowDefinition</c> and its first
/// embedded <c>workflowDefinitionDraft</c> into one Groundwork <see cref="IDocumentUnitOfWork"/> and commits them
/// together.
/// </summary>
public sealed class GroundworkAddWorkflowDefinitionCommand(IDocumentStore store, IPayloadSerializer payloadSerializer, ISystemClock clock)
    : IAddWorkflowDefinitionCommand
{
    public async Task Execute(WorkflowDefinition workflowDefinition, WorkflowDefinitionDraft draft, CancellationToken cancellation)
    {
        var now = clock.UtcNow;
        GroundworkEntityTimestamps.StampAdded(workflowDefinition, now);
        GroundworkEntityTimestamps.StampAdded(draft, now);

        var definitionSave = GroundworkDocumentWriter.ToSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            workflowDefinition,
            GroundworkDesignJson.Options);

        var draftDocuments = new GroundworkWorkflowDefinitionDraftDocumentStore(
            store,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer));
        var draftSave = draftDocuments.ToSaveRequest(draft, [], []);

        await store.SaveAllAsync(
            DocumentCommitScope.Of(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind),
            [definitionSave, draftSave],
            cancellation);
    }
}
