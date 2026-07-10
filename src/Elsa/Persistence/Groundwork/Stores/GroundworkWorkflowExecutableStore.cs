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
/// declared-index equality query every provider supports. Saving is idempotent by artifact id: an
/// existing (content-addressed, immutable) artifact is authoritative and left untouched (ADR 0038).
/// </summary>
public sealed class GroundworkWorkflowExecutableStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind), IWorkflowExecutableStore
{
    public async ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable.Identity.ArtifactId);

        if (await FindAsync(executable.Identity.ArtifactId, cancellationToken) is not null)
            return;

        var document = new ExecutableDocument(ElsaRuntimeStorageManifest.WorkflowExecutableCollection, executable);
        await SaveDocumentAsync(executable.Identity.ArtifactId, document, cancellationToken);
    }

    public async ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var result = await DeleteDocumentAsync(artifactId, cancellationToken);

        return result.Status == DocumentStoreWriteStatus.Deleted;
    }

    public async ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        return await LoadDocumentAsync<ExecutableDocument, WorkflowExecutable>(
            artifactId, document => document.Executable, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowExecutable>> ListAsync(CancellationToken cancellationToken = default)
    {
        var executables = await QueryDocumentsAsync<ExecutableDocument, WorkflowExecutable>(
            ElsaRuntimeStorageManifest.WorkflowExecutableByCollection,
            ElsaRuntimeStorageManifest.WorkflowExecutableCollection,
            document => document.Executable,
            cancellationToken);

        return executables.ToArray();
    }

    // Persistence envelope: stamps the constant collection partition used by ListAsync and carries the
    // provider-neutral executable payload.
    private sealed record ExecutableDocument(string Collection, WorkflowExecutable Executable);
}
