namespace Elsa.Workflows.Runtime.Http.Contracts;

/// <summary>
/// The single serialization point for rebuilding the per-shell HTTP <see cref="Elsa.Http.Core.Contracts.IRouteTable"/>
/// from the durable sources (trigger bindings ∪ waiting bookmarks). Every route-table refresh — publish-time (the
/// trigger-index observer), run-time (the bookmark lifecycle observer), and startup — routes through
/// <see cref="RefreshAsync"/> so the read-then-swap can never interleave across actor threads (spec 089 D review fix).
/// </summary>
/// <remarks>
/// The route table's read-then-swap is not itself atomic across the resolver read and the table swap, so two
/// concurrent refreshes could race: a refresh built from a stale read could clobber a newer swap and permanently
/// drop a live waiting-bookmark route (no self-heal — the healing notification already fired). This seam guards the
/// whole read+swap under one lock. Because every notification fires post-commit and refreshes are serialized, every
/// refresh's read observes all commits whose notifications preceded its lock acquisition; any commit landing after a
/// read triggers its own queued refresh — so no update is lost.
/// </remarks>
public interface IHttpEndpointRouteTableSynchronizer
{
    /// <summary>
    /// Rebuilds the whole route table from the durable sources under the serialization lock: acquires the lock,
    /// opens a fresh scope, resolves <see cref="IHttpEndpointRoutesResolver.ResolveRoutesAsync"/>, refreshes the
    /// route table, then releases. Exceptions propagate to the caller unchanged — callers apply their own failure
    /// policy (the trigger-index observer fails the publish; the bookmark observer's notifier swallows+logs).
    /// </summary>
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);
}
