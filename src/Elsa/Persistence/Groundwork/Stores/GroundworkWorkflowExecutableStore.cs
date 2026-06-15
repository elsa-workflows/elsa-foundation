using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IWorkflowExecutableStore"/>. Like the bookmark bridge it depends only
/// on the provider-neutral <see cref="IDocumentStore"/>; the host selects the concrete provider through
/// feature composition. The executable is stored inside a thin document that stamps a constant
/// collection value, which lets the unfiltered <see cref="ListAsync"/> be served through the same
/// declared-index equality query every provider supports.
/// </summary>
public sealed class GroundworkWorkflowExecutableStore(IDocumentStore store) : IWorkflowExecutableStore
{
    public async ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable.Identity.ArtifactId);

        var document = new ExecutableDocument(ElsaRuntimeStorageManifest.WorkflowExecutableCollection, executable);
        var content = JsonSerializer.Serialize(document, GroundworkRuntimeJson.Options);

        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind,
                executable.Identity.ArtifactId,
                ElsaRuntimeStorageManifest.SchemaVersion,
                content),
            cancellationToken);
    }

    public async ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind,
            artifactId,
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowExecutable>> ListAsync(CancellationToken cancellationToken = default)
    {
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind,
                ElsaRuntimeStorageManifest.WorkflowExecutableByCollection,
                ElsaRuntimeStorageManifest.WorkflowExecutableCollection),
            cancellationToken);

        return envelopes.Select(Map).ToArray();
    }

    private static WorkflowExecutable Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<ExecutableDocument>(envelope.ContentJson, GroundworkRuntimeJson.Options)!.Executable;

    // Persistence envelope: stamps the constant collection partition used by ListAsync and carries the
    // provider-neutral executable payload.
    private sealed record ExecutableDocument(string Collection, WorkflowExecutable Executable);
}
