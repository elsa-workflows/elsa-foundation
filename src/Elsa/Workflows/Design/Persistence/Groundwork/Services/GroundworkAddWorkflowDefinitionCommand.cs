using Elsa.Persistence.Groundwork.Querying;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork (document) implementation of <see cref="IAddWorkflowDefinitionCommand"/>, the document-store
/// counterpart of the EF Core <c>AddWorkflowDefinition</c>. Where the EF command relies on a single
/// <c>SaveChangesAsync</c> for atomicity, this stages the <c>workflowDefinition</c> and its first
/// <c>workflowDefinitionDraft</c> into one Groundwork <see cref="IDocumentUnitOfWork"/> and commits them
/// together, so a host that selects the Groundwork provider gets the same all-or-nothing guarantee. The bare
/// draft (no layout/validation siblings are created on add) reads back through
/// <see cref="GroundworkWorkflowDefinitionDraftStore"/> unchanged.
/// </summary>
public sealed class GroundworkAddWorkflowDefinitionCommand(IDocumentStore store, IPayloadSerializer payloadSerializer)
    : IAddWorkflowDefinitionCommand
{
    public async Task Execute(WorkflowDefinition workflowDefinition, WorkflowDefinitionDraft draft, CancellationToken cancellation)
    {
        var definitionSave = GroundworkDocumentWriter.ToSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            workflowDefinition,
            GroundworkDesignJson.Options);

        var draftSave = GroundworkDocumentWriter.ToSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            draft,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer));

        await store.SaveAllAsync(
            DocumentCommitScope.Of(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind),
            [definitionSave, draftSave],
            cancellation);
    }
}
