using Elsa.Persistence.Groundwork.Serialization;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Shared publication-projection lifecycle for Groundwork stores whose documents are projected per
/// publication and flipped active in one atomic transition (trigger bindings, recurring schedules):
/// idempotent prepare, candidate/replacement activation through
/// <see cref="GroundworkPublicationProjectionTransition"/>, per-publication delete, and the atomic
/// commit that keeps items and projection-state documents in one unit of work.
/// </summary>
public abstract class GroundworkPublicationProjectionStore<TItem>(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    string documentKind,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, documentKind, boundedStore)
    where TItem : notnull
{
    /// <summary>Discriminator segment of the projection-state document id (e.g. <c>triggerBindings</c>).</summary>
    protected abstract string ProjectionKind { get; }

    /// <summary>Human-readable item family used in error messages (e.g. <c>trigger-binding</c>).</summary>
    protected abstract string ProjectionNoun { get; }

    protected abstract string ItemId(TItem item);

    protected abstract TItem WithActive(TItem item, bool isActive);

    /// <summary>The object actually serialized for storage: the item itself, or its storage envelope.</summary>
    protected abstract object StoragePayload(TItem item);

    protected abstract ValueTask<IReadOnlyCollection<TItem>> ListAllByPublicationCoreAsync(
        string publicationId,
        CancellationToken cancellationToken);

    protected async ValueTask PreparePublicationCoreAsync(
        string publicationId,
        IReadOnlyCollection<TItem> items,
        CancellationToken cancellationToken)
    {
        var projectionStateEnvelope = await LoadProjectionStateEnvelopeAsync(publicationId, cancellationToken);
        var existing = await ListAllByPublicationCoreAsync(publicationId, cancellationToken);
        var prepared = items.Select(item => WithActive(item, false)).ToArray();
        if (projectionStateEnvelope is not null)
        {
            var projectionState = Serializer.Deserialize<GroundworkPublicationProjectionState>(projectionStateEnvelope);
            if (!projectionState.IsActive && ProjectionsEqual(existing, prepared))
                return;

            throw new InvalidOperationException(
                $"{ProjectionNounSentenceStart} publication projection '{publicationId}' is already prepared with different state.");
        }

        await CommitAtomicallyAsync(
            existing.Select(ItemId),
            prepared,
            new GroundworkPublicationProjectionState(ProjectionKind, publicationId, IsActive: false),
            deleteProjectionStateId: null,
            cancellationToken,
            projectionStateExpectedVersion: 0);
    }

    protected async ValueTask ActivatePublicationCoreAsync(
        string publicationId,
        string? replacedPublicationId,
        CancellationToken cancellationToken)
    {
        var candidateStateEnvelope = await LoadProjectionStateEnvelopeAsync(publicationId, cancellationToken);
        if (candidateStateEnvelope is null)
            throw new InvalidOperationException($"Publication '{publicationId}' has no prepared {ProjectionNoun} projection.");

        var candidateState = Serializer.Deserialize<GroundworkPublicationProjectionState>(candidateStateEnvelope);
        var hasDistinctReplacement = replacedPublicationId is not null &&
            !StringComparer.Ordinal.Equals(publicationId, replacedPublicationId);
        var replacedStateEnvelope = !hasDistinctReplacement
            ? null
            : await LoadProjectionStateEnvelopeAsync(replacedPublicationId!, cancellationToken);
        var replacedState = replacedStateEnvelope is null
            ? null
            : Serializer.Deserialize<GroundworkPublicationProjectionState>(replacedStateEnvelope);
        if (GroundworkPublicationProjectionTransition.IsAlreadyActivated(
                candidateState,
                replacedState,
                hasDistinctReplacement))
        {
            return;
        }

        GroundworkPublicationProjectionTransition.EnsureCanActivate(
            candidateState,
            replacedState,
            hasDistinctReplacement);

        var candidate = await ListAllByPublicationCoreAsync(publicationId, cancellationToken);
        var replaced = !hasDistinctReplacement
            ? []
            : await ListAllByPublicationCoreAsync(replacedPublicationId!, cancellationToken);
        var updates = candidate.Select(item => WithActive(item, true))
            .Concat(replaced.Select(item => WithActive(item, false)))
            .ToArray();
        await CommitAtomicallyAsync(
            [],
            updates,
            candidateState with { IsActive = true },
            deleteProjectionStateId: null,
            cancellationToken,
            secondaryProjectionState: replacedState is null ? null : replacedState with { IsActive = false },
            projectionStateExpectedVersion: candidateStateEnvelope.Version,
            secondaryProjectionStateExpectedVersion: replacedStateEnvelope?.Version);
    }

    protected async ValueTask DeleteByPublicationCoreAsync(string publicationId, CancellationToken cancellationToken)
    {
        var existing = await ListAllByPublicationCoreAsync(publicationId, cancellationToken);
        await CommitAtomicallyAsync(
            existing.Select(ItemId),
            [],
            projectionState: null,
            ProjectionStateId(publicationId),
            cancellationToken);
    }

    private async ValueTask CommitAtomicallyAsync(
        IEnumerable<string> deleteIds,
        IEnumerable<TItem> upserts,
        GroundworkPublicationProjectionState? projectionState,
        string? deleteProjectionStateId,
        CancellationToken cancellationToken,
        GroundworkPublicationProjectionState? secondaryProjectionState = null,
        long? projectionStateExpectedVersion = null,
        long? secondaryProjectionStateExpectedVersion = null)
    {
        await using var unitOfWork = await Store.BeginAsync(
            DocumentCommitScope.Of(DocumentKind, ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind),
            cancellationToken);
        foreach (var id in deleteIds)
            await unitOfWork.DeleteAsync(new DeleteDocumentRequest(DocumentKind, id), cancellationToken);
        if (deleteProjectionStateId is not null)
            await unitOfWork.DeleteAsync(
                new DeleteDocumentRequest(ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind, deleteProjectionStateId),
                cancellationToken);
        foreach (var item in upserts)
        {
            var (schemaVersion, content) = Serializer.Serialize(DocumentKind, StoragePayload(item));
            await unitOfWork.SaveAsync(
                new SaveDocumentRequest(DocumentKind, ItemId(item), schemaVersion, content),
                cancellationToken);
        }
        if (projectionState is not null)
            await SaveProjectionStateAsync(unitOfWork, projectionState, projectionStateExpectedVersion, cancellationToken);
        if (secondaryProjectionState is not null)
            await SaveProjectionStateAsync(unitOfWork, secondaryProjectionState, secondaryProjectionStateExpectedVersion, cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);
    }

    private async ValueTask<DocumentEnvelope?> LoadProjectionStateEnvelopeAsync(
        string publicationId,
        CancellationToken cancellationToken) =>
        await Store.LoadAsync(
            ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
            ProjectionStateId(publicationId),
            cancellationToken);

    private string ProjectionStateId(string publicationId) =>
        $"{ProjectionKind}:{publicationId.Length}:{publicationId}";

    private async ValueTask SaveProjectionStateAsync(
        IDocumentUnitOfWork unitOfWork,
        GroundworkPublicationProjectionState state,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        var (schemaVersion, content) = Serializer.Serialize(
            ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
            state);
        var result = await unitOfWork.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
                ProjectionStateId(state.ActivationId),
                schemaVersion,
                content,
                ExpectedVersion: expectedVersion),
            cancellationToken);
        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw new InvalidOperationException(
                $"{ProjectionNounSentenceStart} publication projection '{state.ActivationId}' could not be saved because the stored projection version changed.");
    }

    private bool ProjectionsEqual(IEnumerable<TItem> existing, IEnumerable<TItem> prepared)
    {
        var existingById = existing.ToDictionary(ItemId, StringComparer.Ordinal);
        var preparedById = prepared.ToDictionary(ItemId, StringComparer.Ordinal);
        if (existingById.Count != preparedById.Count)
            return false;

        foreach (var (id, expected) in preparedById)
        {
            if (!existingById.TryGetValue(id, out var actual))
                return false;

            var (_, actualJson) = Serializer.Serialize(DocumentKind, StoragePayload(WithActive(actual, false)));
            var (_, expectedJson) = Serializer.Serialize(DocumentKind, StoragePayload(expected));
            if (!StringComparer.Ordinal.Equals(actualJson, expectedJson))
                return false;
        }

        return true;
    }

    private string ProjectionNounSentenceStart => char.ToUpperInvariant(ProjectionNoun[0]) + ProjectionNoun[1..];
}
