using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IWorkflowExecutableStore"/>. Like the bookmark bridge it depends only
/// on the provider-neutral <see cref="IDocumentStore"/>; the host selects the concrete provider through
/// feature composition. The executable is stored inside a thin document that stamps a constant
/// collection value, which lets <see cref="ListAsync"/> be served through the same
/// declared-index equality query every provider supports.
/// </summary>
public sealed class GroundworkWorkflowExecutableStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer) : IWorkflowExecutableStore
{
    public async ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable.Identity.ArtifactId);

        await SaveCoreAsync(executable, cancellationToken);
    }

    public async ValueTask<bool> SoftDeleteAsync(string artifactId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default)
    {
        var executable = await FindAsync(artifactId, cancellationToken, includeDeleted: true);
        if (executable is null)
            return false;

        await SaveCoreAsync(executable.WithDeleted(deletedAt, reason), cancellationToken);
        return true;
    }

    public async ValueTask<bool> RestoreAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        var executable = await FindAsync(artifactId, cancellationToken, includeDeleted: true);
        if (executable is null)
            return false;

        await SaveCoreAsync(executable.WithRestored(), cancellationToken);
        return true;
    }

    public async ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var result = await store.DeleteAsync(
            new DeleteDocumentRequest(
                ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind,
                artifactId),
            cancellationToken);

        return result.Status == DocumentStoreWriteStatus.Deleted;
    }

    public async ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default, bool includeDeleted = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind,
            artifactId,
            cancellationToken);

        var executable = envelope is null ? null : Map(envelope);
        return executable?.DeletedAt is null || includeDeleted ? executable : null;
    }

    public async ValueTask<IReadOnlyCollection<WorkflowExecutable>> ListAsync(
        bool includeTransient = false,
        CancellationToken cancellationToken = default,
        bool includeDeleted = false)
    {
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind,
                ElsaRuntimeStorageManifest.WorkflowExecutableByCollection,
                ElsaRuntimeStorageManifest.WorkflowExecutableCollection),
            cancellationToken);

        return envelopes.Select(Map)
            .Where(executable => includeTransient || executable.Scope == WorkflowExecutableScope.Published)
            .Where(executable => includeDeleted || executable.DeletedAt is null)
            .ToArray();
    }

    private async ValueTask SaveCoreAsync(WorkflowExecutable executable, CancellationToken cancellationToken)
    {
        var document = new ExecutableDocument(ElsaRuntimeStorageManifest.WorkflowExecutableCollection, executable);
        var (schemaVersion, content) = serializer.Serialize(ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind, document);

        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind,
                executable.Identity.ArtifactId,
                schemaVersion,
                content),
            cancellationToken);
    }

    private WorkflowExecutable Map(DocumentEnvelope envelope) =>
        serializer.Deserialize<ExecutableDocument>(envelope).Executable;

    // Persistence envelope: stamps the constant collection partition used by ListAsync and carries the
    // provider-neutral executable payload.
    private sealed record ExecutableDocument(string Collection, WorkflowExecutable Executable);
}
