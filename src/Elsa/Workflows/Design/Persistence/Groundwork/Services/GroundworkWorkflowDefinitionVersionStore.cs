using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Primitives.Exceptions;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork (document) implementation of <see cref="IWorkflowDefinitionVersionStore"/>, the document-store
/// implementation of the version-store contract. It translates each closed
/// <see cref="Query{TEntity}"/> shape to an explicitly admitted Groundwork route and executes it through one
/// access-bound store session.
/// <para>
/// This is the first <b>rich</b> design aggregate on the document path: its authored
/// <c>WorkflowDefinitionState</c> is serialized via <see cref="IPayloadSerializer"/> (the same serializer
/// the EF handlers use), and the owning definition is fetched with an explicit <b>second read</b> rather than
/// a relational join — exactly what <see cref="IWorkflowDefinitionVersionStore.GetWithDefinitionAsync"/>
/// models for non-relational providers.
/// </para>
/// </summary>
public sealed class GroundworkWorkflowDefinitionVersionStore : IWorkflowDefinitionVersionStore
{
    private readonly GroundworkNamedQueryAccess<WorkflowDefinitionVersion> _reads;
    private readonly IWorkflowDefinitionStore _definitions;

    public GroundworkWorkflowDefinitionVersionStore(
        IDocumentStore store,
        IWorkflowDefinitionStore definitions,
        IPayloadSerializer payloadSerializer,
        IBoundedDocumentStore? boundedStore = null,
        IGroundworkStoreSessionFactory? sessions = null)
    {
        _reads = new GroundworkNamedQueryAccess<WorkflowDefinitionVersion>(
            store,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer),
            boundedStore,
            sessions,
            DesignPersistenceDomain.Workflow,
            "workflow definition version");
        _definitions = definitions;
    }

    public async Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default)
        => await FindByIdAsync(versionId, cancellationToken)
           ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionVersion), versionId);

    public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default)
        => _reads.ExecuteAsync(
            acrossScopes: false,
            (executor, token) => executor.LoadAsync(versionId, token),
            cancellationToken);

    public async Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default)
    {
        var version = await FindByIdAsync(versionId, cancellationToken)
                      ?? throw new ArgumentException($"Workflow definition version with id '{versionId}' does not exist");

        // Non-relational providers satisfy the owning-definition load with an explicit second aggregate read
        // instead of a join — the document stores no embedded navigation copy.
        version.Definition = await _definitions.GetAsync(version.DefinitionId, cancellationToken);
        return version;
    }

    public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default)
        => _reads.ExecuteAsync(
            acrossScopes: false,
            (executor, token) => executor.FirstOrDefaultAsync(
                WorkflowsDesignStorageManifest.FindLatestVersionQuery,
                Query<WorkflowDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId)
                    .OrderByDescending(x => x.SemVerSortKey),
                WorkflowsDesignStorageManifest.WorkflowDefinitionLatestVersionOrder,
                token),
            cancellationToken);

    public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
        => _reads.ExecuteAsync(
            acrossScopes: false,
            (executor, token) => executor.QueryAsync(
                WorkflowsDesignStorageManifest.ListVersionsByDefinitionQuery,
                Query<WorkflowDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId),
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionOrder,
                token),
            cancellationToken);

    public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
        => _reads.ExecuteAsync(
            acrossScopes: false,
            (executor, token) => executor.AnyAsync(
                WorkflowsDesignStorageManifest.FindVersionByDefinitionAndSortKeyQuery,
                Query<WorkflowDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId)
                    .And(x => x.SemVerSortKey, QueryOp.Equal, semVerSortKey),
                token),
            cancellationToken);
}
