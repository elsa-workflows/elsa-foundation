using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IWorkflowTriggerBindingStore"/>. Like the other bridge stores it depends
/// only on the provider-neutral <see cref="IDocumentStore"/>; the concrete provider (SQLite, SQL Server,
/// PostgreSQL, MongoDB) is chosen by the host through feature composition and never leaks into this bridge
/// or into runtime domain code.
/// </summary>
public sealed class GroundworkWorkflowTriggerBindingStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer) : IWorkflowTriggerBindingStore
{
    public async ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.TriggerBindingId);

        var (schemaVersion, content) = serializer.Serialize(ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind, binding);
        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
                binding.TriggerBindingId,
                schemaVersion,
                content),
            cancellationToken);

        return binding;
    }

    public async ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var existing = await ListByArtifactAsync(artifactId, cancellationToken);
        var deleted = 0;

        foreach (var binding in existing)
        {
            var result = await store.DeleteAsync(
                new DeleteDocumentRequest(
                    ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
                    binding.TriggerBindingId),
                cancellationToken);

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
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingByStimulus,
                stimulusHash),
            cancellationToken);

        return envelopes
            .Select(Map)
            .Where(binding => StringComparer.Ordinal.Equals(binding.StimulusType, stimulusType))
            .ToArray();
    }

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingByArtifact,
                artifactId),
            cancellationToken);

        return envelopes.Select(Map).ToArray();
    }

    private WorkflowTriggerBinding Map(DocumentEnvelope envelope) =>
        serializer.Deserialize<WorkflowTriggerBinding>(envelope);
}
