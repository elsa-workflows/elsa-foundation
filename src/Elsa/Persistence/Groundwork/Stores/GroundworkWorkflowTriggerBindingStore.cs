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

        await SaveDocumentAsync(binding.TriggerBindingId, binding, cancellationToken);

        return binding;
    }

    public async ValueTask PreparePublicationAsync(
        string publicationId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(bindings);
        ValidatePublicationBindings(publicationId, bindings);

        var existing = await ListByPublicationAsync(publicationId, cancellationToken);
        await CommitAtomicallyAsync(
            existing.Select(binding => binding.TriggerBindingId),
            bindings.Select(binding => binding with { IsActive = false }),
            new PublicationProjectionState(ProjectionKind, publicationId, IsActive: false),
            deleteProjectionStateId: null,
            cancellationToken);
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

        var candidateState = Serializer.Deserialize<PublicationProjectionState>(candidateStateEnvelope);
        var candidate = await ListByPublicationAsync(publicationId, cancellationToken);
        var replaced = replacedPublicationId is null || StringComparer.Ordinal.Equals(publicationId, replacedPublicationId)
            ? []
            : await ListByPublicationAsync(replacedPublicationId, cancellationToken);
        var updates = candidate.Select(binding => binding with { IsActive = true })
            .Concat(replaced.Select(binding => binding with { IsActive = false }))
            .ToArray();

        var replacedStateEnvelope = replacedPublicationId is null || StringComparer.Ordinal.Equals(publicationId, replacedPublicationId)
            ? null
            : await LoadProjectionStateEnvelopeAsync(replacedPublicationId, cancellationToken);
        var replacedState = replacedStateEnvelope is null
            ? null
            : Serializer.Deserialize<PublicationProjectionState>(replacedStateEnvelope);
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

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByStimulusAsync(string stimulusType, string stimulusHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);

        var bindings = await QueryBindingsAsync(
            ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusAndTypeQuery,
            [
                Equal(ElsaRuntimeStorageManifest.StimulusHashField, stimulusHash),
                Equal(ElsaRuntimeStorageManifest.StimulusTypeField, stimulusType)
            ],
            cancellationToken);

        return bindings
            .Where(binding =>
                binding.IsActive &&
                StringComparer.Ordinal.Equals(binding.StimulusType, stimulusType))
            .ToArray();
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
        PublicationProjectionState? projectionState,
        string? deleteProjectionStateId,
        CancellationToken cancellationToken,
        PublicationProjectionState? secondaryProjectionState = null,
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
        PublicationProjectionState state,
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

    private sealed record PublicationProjectionState(string ProjectionKind, string PublicationId, bool IsActive);
}
