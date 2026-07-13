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
public sealed class GroundworkWorkflowTriggerBindingStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind), IWorkflowTriggerBindingStore
{
    private const string ProjectionKind = "triggerBindings";

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
        var result = await Store.QueryAsync(new PortableDocumentQuery(DocumentKind), cancellationToken);
        return result.Documents
            .Select(envelope => Serializer.Deserialize<WorkflowTriggerBinding>(envelope))
            .Where(binding => StringComparer.Ordinal.Equals(binding.PublicationId, publicationId))
            .ToArray();
    }

    public async ValueTask ActivatePublicationAsync(
        string publicationId,
        string? replacedPublicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        if (replacedPublicationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(replacedPublicationId);

        var candidateState = await LoadProjectionStateAsync(publicationId, cancellationToken);
        if (candidateState is null)
            throw new InvalidOperationException($"Publication '{publicationId}' has no prepared trigger-binding projection.");

        var candidate = await ListByPublicationAsync(publicationId, cancellationToken);
        var replaced = replacedPublicationId is null || StringComparer.Ordinal.Equals(publicationId, replacedPublicationId)
            ? []
            : await ListByPublicationAsync(replacedPublicationId, cancellationToken);
        var updates = candidate.Select(binding => binding with { IsActive = true })
            .Concat(replaced.Select(binding => binding with { IsActive = false }))
            .ToArray();

        var replacedState = replacedPublicationId is null || StringComparer.Ordinal.Equals(publicationId, replacedPublicationId)
            ? null
            : await LoadProjectionStateAsync(replacedPublicationId, cancellationToken);
        await CommitAtomicallyAsync(
            [],
            updates,
            candidateState with { IsActive = true },
            deleteProjectionStateId: null,
            cancellationToken,
            replacedState is null ? null : replacedState with { IsActive = false });
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

        // The cross-artifact index is keyed by stimulus hash only (every provider supports single-field
        // equality). Post-filter by stimulus type in code so a hash shared across two stimulus types can
        // never cross-match; the hash is type-derived in practice so this is a defensive narrowing.
        var bindings = await QueryDocumentsAsync<WorkflowTriggerBinding, WorkflowTriggerBinding>(
            ElsaRuntimeStorageManifest.WorkflowTriggerBindingByStimulus, stimulusHash, binding => binding, cancellationToken);

        return bindings
            .Where(binding =>
                binding.IsActive &&
                StringComparer.Ordinal.Equals(binding.StimulusType, stimulusType))
            .ToArray();
    }

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        return await QueryDocumentsAsync<WorkflowTriggerBinding, WorkflowTriggerBinding>(
            ElsaRuntimeStorageManifest.WorkflowTriggerBindingByArtifact, artifactId, binding => binding, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByStimulusTypeAsync(string stimulusType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);

        // No stimulus-type index exists (the cross-artifact index is hash-keyed); a full type-scoped scan is
        // acceptable because this feeds the startup/refresh route-table rebuild, not a per-request path. A
        // clause-free PortableDocumentQuery matches every document of this kind, and we narrow to the requested
        // stimulus type in code — the same defensive filter ListByStimulusAsync applies. No new index is added,
        // so the persisted document shape and SchemaVersion are unchanged.
        var result = await Store.QueryAsync(new PortableDocumentQuery(DocumentKind), cancellationToken);

        return result.Documents
            .Select(envelope => Serializer.Deserialize<WorkflowTriggerBinding>(envelope))
            .Where(binding => binding.IsActive && StringComparer.Ordinal.Equals(binding.StimulusType, stimulusType))
            .ToArray();
    }

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

    private async ValueTask<PublicationProjectionState?> LoadProjectionStateAsync(
        string publicationId,
        CancellationToken cancellationToken)
    {
        var envelope = await Store.LoadAsync(
            ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
            ProjectionStateId(publicationId),
            cancellationToken);
        return envelope is null ? null : Serializer.Deserialize<PublicationProjectionState>(envelope);
    }

    private async ValueTask CommitAtomicallyAsync(
        IEnumerable<string> deleteIds,
        IEnumerable<WorkflowTriggerBinding> upserts,
        PublicationProjectionState? projectionState,
        string? deleteProjectionStateId,
        CancellationToken cancellationToken,
        PublicationProjectionState? secondaryProjectionState = null)
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
            await SaveProjectionStateAsync(unitOfWork, projectionState, cancellationToken);
        if (secondaryProjectionState is not null)
            await SaveProjectionStateAsync(unitOfWork, secondaryProjectionState, cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);
    }

    private async ValueTask SaveProjectionStateAsync(
        IDocumentUnitOfWork unitOfWork,
        PublicationProjectionState state,
        CancellationToken cancellationToken)
    {
        var (schemaVersion, content) = Serializer.Serialize(
            ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
            state);
        await unitOfWork.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind,
                ProjectionStateId(state.PublicationId),
                schemaVersion,
                content),
            cancellationToken);
    }

    private static string ProjectionStateId(string publicationId) =>
        $"{ProjectionKind}:{publicationId.Length}:{publicationId}";

    private sealed record PublicationProjectionState(string ProjectionKind, string PublicationId, bool IsActive);
}
