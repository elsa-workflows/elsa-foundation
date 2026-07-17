using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IWorkflowTriggerBindingStore"/>. Like the other bridge stores it depends
/// only on the provider-neutral <see cref="IDocumentStore"/>; the concrete provider (SQLite, SQL Server,
/// PostgreSQL, MongoDB) is chosen by the host through feature composition and never leaks into this bridge
/// or into runtime domain code.
/// </summary>
public sealed class GroundworkWorkflowTriggerBindingStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind), IWorkflowTriggerBindingStore
{
    private const string ProjectionKind = "triggerBindings";
    private readonly IBoundedDocumentStore? _queries = boundedStore ?? store as IBoundedDocumentStore;

    private IBoundedDocumentStore Queries => _queries ?? throw new InvalidOperationException(
        "Workflow trigger-binding queries require an admitted bounded document-store runtime.");

    public async ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.TriggerBindingId);

        var existing = await Store.LoadAsync(DocumentKind, binding.TriggerBindingId, cancellationToken);
        var result = await SaveDocumentAsync(
            binding.TriggerBindingId,
            binding,
            cancellationToken,
            existing?.Version ?? 0);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return binding;
        if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
        {
            throw new InvalidOperationException(
                $"Groundwork rejected workflow trigger binding '{binding.TriggerBindingId}' with status '{result.Status}'.");
        }

        var winner = await Store.LoadAsync(DocumentKind, binding.TriggerBindingId, cancellationToken);
        if (winner is not null)
        {
            var winnerBinding = Serializer.Deserialize<WorkflowTriggerBinding>(winner);
            if (StringComparer.Ordinal.Equals(
                    Serializer.SerializeForComparison(winnerBinding),
                    Serializer.SerializeForComparison(binding)))
            {
                return binding;
            }
        }

        throw new InvalidOperationException(
            $"Workflow trigger binding '{binding.TriggerBindingId}' changed concurrently and was not overwritten.");
    }

    public async ValueTask PreparePublicationAsync(
        string publicationId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(bindings);
        ValidatePublicationBindings(publicationId, bindings);

        var projectionStateEnvelope = await LoadProjectionStateEnvelopeAsync(publicationId, cancellationToken);
        var existing = await ListByPublicationAsync(publicationId, cancellationToken);
        var prepared = bindings.Select(binding => binding with { IsActive = false }).ToArray();
        if (projectionStateEnvelope is not null)
        {
            var projectionState = Serializer.Deserialize<GroundworkPublicationProjectionState>(projectionStateEnvelope);
            if (!projectionState.IsActive && ProjectionsEqual(existing, prepared))
                return;

            throw new InvalidOperationException(
                $"Trigger-binding publication projection '{publicationId}' is already prepared with different state.");
        }

        await CommitAtomicallyAsync(
            existing.Select(binding => binding.TriggerBindingId),
            prepared,
            new GroundworkPublicationProjectionState(ProjectionKind, publicationId, IsActive: false),
            deleteProjectionStateId: null,
            cancellationToken,
            projectionStateExpectedVersion: 0);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByPublicationAsync(
        string publicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        return await QueryBindingsAsync(
            ElsaRuntimeStorageManifest.ListTriggerBindingsByPublicationQuery,
            [Equal(ElsaRuntimeStorageManifest.PublicationIdField, publicationId)],
            cancellationToken);
    }

    public async ValueTask ActivatePublicationAsync(
        string publicationId,
        string? replacedPublicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        if (replacedPublicationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(replacedPublicationId);

        var candidateStateEnvelope = await LoadProjectionStateEnvelopeAsync(publicationId, cancellationToken);
        if (candidateStateEnvelope is null)
            throw new InvalidOperationException($"Publication '{publicationId}' has no prepared trigger-binding projection.");

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

        var candidate = await ListByPublicationAsync(publicationId, cancellationToken);
        var replaced = !hasDistinctReplacement
            ? []
            : await ListByPublicationAsync(replacedPublicationId!, cancellationToken);
        var updates = candidate.Select(binding => binding with { IsActive = true })
            .Concat(replaced.Select(binding => binding with { IsActive = false }))
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

    public async ValueTask DeleteByPublicationAsync(string publicationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        var existing = await ListByPublicationAsync(publicationId, cancellationToken);
        await CommitAtomicallyAsync(
            existing.Select(binding => binding.TriggerBindingId),
            [],
            projectionState: null,
            ProjectionStateId(publicationId),
            cancellationToken);
    }

    public async ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var existing = await ListByArtifactAsync(artifactId, cancellationToken);
        var deleted = 0;

        foreach (var binding in existing)
        {
            var result = await DeleteDocumentAsync(binding.TriggerBindingId, cancellationToken);

            if (result.Status == DocumentStoreWriteStatus.Deleted)
                deleted++;
        }

        return deleted;
    }

    public async ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(
        WorkflowTriggerBindingPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await Queries.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusAndTypeQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.StimulusHashField, query.StimulusHash),
                    Equal(ElsaRuntimeStorageManifest.StimulusTypeField, query.StimulusType),
                    Equal(
                        ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField,
                        bool.TrueString.ToLowerInvariant())
                ],
                take: query.Limit,
                continuation: query.ContinuationToken),
            cancellationToken);

        return new WorkflowTriggerBindingPage(
            query,
            result.Documents.Select(Serializer.Deserialize<WorkflowTriggerBinding>).ToArray(),
            result.TotalCount,
            result.NextContinuation);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        return await QueryBindingsAsync(
            ElsaRuntimeStorageManifest.ListTriggerBindingsByArtifactQuery,
            [Equal(ElsaRuntimeStorageManifest.ArtifactIdField, artifactId)],
            cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByStimulusTypeAsync(string stimulusType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);

        var bindings = await QueryBindingsAsync(
            ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusTypeQuery,
            [Equal(ElsaRuntimeStorageManifest.StimulusTypeField, stimulusType)],
            cancellationToken);
        return bindings.Where(binding => binding.IsActive).ToArray();
    }

    private async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> QueryBindingsAsync(
        string queryIdentity,
        IReadOnlyList<DocumentQueryClause> clauses,
        CancellationToken cancellationToken)
    {
        var result = await Queries.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                queryIdentity,
                clauses),
            cancellationToken);
        return result.Documents.Select(Serializer.Deserialize<WorkflowTriggerBinding>).ToArray();
    }

    private static DocumentQueryClause Equal(string fieldPath, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(fieldPath, value));

    private static void ValidatePublicationBindings(
        string publicationId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings)
    {
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (!StringComparer.Ordinal.Equals(binding.PublicationId, publicationId))
                throw new ArgumentException($"Binding '{binding.TriggerBindingId}' does not belong to publication '{publicationId}'.", nameof(bindings));
            ArgumentException.ThrowIfNullOrWhiteSpace(binding.SlotId);
        }
    }

    private async ValueTask<DocumentEnvelope?> LoadProjectionStateEnvelopeAsync(
        string publicationId,
        CancellationToken cancellationToken) =>
        await Store.LoadAsync(
            ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
            ProjectionStateId(publicationId),
            cancellationToken);

    private async ValueTask CommitAtomicallyAsync(
        IEnumerable<string> deleteIds,
        IEnumerable<WorkflowTriggerBinding> upserts,
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
        foreach (var binding in upserts)
        {
            var (schemaVersion, content) = Serializer.Serialize(DocumentKind, binding);
            await unitOfWork.SaveAsync(
                new SaveDocumentRequest(DocumentKind, binding.TriggerBindingId, schemaVersion, content),
                cancellationToken);
        }
        if (projectionState is not null)
            await SaveProjectionStateAsync(unitOfWork, projectionState, projectionStateExpectedVersion, cancellationToken);
        if (secondaryProjectionState is not null)
            await SaveProjectionStateAsync(unitOfWork, secondaryProjectionState, secondaryProjectionStateExpectedVersion, cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);
    }

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
                ProjectionStateId(state.PublicationId),
                schemaVersion,
                content,
                ExpectedVersion: expectedVersion),
            cancellationToken);
        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw new InvalidOperationException(
                $"Trigger-binding publication projection '{state.PublicationId}' could not be saved because the stored projection version changed.");
    }

    private static string ProjectionStateId(string publicationId) =>
        $"{ProjectionKind}:{publicationId.Length}:{publicationId}";

    private bool ProjectionsEqual(
        IEnumerable<WorkflowTriggerBinding> existing,
        IEnumerable<WorkflowTriggerBinding> prepared)
    {
        var existingById = existing.ToDictionary(binding => binding.TriggerBindingId, StringComparer.Ordinal);
        var preparedById = prepared.ToDictionary(binding => binding.TriggerBindingId, StringComparer.Ordinal);
        if (existingById.Count != preparedById.Count)
            return false;

        foreach (var (id, expected) in preparedById)
        {
            if (!existingById.TryGetValue(id, out var actual))
                return false;

            var (_, actualJson) = Serializer.Serialize(DocumentKind, actual with { IsActive = false });
            var (_, expectedJson) = Serializer.Serialize(DocumentKind, expected);
            if (!StringComparer.Ordinal.Equals(actualJson, expectedJson))
                return false;
        }

        return true;
    }
}
