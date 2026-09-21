using Elsa.Http.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Http.Contracts;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// Keeps the per-shell HTTP <see cref="IRouteTable"/> in step with waiting HttpEndpoint bookmarks as workflow
/// instances suspend on and resume from mid-flow endpoints (spec 089 D, D-D4). Registered as an
/// <see cref="IBookmarkLifecycleObserver"/>, it runs after a bookmark's durable create/consume commit and rebuilds
/// the whole route table from the resolver — so a mid-flow endpoint's route appears when an instance suspends on it
/// and disappears when that instance resumes (and no other instance still awaits it).
/// </summary>
/// <remarks>
/// <para>
/// <b>HTTP bookmarks only.</b> The lifecycle seam fires for every stimulus type; a bookmark whose
/// <see cref="BookmarkState.StimulusType"/> is not <see cref="HttpEndpointRouting.StimulusType"/> cannot affect the
/// HTTP route table, so it returns immediately without opening a scope — the common cheap case.
/// </para>
/// <para>
/// <b>Full refresh, not incremental diff.</b> Like <see cref="RouteTableTriggerIndexObserver"/>, this re-projects
/// the entire route table (trigger bindings ∪ waiting bookmarks) from the resolver rather than adding/removing the
/// single changed template. That is always correct — a consumed bookmark's template must survive if another waiting
/// instance still awaits it, and only a full union from the durable sources knows that — and cheap at this scale.
/// The changed <paramref name="bookmark"/> is therefore not inspected beyond its stimulus type.
/// </para>
/// <para>
/// <b>Lifetime + failure policy.</b> This is a singleton that delegates the read-then-swap to the shared
/// <see cref="IHttpEndpointRouteTableSynchronizer"/>, which serializes every refresh (publish, run, startup) under one
/// lock so a stale read can never clobber a newer swap (spec 089 D review fix). Unlike the trigger-index observer, an
/// exception here MUST NOT fault the run: the <see cref="IBookmarkLifecycleObserver"/> seam fires on the RUN path (the
/// <see cref="BookmarkLifecycleNotifier"/> catches and logs observer throws). A refresh that fails leaves a stale route
/// that deterministically 404s until the next successful refresh — tolerated per the seam's contract.
/// </para>
/// </remarks>
public sealed class RouteTableBookmarkObserver(IHttpEndpointRouteTableSynchronizer synchronizer) : IBookmarkLifecycleObserver
{
    public ValueTask OnBookmarkCreatedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default) =>
        RefreshIfHttpAsync(bookmark, cancellationToken);

    public ValueTask OnBookmarkConsumedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default) =>
        RefreshIfHttpAsync(bookmark, cancellationToken);

    private ValueTask RefreshIfHttpAsync(BookmarkState bookmark, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bookmark);

        if (!StringComparer.Ordinal.Equals(bookmark.StimulusType, HttpEndpointRouting.StimulusType))
            return ValueTask.CompletedTask;

        return synchronizer.RefreshAsync(cancellationToken);
    }
}
