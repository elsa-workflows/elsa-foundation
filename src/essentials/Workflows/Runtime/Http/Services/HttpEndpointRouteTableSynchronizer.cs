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
/// full read (<see cref="IHttpEndpointRoutesResolver.ResolveRouteSetAsync"/>) + swap (<see cref="IRouteTable.Refresh(IEnumerable{Http.Core.Models.HttpRouteData})"/>),
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
/// <b>Convergence across nodes (#2190).</b> The notifications that drive the observers fire only on the node that made
/// the change, so each refresh also keeps the fingerprint of the stimulus identities its routes were projected from,
/// computed from the rows it read. <see cref="ConvergeAsync"/>, run on an interval by the convergence pump, compares
/// that with the fingerprint the durable sources give now and refreshes only when they differ. Because the kept
/// fingerprint describes the rows that were loaded, not a separate earlier read, a change that lands and is undone
/// between two checks cannot leave the table behind unnoticed. Any failed refresh forgets the fingerprint, so the next
/// check rebuilds rather than trusting a table in an unknown state.
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

    // The fingerprint of the identities the current table was built from; null until a refresh succeeds, and after any
    // refresh fails. Read and written only under _gate.
    private string? _refreshedFingerprint;

    public ValueTask RefreshAsync(CancellationToken cancellationToken = default) =>
        ObserveRefreshAsync(async () =>
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                return await RefreshUnderGateAsync(scope.ServiceProvider, cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        });

    public async ValueTask<bool> ConvergeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var resolver = scope.ServiceProvider.GetRequiredService<IHttpEndpointRoutesResolver>();
            var fingerprint = await resolver.ResolveRouteFingerprintAsync(cancellationToken);
            if (StringComparer.Ordinal.Equals(fingerprint, _refreshedFingerprint))
                return false;

            await ObserveRefreshAsync(() => RefreshUnderGateAsync(scope.ServiceProvider, cancellationToken));
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<int> RefreshUnderGateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var resolver = services.GetRequiredService<IHttpEndpointRoutesResolver>();
        var routeTable = services.GetRequiredService<IRouteTable>();

        _refreshedFingerprint = null;
        var routeSet = await resolver.ResolveRouteSetAsync(cancellationToken);
        await routeTable.Refresh(routeSet.Routes);
        _refreshedFingerprint = routeSet.Fingerprint;
        return routeSet.Routes.Count;
    }

    private static async ValueTask ObserveRefreshAsync(Func<Task<int>> refresh)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = HttpRouteTableTelemetry.SuccessOutcome;
        int? routeCount = null;
        using var activity = ObservationalTelemetryScope.Start(
            HttpRouteTableTelemetry.GetActivitySource,
            HttpRouteTableTelemetry.ActivityName);

        try
        {
            routeCount = await refresh();
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
            var tags = new TagList { { HttpRouteTableTelemetry.OutcomeTag, outcome } };
            activity.SetTag(HttpRouteTableTelemetry.OutcomeTag, outcome);
            if (routeCount is not null)
            {
                activity.SetTag(HttpRouteTableTelemetry.RouteCountTag, routeCount.Value);
            }

            activity.Observe(
                HttpRouteTableTelemetry.GetDuration,
                histogram => histogram.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags));
        }
    }
}
