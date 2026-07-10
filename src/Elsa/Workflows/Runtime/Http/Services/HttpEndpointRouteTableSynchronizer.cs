using Elsa.Http.Core.Contracts;
using Elsa.Workflows.Runtime.Http.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// The single serialization point for HTTP route-table refreshes (spec 089 D review fix). A singleton owning a
/// <see cref="SemaphoreSlim"/>(1,1): every <see cref="RefreshAsync"/> acquires the lock, opens a FRESH scope,
/// resolves the scoped <see cref="IHttpEndpointRoutesResolver"/> and <see cref="IRouteTable"/> inside it, does the
/// full read (<see cref="IHttpEndpointRoutesResolver.ResolveRoutesAsync"/>) + swap (<see cref="IRouteTable.Refresh(IEnumerable{Http.Core.Models.HttpRouteData})"/>),
/// then releases.
/// </summary>
/// <remarks>
/// <para>
/// Before this seam, each caller (the trigger-index observer, the bookmark lifecycle observer, and the startup
/// task) opened its own scope and did its own read-then-swap. Those swaps are not serialized across actor threads,
/// so a refresh built from a stale read could clobber a newer swap and permanently drop a live waiting-bookmark
/// route — with no self-heal, because the healing notification had already fired. Funnelling every refresh through
/// one lock closes that window: refreshes run strictly one at a time, and because every notification fires
/// post-commit, each refresh's read observes all commits whose notifications preceded its lock acquisition. Any
/// commit that lands after a read has already queued its own refresh, so no update is lost.
/// </para>
/// <para>
/// The route table's state lives in the shared memory cache, so resolving it from any scope mutates the same table
/// — resolving inside the per-refresh scope is therefore equivalent to (and matches) the observers' prior pattern,
/// with the read+swap now guarded. Exceptions propagate unchanged: the trigger-index observer lets a throw fail the
/// publish; the bookmark observer runs under the <c>BookmarkLifecycleNotifier</c>, which swallows and logs.
/// </para>
/// </remarks>
public sealed class HttpEndpointRouteTableSynchronizer(IServiceScopeFactory scopeFactory) : IHttpEndpointRouteTableSynchronizer, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var resolver = scope.ServiceProvider.GetRequiredService<IHttpEndpointRoutesResolver>();
            var routeTable = scope.ServiceProvider.GetRequiredService<IRouteTable>();

            var routes = await resolver.ResolveRoutesAsync(cancellationToken);
            await routeTable.Refresh(routes);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
