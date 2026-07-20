using System.Text.Json;
using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork (document) implementation of <see cref="IWorkflowDefinitionVersionLayoutStore"/>, the
/// document-store counterpart of <c>EFCoreWorkflowDefinitionVersionLayoutStore</c>. The layout's
/// <c>Records</c> are plain JSON (the relational provider stores them via a System.Text.Json value
/// converter, not the payload serializer), so the document serializes them directly; only the navigation
/// property and the relational identity column are excluded.
/// </summary>
public sealed class GroundworkWorkflowDefinitionVersionLayoutStore : IWorkflowDefinitionVersionLayoutStore
{
    private static readonly JsonSerializerOptions Options =
        GroundworkDocumentSerialization.Create(["RowNumber", "WorkflowDefinitionVersion"]);

    private readonly GroundworkNamedQueryAccess<WorkflowDefinitionVersionLayout> _reads;

    public GroundworkWorkflowDefinitionVersionLayoutStore(
        IDocumentStore store,
        IBoundedDocumentStore? boundedStore = null,
        IGroundworkStoreSessionFactory? sessions = null)
    {
        _reads = new GroundworkNamedQueryAccess<WorkflowDefinitionVersionLayout>(
            store,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
            Options,
            boundedStore,
            sessions,
            DesignPersistenceDomain.Workflow,
            "workflow definition version layout");
    }

    public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(
        string workflowDefinitionVersionId,
        CancellationToken cancellationToken = default) =>
        _reads.ExecuteAsync(
            acrossScopes: false,
            (executor, token) => executor.FirstOrDefaultAsync(
                WorkflowsDesignStorageManifest.FindLayoutByVersionQuery,
                Query<WorkflowDefinitionVersionLayout>.Where(
                    x => x.WorkflowDefinitionVersionId,
                    QueryOp.Equal,
                    workflowDefinitionVersionId),
                token),
            cancellationToken);
}
