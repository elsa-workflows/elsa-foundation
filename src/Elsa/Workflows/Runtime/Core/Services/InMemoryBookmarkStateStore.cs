using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

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

    public ValueTask<IReadOnlyCollection<BookmarkState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var states = _states
                .Where(item => item.Key.WorkflowExecutionId == workflowExecutionId)
                .Select(item => item.Value)
                .ToArray();

            return new ValueTask<IReadOnlyCollection<BookmarkState>>(states);
        }
    }

    public ValueTask<IReadOnlyCollection<BookmarkState>> ListByStimulusAsync(string stimulusType, string stimulusHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var states = _states.Values
                .Where(state =>
                    StringComparer.Ordinal.Equals(state.StimulusType, stimulusType) &&
                    StringComparer.Ordinal.Equals(state.StimulusHash, stimulusHash))
                .ToArray();

            return new ValueTask<IReadOnlyCollection<BookmarkState>>(states);
        }
    }

    private readonly record struct BookmarkStateKey(string WorkflowExecutionId, string BookmarkId);
}
