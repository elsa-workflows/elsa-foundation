using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IBookmarkStateStore"/>. It depends only on the provider-neutral
/// <see cref="IDocumentStore"/>; the concrete provider (SQLite, SQL Server, PostgreSQL, MongoDB) is
/// chosen by the host through feature composition and never leaks into this bridge or into runtime
/// domain code.
/// </summary>
public sealed class GroundworkBookmarkStateStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer) : IBookmarkStateStore, IBookmarkStimulusIndex
{
    public async ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.BookmarkId);

        var (schemaVersion, content) = serializer.Serialize(ElsaRuntimeStorageManifest.BookmarkStateDocumentKind, state);
        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
                BuildId(state.WorkflowExecutionId, state.BookmarkId),
                schemaVersion,
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

    public async ValueTask<IReadOnlyCollection<BookmarkState>> ListByStimulusAsync(string stimulusType, string stimulusHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);

        // The cross-execution index is keyed by stimulus hash only (every provider supports single-field
        // equality). Post-filter by stimulus type in code so a hash shared across two stimulus types can
        // never cross-match; the hash is type-derived in practice so this is a defensive narrowing.
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
                ElsaRuntimeStorageManifest.BookmarkStateByStimulus,
                stimulusHash),
            cancellationToken);

        return envelopes
            .Select(Map)
            .Where(bookmark => StringComparer.Ordinal.Equals(bookmark.StimulusType, stimulusType))
            .ToArray();
    }

    private BookmarkState Map(DocumentEnvelope envelope) =>
        serializer.Deserialize<BookmarkState>(envelope);

    // Deterministic, collision-free composite document id. Parts are escaped so a separator inside
    // an id cannot forge a different (workflowExecutionId, bookmarkId) pair.
    private static string BuildId(string workflowExecutionId, string bookmarkId) =>
        $"{Escape(workflowExecutionId)}:{Escape(bookmarkId)}";

    private static string Escape(string value) => value.Replace("%", "%25").Replace(":", "%3A");
}
