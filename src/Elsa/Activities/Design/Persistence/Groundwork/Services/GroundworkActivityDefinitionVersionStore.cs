using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Primitives.Exceptions;
using Elsa.Serialization.Core;
using Groundwork.Documents.Store;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork (document) implementation of <see cref="IActivityDefinitionVersionStore"/>, the document-store
/// counterpart of <c>EFCoreActivityDefinitionVersionStore</c>. It is the most complex rich design aggregate
/// on the document path: its authored projection collections (inputs/outputs/design facets) are serialized
/// via <see cref="IPayloadSerializer"/> (the same serializer the EF handlers use), and the owning definition
/// is fetched with an explicit <b>second read</b> rather than a relational join — exactly what
/// <see cref="IActivityDefinitionVersionStore.GetWithDefinitionAsync"/> models for non-relational providers.
/// Point, compound, and list reads execute only their explicitly admitted Groundwork routes through one
/// access-bound store session.
/// </summary>
public sealed class GroundworkActivityDefinitionVersionStore : IActivityDefinitionVersionStore
{
    private readonly GroundworkNamedQueryAccess<ActivityDefinitionVersion> _reads;
    private readonly IActivityDefinitionStore _definitions;

    public GroundworkActivityDefinitionVersionStore(
        IDocumentStore store,
        IActivityDefinitionStore definitions,
        IPayloadSerializer payloadSerializer,
        IBoundedDocumentStore? boundedStore = null,
        IGroundworkStoreSessionFactory? sessions = null)
    {
        _reads = new GroundworkNamedQueryAccess<ActivityDefinitionVersion>(
            store,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer),
            boundedStore,
            sessions);
        _definitions = definitions;
    }

    public async Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default)
        => await FindByIdAsync(versionId, cancellationToken)
           ?? throw EntityNotFoundException.ForEntity(typeof(ActivityDefinitionVersion), versionId);

    public async Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default)
    {
        var version = await FindByIdAsync(versionId, cancellationToken)
                      ?? throw EntityNotFoundException.ForEntity(typeof(ActivityDefinitionVersion), versionId);

        // Non-relational providers satisfy the owning-definition load with an explicit second aggregate read
        // instead of a join — the document stores no embedded navigation copy.
        version.Definition = await _definitions.GetAsync(version.DefinitionId, cancellationToken);
        return version;
    }

    public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
        => _reads.ExecuteAsync(
            acrossScopes: false,
            (executor, token) => executor.FirstOrDefaultAsync(
                ActivitiesDesignStorageManifest.FindActivityDefinitionVersionByDefinitionAndSortKeyQuery,
                Query<ActivityDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId)
                    .And(x => x.SemVerSortKey, QueryOp.Equal, semVerSortKey),
                token),
            cancellationToken);

    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
        => QueryByDefinitionAsync(
            Query<ActivityDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId),
            cancellationToken);

    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(
        IEnumerable<string> definitionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitionIds);
        var ids = definitionIds.Distinct(StringComparer.Ordinal).ToArray();
        return ids.Length == 0
            ? Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([])
            : QueryByDefinitionAsync(
                Query<ActivityDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.In, ids),
                cancellationToken);
    }

    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default)
        => QueryByDefinitionAsync(
            Query<ActivityDefinitionVersion>.All(),
            cancellationToken);

    private Task<ActivityDefinitionVersion?> FindByIdAsync(
        string versionId,
        CancellationToken cancellationToken) =>
        _reads.ExecuteAsync(
            acrossScopes: false,
            (executor, token) => executor.LoadAsync(versionId, token),
            cancellationToken);

    private Task<IReadOnlyList<ActivityDefinitionVersion>> QueryByDefinitionAsync(
        Query<ActivityDefinitionVersion> query,
        CancellationToken cancellationToken) =>
        _reads.ExecuteAsync(
            acrossScopes: false,
            (executor, token) => executor.QueryAsync(
                ActivitiesDesignStorageManifest.ListActivityDefinitionVersionsByDefinitionQuery,
                query,
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionOrder,
                token),
            cancellationToken);
}
