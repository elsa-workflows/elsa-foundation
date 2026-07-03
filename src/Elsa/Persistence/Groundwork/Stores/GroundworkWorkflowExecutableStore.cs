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
public sealed class GroundworkWorkflowExecutableStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind), IWorkflowExecutableStore
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

        var result = await DeleteDocumentAsync(artifactId, cancellationToken);

        return result.Status == DocumentStoreWriteStatus.Deleted;
    }

    public async ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default, bool includeDeleted = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var executable = await LoadDocumentAsync<ExecutableDocument, WorkflowExecutable>(
            artifactId, document => document.Executable, cancellationToken);
        return executable?.DeletedAt is null || includeDeleted ? executable : null;
    }

    public async ValueTask<IReadOnlyCollection<WorkflowExecutable>> ListAsync(
        bool includeTransient = false,
        CancellationToken cancellationToken = default,
        bool includeDeleted = false)
    {
        var executables = await QueryDocumentsAsync<ExecutableDocument, WorkflowExecutable>(
            ElsaRuntimeStorageManifest.WorkflowExecutableByCollection,
            ElsaRuntimeStorageManifest.WorkflowExecutableCollection,
            document => document.Executable,
            cancellationToken);

        return executables
            .Where(executable => includeTransient || executable.Scope == WorkflowExecutableScope.Published)
            .Where(executable => includeDeleted || executable.DeletedAt is null)
            .ToArray();
    }

    private async ValueTask SaveCoreAsync(WorkflowExecutable executable, CancellationToken cancellationToken)
    {
        var document = new ExecutableDocument(ElsaRuntimeStorageManifest.WorkflowExecutableCollection, executable);
        await SaveDocumentAsync(executable.Identity.ArtifactId, document, cancellationToken);
    }

    // Persistence envelope: stamps the constant collection partition used by ListAsync and carries the
    // provider-neutral executable payload.
    private sealed record ExecutableDocument(string Collection, WorkflowExecutable Executable);
}
