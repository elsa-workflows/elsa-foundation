using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Records the bookmarks it is notified about, shared by the create/consume observer tests (spec 089 D T005).</summary>
internal sealed class RecordingBookmarkLifecycleObserver : IBookmarkLifecycleObserver
{
    public List<BookmarkState> Created { get; } = [];
    public List<BookmarkState> Consumed { get; } = [];

    public ValueTask OnBookmarkCreatedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default)
    {
        Created.Add(bookmark);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnBookmarkConsumedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default)
    {
        Consumed.Add(bookmark);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Throws from both callbacks — pins that a run-path observer throw is swallowed and never faults the run.</summary>
internal sealed class ThrowingBookmarkLifecycleObserver : IBookmarkLifecycleObserver
{
    public ValueTask OnBookmarkCreatedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("observer boom (created)");

    public ValueTask OnBookmarkConsumedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("observer boom (consumed)");
}
