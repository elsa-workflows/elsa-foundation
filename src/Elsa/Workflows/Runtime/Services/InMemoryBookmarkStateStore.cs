using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryBookmarkStateStore : IBookmarkStateStore, IBookmarkStimulusIndex
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<BookmarkStateKey, BookmarkState> _states = new();

    public ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.BookmarkId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = new BookmarkStateKey(state.WorkflowExecutionId, state.BookmarkId);
            _states[key] = state;
            return new ValueTask<BookmarkState>(state);
        }
    }

    public ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return new ValueTask<bool>(_states.Remove(new BookmarkStateKey(workflowExecutionId, bookmarkId)));
        }
    }

    public ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _states.TryGetValue(new BookmarkStateKey(workflowExecutionId, bookmarkId), out var state);
            return new ValueTask<BookmarkState?>(state);
        }
    }

    public ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(
        BookmarkStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var states = _states
                .Where(item => item.Key.WorkflowExecutionId == query.WorkflowExecutionId)
                .OrderBy(item => item.Key.BookmarkId, StringComparer.Ordinal)
                .Select(item => item.Value)
                .ToArray();
            var queryBinding = $"bookmark-state:workflow:{query.WorkflowExecutionId}";
            var lastBookmarkId = InMemoryRuntimeStoreContinuation.Decode(
                query.ContinuationToken,
                queryBinding,
                nameof(query));
            var remaining = lastBookmarkId is null
                ? states
                : states.Where(state => StringComparer.Ordinal.Compare(state.BookmarkId, lastBookmarkId) > 0).ToArray();
            var items = remaining.Take(query.Limit).ToArray();
            var nextContinuationToken = remaining.Length > items.Length
                ? InMemoryRuntimeStoreContinuation.Encode(queryBinding, items[^1].BookmarkId)
                : null;

            return new ValueTask<RuntimeStorePage<BookmarkState>>(
                new RuntimeStorePage<BookmarkState>(query, items, nextContinuationToken));
        }
    }

    public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusPageAsync(
        BookmarkStimulusPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return new ValueTask<RuntimeStorePage<BookmarkState>>(PageStimulus(
                query,
                $"bookmark-state:stimulus:{query.StimulusType}:{query.StimulusHash}",
                state =>
                    StringComparer.Ordinal.Equals(state.StimulusType, query.StimulusType) &&
                    StringComparer.Ordinal.Equals(state.StimulusHash, query.StimulusHash)));
        }
    }

    public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusTypePageAsync(
        BookmarkStimulusTypePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return new ValueTask<RuntimeStorePage<BookmarkState>>(PageStimulus(
                query,
                $"bookmark-state:stimulus-type:{query.StimulusType}",
                state => StringComparer.Ordinal.Equals(state.StimulusType, query.StimulusType)));
        }
    }

    private RuntimeStorePage<BookmarkState> PageStimulus(
        RuntimeStorePageRequest query,
        string queryBinding,
        Func<BookmarkState, bool> predicate)
    {
        var cursor = DecodeStimulusCursor(
            InMemoryRuntimeStoreContinuation.Decode(
                query.ContinuationToken,
                queryBinding,
                nameof(query)));
        var remaining = _states.Values
            .Where(predicate)
            .OrderBy(state => state.WorkflowExecutionId, StringComparer.Ordinal)
            .ThenBy(state => state.BookmarkId, StringComparer.Ordinal)
            .Where(state => cursor is null || CompareIdentity(state, cursor) > 0)
            .Take(query.Limit + 1)
            .ToArray();
        var items = remaining.Take(query.Limit).ToArray();
        var nextContinuationToken = remaining.Length > items.Length
            ? InMemoryRuntimeStoreContinuation.Encode(
                queryBinding,
                EncodeStimulusCursor(items[^1]))
            : null;
        return new RuntimeStorePage<BookmarkState>(query, items, nextContinuationToken);
    }

    private static int CompareIdentity(BookmarkState state, BookmarkStimulusCursor cursor)
    {
        var workflow = StringComparer.Ordinal.Compare(state.WorkflowExecutionId, cursor.WorkflowExecutionId);
        return workflow != 0
            ? workflow
            : StringComparer.Ordinal.Compare(state.BookmarkId, cursor.BookmarkId);
    }

    private static string EncodeStimulusCursor(BookmarkState state) =>
        JsonSerializer.Serialize(new BookmarkStimulusCursor(state.WorkflowExecutionId, state.BookmarkId));

    private static BookmarkStimulusCursor? DecodeStimulusCursor(string? cursor)
    {
        if (cursor is null)
            return null;

        try
        {
            var decoded = JsonSerializer.Deserialize<BookmarkStimulusCursor>(cursor);
            return decoded is null ||
                   string.IsNullOrWhiteSpace(decoded.WorkflowExecutionId) ||
                   string.IsNullOrWhiteSpace(decoded.BookmarkId)
                ? throw new JsonException()
                : decoded;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The bookmark stimulus continuation token is invalid.", nameof(cursor), exception);
        }
    }

    private sealed record BookmarkStimulusCursor(string WorkflowExecutionId, string BookmarkId);
    private readonly record struct BookmarkStateKey(string WorkflowExecutionId, string BookmarkId);
}
