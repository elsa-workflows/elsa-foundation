using Elsa.Http.Core.Models;
using Elsa.Http.Options;
using Elsa.Http.Services;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Http.Tests;

/// <summary>
/// Unit coverage for the internal <see cref="RouteTable"/>. Covers the B6 atomic-swap regression (a reader must
/// never observe an empty/partial table during a publish) plus the issue #592 follow-ups: specificity-ordered
/// enumeration (item 1), precompiled matchers at refresh (item 6), and a shell-namespaced cache key (item 5).
/// </summary>
public sealed class RouteTableTests
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private RouteTable CreateTable() => new(_cache, NullLogger<RouteTable>.Instance);

    private RouteTable CreateTable(string shellDiscriminator) =>
        new(_cache, NullLogger<RouteTable>.Instance, Microsoft.Extensions.Options.Options.Create(new RouteTableOptions { ShellDiscriminator = shellDiscriminator }));

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

    // ---- Issue #592 item 1: specificity-ordered enumeration ----

    [Theory]
    [InlineData("orders/list", "orders/{id}")] // literal segment before parameter segment
    [InlineData("orders/{id}", "{*catchall}")] // parameter segment before catch-all
    public async Task Enumerates_MoreSpecific_First_RegardlessOfInsertionOrder(string moreSpecific, string lessSpecific)
    {
        // Whichever order they are refreshed in, the more specific template must enumerate first so the
        // middleware's "first match wins" is deterministic.
        var forward = CreateTable();
        await forward.Refresh(new[] { moreSpecific, lessSpecific });

        var reverse = new RouteTable(new MemoryCache(new MemoryCacheOptions()), NullLogger<RouteTable>.Instance);
        await reverse.Refresh(new[] { lessSpecific, moreSpecific });

        Assert.Equal(moreSpecific, forward.First().Route.Trim('/'));
        Assert.Equal(moreSpecific, reverse.First().Route.Trim('/'));
    }

    [Fact]
    public async Task Add_KeepsTheTable_SpecificityOrdered()
    {
        var table = CreateTable();
        await table.Refresh(new[] { "orders/{id}" });

        // Adding the more-specific literal after the parameter route must still place it first.
        await table.Add("orders/list");

        Assert.Equal("orders/list", table.First().Route.Trim('/'));
    }

    // ---- Issue #592 item 6: precompiled matchers ----

    [Fact]
    public async Task Refresh_PrecompilesATemplateMatcher_PerRoute()
    {
        var table = CreateTable();
        await table.Refresh(new[] { "orders/{id}" });

        var route = Assert.Single(table);
        Assert.IsType<TemplateMatcher>(route.CompiledMatcher);
    }

    // ---- Issue #592 item 5: shell-namespaced cache key ----

    [Fact]
    public async Task DifferentShellDiscriminators_DoNotAlias_OnASharedCache()
    {
        // Both tables share ONE cache (the future root-promoted scenario). A distinct discriminator per shell must
        // keep their route sets separate rather than clobbering one entry.
        var shellA = CreateTable("shell-a");
        var shellB = CreateTable("shell-b");

        await shellA.Refresh(new[] { "a/route" });
        await shellB.Refresh(new[] { "b/route" });

        Assert.Equal(new[] { "a/route" }, shellA.Select(r => r.Route).ToArray());
        Assert.Equal(new[] { "b/route" }, shellB.Select(r => r.Route).ToArray());
    }

    private static IEnumerable<string> Sorted(IEnumerable<HttpRouteData> routes) => Sorted(routes.Select(r => r.Route));
    private static IEnumerable<string> Sorted(IEnumerable<string> routes) => routes.OrderBy(r => r, StringComparer.Ordinal);
}
