namespace Elsa.Workflows.Runtime.Http.Contracts;

/// <summary>
/// The single serialization point for rebuilding the per-shell HTTP <see cref="Elsa.Http.Core.Contracts.IRouteTable"/>
/// from the durable sources (trigger bindings ∪ waiting bookmarks). Every route-table refresh — publish-time (the
/// trigger-index observer), run-time (the bookmark lifecycle observer), startup, and the convergence check that picks up
/// changes made on other nodes — routes through this seam so the read-then-swap can never interleave across actor
/// threads (spec 089 D review fix).
/// </summary>
/// <remarks>
/// The route table's read-then-swap is not itself atomic across the resolver read and the table swap, so two
/// concurrent refreshes could race: a refresh built from a stale read could clobber a newer swap and permanently
/// drop a live waiting-bookmark route (no self-heal — the healing notification already fired). This seam guards the
/// whole read+swap under one lock. Because every notification fires post-commit and refreshes are serialized, every
/// refresh's read observes all commits whose notifications preceded its lock acquisition; any commit landing after a
/// read triggers its own queued refresh — so no update is lost on the node that made it. Notifications are local to
/// that node, so the other nodes pick the change up through <see cref="ConvergeAsync"/> (#2190).
/// </remarks>
public interface IHttpEndpointRouteTableSynchronizer
{
    /// <summary>
    /// Rebuilds the whole route table from the durable sources under the serialization lock: acquires the lock,
    /// opens a fresh scope, resolves <see cref="IHttpEndpointRoutesResolver.ResolveRouteSetAsync"/>, refreshes the
    /// route table, then releases. Exceptions propagate to the caller unchanged — callers apply their own failure
    /// policy (the trigger-index observer fails the publish; the bookmark observer's notifier swallows+logs).
    /// </summary>
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings the route table in line with the durable sources when they no longer match what it was last built from,
    /// whichever node changed them. Under the same lock as <see cref="RefreshAsync"/>, reads the resolver's cheap
    /// <see cref="IHttpEndpointRoutesResolver.ResolveRouteFingerprintAsync"/> and refreshes only when it differs from the
    /// fingerprint of the last successful refresh, or when there is none. Returns whether it refreshed. Exceptions
    /// propagate unchanged.
    /// </summary>
    ValueTask<bool> ConvergeAsync(CancellationToken cancellationToken = default);
}
