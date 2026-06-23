using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork (document) implementation of <see cref="IWorkflowDefinitionVersionStore"/>, the document-store
/// counterpart of <c>EFCoreWorkflowDefinitionVersionStore</c>. Both translate the named read operations into
/// the closed <see cref="Query{TEntity}"/> spec; this one executes it against a Groundwork
/// <see cref="IDocumentStore"/> via <see cref="GroundworkReadStore{TEntity}"/>.
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
    private readonly GroundworkReadStore<WorkflowDefinitionVersion> _reads;
    private readonly IWorkflowDefinitionStore _definitions;

    public GroundworkWorkflowDefinitionVersionStore(
        IDocumentStore store,
        IWorkflowDefinitionStore definitions,
        IPayloadSerializer payloadSerializer)
    {
        _reads = new GroundworkReadStore<WorkflowDefinitionVersion>(
            store,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            WorkflowsDesignStorageManifest.ByCollectionIndex,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer));
        _definitions = definitions;
    }

    public async Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default)
        => await FindByIdAsync(versionId, cancellationToken)
           ?? throw new InvalidOperationException($"Entity '{typeof(WorkflowDefinitionVersion)}' with id '{versionId}' cannot be found");

    public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default)
        => _reads.FirstOrDefaultAsync(ById(versionId), cancellationToken);

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
        => _reads.FirstOrDefaultAsync(
            Query<WorkflowDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId)
                .OrderByDescending(x => x.SemVerSortKey),
            cancellationToken);

    public async Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
        => await _reads.QueryAsync(
            Query<WorkflowDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId),
            cancellationToken);

    public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
        => _reads.AnyAsync(
            Query<WorkflowDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId)
                .And(x => x.SemVerSortKey, QueryOp.Equal, semVerSortKey),
            cancellationToken);

    private static Query<WorkflowDefinitionVersion> ById(string versionId)
        => Query<WorkflowDefinitionVersion>.Where(x => x.Id, QueryOp.Equal, versionId);
}
