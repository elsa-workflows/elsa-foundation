using System.Diagnostics;
using Elsa.Http.Core.Contracts;
using Elsa.Primitives.Diagnostics;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Diagnostics;
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
        var started = Stopwatch.GetTimestamp();
        var outcome = HttpRouteTableTelemetry.SuccessOutcome;
        int? routeCount = null;
        var gateAcquired = false;
        using var activity = ObservationalTelemetryScope.Start(
            HttpRouteTableTelemetry.ActivitySource,
            HttpRouteTableTelemetry.ActivityName);

        try
        {
            await _gate.WaitAsync(cancellationToken);
            gateAcquired = true;
            await using var scope = scopeFactory.CreateAsyncScope();
            var resolver = scope.ServiceProvider.GetRequiredService<IHttpEndpointRoutesResolver>();
            var routeTable = scope.ServiceProvider.GetRequiredService<IRouteTable>();

            var routes = await resolver.ResolveRoutesAsync(cancellationToken);
            await routeTable.Refresh(routes);
            routeCount = routes.Count;
        }
        catch (OperationCanceledException)
        {
            outcome = HttpRouteTableTelemetry.CancelledOutcome;
            activity.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        catch (Exception)
        {
            outcome = HttpRouteTableTelemetry.FailedOutcome;
            activity.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            if (gateAcquired)
                _gate.Release();

            var tags = new TagList { { HttpRouteTableTelemetry.OutcomeTag, outcome } };
            activity.SetTag(HttpRouteTableTelemetry.OutcomeTag, outcome);
            if (routeCount is not null)
            {
                activity.SetTag(HttpRouteTableTelemetry.RouteCountTag, routeCount.Value);
            }

            activity.Observe(() =>
                HttpRouteTableTelemetry.Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags));
        }
    }

    public void Dispose() => _gate.Dispose();
}
