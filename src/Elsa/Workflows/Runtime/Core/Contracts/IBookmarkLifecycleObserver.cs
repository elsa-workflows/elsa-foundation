using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// A narrow post-commit notification seam for bookmark lifecycle transitions (spec 089 D). The runtime invokes
/// every registered observer AFTER a bookmark's durable state change has committed: once with the created
/// bookmark when a mid-flow suspension persists it (the bookmark-created checkpoint), and once with the consumed
/// bookmark when a resume deletes it (the bookmark-consumed checkpoint). Observers exist so a projection derived
/// from waiting bookmarks (e.g. the per-shell HTTP route table) can be refreshed as bookmarks come and go,
/// without the runtime taking a dependency on any consumer.
/// </summary>
/// <remarks>
/// <para>
/// This is a contribution (fan-in) seam: register implementations with
/// <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;IBookmarkLifecycleObserver, MyObserver&gt;())</c>;
/// the runtime resolves them as <c>IEnumerable&lt;IBookmarkLifecycleObserver&gt;</c>. There is no default
/// implementation — an unobserved bookmark lifecycle is valid. Register observers as <b>singleton</b>: the
/// notifier that fans into them is a singleton capturing <c>IEnumerable&lt;IBookmarkLifecycleObserver&gt;</c>, so a
/// scoped observer would be a captive dependency — resolve any scoped services from a fresh scope opened per
/// notification instead (as <c>RouteTableBookmarkObserver</c> does via the route-table synchronizer).
/// </para>
/// <para>
/// <b>Failure policy (deliberately different from <see cref="IWorkflowTriggerIndexObserver"/>):</b> this seam
/// fires on the RUN path, not the publish path. An observer exception is caught and logged and MUST NEVER fault
/// the running workflow — a stale projection is tolerated (a route not yet refreshed simply 404s until the next
/// refresh) rather than aborting a durable, already-committed run. Contrast the trigger-index observer, whose
/// exceptions fail the publish because a stale trigger projection would silently drop a start trigger. Because a
/// throw is swallowed, keep observer work idempotent and cheap.
/// </para>
/// </remarks>
public interface IBookmarkLifecycleObserver
{
    /// <summary>
    /// Called after the bookmark-created checkpoint committed. <paramref name="bookmark"/> is the committed
    /// <see cref="BookmarkState"/> (incl. <c>Metadata</c>). Throwing is caught and logged, never faults the run.
    /// </summary>
    ValueTask OnBookmarkCreatedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after the bookmark-consumed checkpoint committed (the bookmark row was deleted).
    /// <paramref name="bookmark"/> is the committed-away <see cref="BookmarkState"/> (incl. <c>Metadata</c>).
    /// Throwing is caught and logged, never faults the run.
    /// </summary>
    ValueTask OnBookmarkConsumedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default);
}
