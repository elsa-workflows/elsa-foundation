using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Groundwork.Querying;
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
/// </summary>
public sealed class GroundworkActivityDefinitionVersionStore : IActivityDefinitionVersionStore
{
    private readonly GroundworkReadStore<ActivityDefinitionVersion> _reads;
    private readonly IActivityDefinitionStore _definitions;

    public GroundworkActivityDefinitionVersionStore(
        IDocumentStore store,
        IActivityDefinitionStore definitions,
        IPayloadSerializer payloadSerializer,
        IBoundedDocumentStore? boundedStore = null)
    {
        _reads = new GroundworkReadStore<ActivityDefinitionVersion>(
            store,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            ActivitiesDesignStorageManifest.ListAllQuery,
            ActivitiesDesignStorageManifest.CollectionField,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionCollection,
            GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer),
            boundedStore,
            collectionOrder: ActivitiesDesignStorageManifest.DeterministicDocumentOrder);
        _definitions = definitions;
    }

    public async Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default)
        => await _reads.FirstOrDefaultAsync(ById(versionId), cancellationToken)
           ?? throw EntityNotFoundException.ForEntity(typeof(ActivityDefinitionVersion), versionId);

    public async Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default)
    {
        var version = await _reads.FirstOrDefaultAsync(ById(versionId), cancellationToken)
                      ?? throw EntityNotFoundException.ForEntity(typeof(ActivityDefinitionVersion), versionId);

        // Non-relational providers satisfy the owning-definition load with an explicit second aggregate read
        // instead of a join — the document stores no embedded navigation copy.
        version.Definition = await _definitions.GetAsync(version.DefinitionId, cancellationToken);
        return version;
    }

    public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
        => _reads.FirstOrDefaultAsync(
            Query<ActivityDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId)
                .And(x => x.SemVerSortKey, QueryOp.Equal, semVerSortKey),
            cancellationToken);

    public async Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
        => await _reads.QueryAsync(
            Query<ActivityDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId),
            cancellationToken);

    public async Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default)
        => await _reads.QueryAsync(
            Query<ActivityDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.In, definitionIds),
            cancellationToken);

    public async Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default)
        => await _reads.QueryAsync(Query<ActivityDefinitionVersion>.All(), cancellationToken);

    private static Query<ActivityDefinitionVersion> ById(string versionId)
        => Query<ActivityDefinitionVersion>.Where(x => x.Id, QueryOp.Equal, versionId);
}
