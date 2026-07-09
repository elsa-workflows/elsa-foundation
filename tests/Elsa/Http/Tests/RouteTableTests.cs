using Elsa.Http.Core.Models;
using Elsa.Http.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Http.Tests;

/// <summary>
/// Unit coverage for the internal <see cref="RouteTable"/>. The regression under test (B6): <c>Refresh</c> used to
/// <c>Clear()</c> the live dictionary then re-<c>Add</c> item by item, so any reader that enumerated during a
/// publish could observe an empty or partial table — a transient 404. The fix builds a complete new table off to
/// the side and publishes it in a single cache swap, so a reader observes either the old table or the fully-built
/// new one, never the empty intermediate.
/// </summary>
public sealed class RouteTableTests
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private RouteTable CreateTable() => new(_cache, NullLogger<RouteTable>.Instance);

    [Fact]
    public async Task Refresh_ReplacesTheWholeTable()
    {
        var table = CreateTable();
        await table.Refresh(new[] { "orders/webhook", "invoices/{id}" });

        await table.Refresh(new[] { "payments/callback" });

        Assert.Equal(new[] { "payments/callback" }, Sorted(table));
    }

    [Fact]
    public async Task Refresh_WithDuplicateRoute_Throws_AndLeavesLiveTableIntact()
    {
        var table = CreateTable();
        await table.Refresh(new[] { "orders/webhook" });

        // A build-time duplicate must still surface, but must NOT destroy the live table (the swap never happens).
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await table.Refresh(new[] { "invoices/a", "invoices/a" }));

        Assert.Equal(new[] { "orders/webhook" }, Sorted(table));
    }

    [Fact]
    public async Task Refresh_SwapsAtomically_OldEnumerableStillHoldsOldRoutes()
    {
        var table = CreateTable();
        await table.Refresh(new[] { "orders/webhook", "invoices/{id}" });

        // Snapshot the live table before the refresh. The swap replaces the container the cache holds, so this
        // captured snapshot must still hold exactly the old routes — never the empty/partial intermediate a
        // Clear()+Add loop would have exposed to a concurrent reader.
        var beforeSnapshot = table.ToArray();

        await table.Refresh(new[] { "payments/callback" });

        Assert.Equal(new[] { "invoices/{id}", "orders/webhook" }, Sorted(beforeSnapshot.Select(r => r.Route)));
        Assert.Equal(new[] { "payments/callback" }, Sorted(table));
    }

    [Fact]
    public async Task ConcurrentEnumeration_NeverObservesAnEmptyTable_DuringRefresh()
    {
        var table = CreateTable();
        var oldSet = new[] { "a/1", "a/2", "a/3" };
        var newSet = new[] { "b/1", "b/2", "b/3", "b/4" };
        await table.Refresh(oldSet);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observedEmpty = false;

        // Reader: enumerate as fast as possible. Every snapshot must be a whole set (old or new), never empty.
        var reader = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                if (table.Count() == 0)
                {
                    observedEmpty = true;
                    return;
                }
            }
        });

        // Writer: flip between the two non-empty sets many times.
        for (var i = 0; i < 2000 && !observedEmpty; i++)
            await table.Refresh(i % 2 == 0 ? newSet : oldSet);

        cts.Cancel();
        await reader;

        Assert.False(observedEmpty, "Refresh must swap the route table atomically; a reader observed an empty table mid-publish.");
    }

    private static IEnumerable<string> Sorted(IEnumerable<HttpRouteData> routes) => Sorted(routes.Select(r => r.Route));
    private static IEnumerable<string> Sorted(IEnumerable<string> routes) => routes.OrderBy(r => r, StringComparer.Ordinal);
}
