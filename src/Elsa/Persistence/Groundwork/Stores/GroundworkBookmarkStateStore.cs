using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IBookmarkStateStore"/>. It depends only on the provider-neutral
/// <see cref="IDocumentStore"/>; the concrete provider (SQLite, SQL Server, PostgreSQL, MongoDB) is
/// chosen by the host through feature composition and never leaks into this bridge or into runtime
/// domain code.
/// </summary>
public sealed class GroundworkBookmarkStateStore(IDocumentStore store) : IBookmarkStateStore
{
    public async ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.BookmarkId);

        var content = JsonSerializer.Serialize(state, GroundworkRuntimeJson.Options);
        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
                BuildId(state.WorkflowExecutionId, state.BookmarkId),
                ElsaRuntimeStorageManifest.SchemaVersion,
                content),
            cancellationToken);

        return state;
    }

    public async ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);

        var result = await store.DeleteAsync(
            new DeleteDocumentRequest(
                ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
                BuildId(workflowExecutionId, bookmarkId)),
            cancellationToken);

        return result.Status == DocumentStoreWriteStatus.Deleted;
    }

    public async ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);

        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
            BuildId(workflowExecutionId, bookmarkId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IReadOnlyCollection<BookmarkState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
                ElsaRuntimeStorageManifest.BookmarkStateByWorkflowExecution,
                workflowExecutionId),
            cancellationToken);

        return envelopes.Select(Map).ToArray();
    }

    private static BookmarkState Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<BookmarkState>(envelope.ContentJson, GroundworkRuntimeJson.Options)!;

    // Deterministic, collision-free composite document id. Parts are escaped so a separator inside
    // an id cannot forge a different (workflowExecutionId, bookmarkId) pair.
    private static string BuildId(string workflowExecutionId, string bookmarkId) =>
        $"{Escape(workflowExecutionId)}:{Escape(bookmarkId)}";

    private static string Escape(string value) => value.Replace("%", "%25").Replace(":", "%3A");
}
