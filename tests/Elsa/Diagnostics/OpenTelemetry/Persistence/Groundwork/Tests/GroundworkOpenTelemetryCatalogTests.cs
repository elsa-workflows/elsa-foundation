using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

public sealed class GroundworkOpenTelemetryCatalogTests : IAsyncLifetime
{
    private readonly OpenTelemetryGroundworkSqliteFixture _fixture = new();

    [Fact]
    public async Task Catalog_upserts_and_capacity_keep_the_newest_entries()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = new GroundworkOpenTelemetryStore(
            providers,
            Options.Create(new OpenTelemetryDiagnosticsOptions
            {
                ResourceCapacity = 2,
                MetricInstrumentCapacity = 2,
                MaxQuerySize = 20
            }),
            _fixture.Binding);
        var first = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

        await store.WriteDurablyAsync(Batch(Resource("resource-a", first), Instrument("instrument-a", "resource-a"), first));
        await store.WriteDurablyAsync(Batch(Resource("resource-b", first.AddSeconds(1)), Instrument("instrument-b", "resource-b"), first.AddSeconds(1)));
        await store.WriteDurablyAsync(Batch(Resource("resource-c", first.AddSeconds(2)), Instrument("instrument-c", "resource-c"), first.AddSeconds(2)));

        var resources = await store.QueryResourcesAsync(new() { Take = 20 });
        Assert.Equal(["resource-c", "resource-b"], resources.Items.Select(x => x.Id));
        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal(2, diagnostics.ResourceCount);
        Assert.Equal(2, diagnostics.MetricInstrumentCount);

        var refreshed = Resource("resource-b", first.AddSeconds(3)) with { ServiceName = "orders-refreshed" };
        await store.WriteDurablyAsync(Batch(refreshed, Instrument("instrument-b", "resource-b"), first.AddSeconds(3)));
        var afterUpsert = await store.QueryResourcesAsync(new() { Take = 20 });
        Assert.Equal(["resource-b", "resource-c"], afterUpsert.Items.Select(x => x.Id));
        Assert.Equal("orders-refreshed", afterUpsert.Items.First().ServiceName);
    }

    [Fact]
    public async Task Misconfigured_catalog_capacities_clamp_to_one()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = new GroundworkOpenTelemetryStore(
            providers,
            Options.Create(new OpenTelemetryDiagnosticsOptions
            {
                ResourceCapacity = 0,
                MetricInstrumentCapacity = 0,
                MaxQuerySize = 20
            }),
            _fixture.Binding);
        var time = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

        await store.WriteDurablyAsync(Batch(Resource("resource-a", time), Instrument("instrument-a", "resource-a"), time));

        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal(1, diagnostics.ResourceCount);
        Assert.Equal(1, diagnostics.MetricInstrumentCount);
        Assert.Equal("resource-a", Assert.Single((await store.QueryResourcesAsync(new() { Take = 20 })).Items).Id);
    }

    [Fact]
    public async Task Lowered_catalog_capacity_removes_all_preexisting_overflow_in_bounded_pages()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var initial = new GroundworkOpenTelemetryStore(
            providers,
            Options.Create(new OpenTelemetryDiagnosticsOptions
            {
                ResourceCapacity = 10,
                MetricInstrumentCapacity = 10,
                MaxQuerySize = 20
            }),
            _fixture.Binding);
        var first = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 6; index++)
        {
            var suffix = (char)('a' + index);
            await initial.WriteDurablyAsync(Batch(
                Resource($"resource-{suffix}", first.AddSeconds(index)),
                Instrument($"instrument-{suffix}", $"resource-{suffix}"),
                first.AddSeconds(index)));
        }

        var reduced = new GroundworkOpenTelemetryStore(
            providers,
            Options.Create(new OpenTelemetryDiagnosticsOptions
            {
                ResourceCapacity = 2,
                MetricInstrumentCapacity = 2,
                MaxQuerySize = 20
            }),
            _fixture.Binding);
        await reduced.WriteDurablyAsync(Batch(
            Resource("resource-g", first.AddSeconds(6)),
            Instrument("instrument-g", "resource-g"),
            first.AddSeconds(6)));

        var diagnostics = await reduced.GetDiagnosticsAsync();
        Assert.Equal(2, diagnostics.ResourceCount);
        Assert.Equal(2, diagnostics.MetricInstrumentCount);
        Assert.Equal(
            ["resource-g", "resource-f"],
            (await reduced.QueryResourcesAsync(new() { Take = 20 })).Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Resource_and_instrument_capacities_are_enforced_independently()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = new GroundworkOpenTelemetryStore(
            providers,
            Options.Create(new OpenTelemetryDiagnosticsOptions
            {
                ResourceCapacity = 1,
                MetricInstrumentCapacity = 2,
                MaxQuerySize = 20
            }),
            _fixture.Binding);
        var first = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

        await store.WriteDurablyAsync(Batch(Resource("resource-a", first), Instrument("instrument-a", "resource-a"), first));
        await store.WriteDurablyAsync(Batch(Resource("resource-b", first.AddSeconds(1)), Instrument("instrument-b", "resource-b"), first.AddSeconds(1)));

        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal(1, diagnostics.ResourceCount);
        Assert.Equal(2, diagnostics.MetricInstrumentCount);
    }

    [Fact]
    public async Task Case_equivalent_catalog_ids_update_one_authoritative_entry()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = new GroundworkOpenTelemetryStore(
            providers,
            Options.Create(new OpenTelemetryDiagnosticsOptions
            {
                ResourceCapacity = 10,
                MetricInstrumentCapacity = 10,
                MaxQuerySize = 20
            }),
            _fixture.Binding);
        var first = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

        await store.WriteDurablyAsync(Batch(
            Resource("resource-api", first),
            Instrument("resource-api:request.count:gauge", "resource-api"),
            first));
        await store.WriteDurablyAsync(Batch(
            Resource("RESOURCE-API", first.AddSeconds(1)) with { ServiceName = "orders-refreshed" },
            Instrument("RESOURCE-API:REQUEST.COUNT:GAUGE", "RESOURCE-API") with { Name = "REQUEST.COUNT" },
            first.AddSeconds(1)));

        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal(1, diagnostics.ResourceCount);
        Assert.Equal(1, diagnostics.MetricInstrumentCount);
        var resource = Assert.Single((await store.QueryResourcesAsync(new() { Take = 20 })).Items);
        Assert.Equal("RESOURCE-API", resource.Id);
        Assert.Equal("orders-refreshed", resource.ServiceName);
    }

    [Fact]
    public async Task Status_filter_keeps_last_seen_order_before_applying_take()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = new GroundworkOpenTelemetryStore(
            providers,
            Options.Create(new OpenTelemetryDiagnosticsOptions { ResourceCapacity = 10, MaxQuerySize = 20 }),
            _fixture.Binding);
        var first = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

        await store.WriteDurablyAsync(Batch(
            Resource("resource-old", first),
            Instrument("instrument-old", "resource-old"),
            first));
        await store.WriteDurablyAsync(Batch(
            Resource("resource-other", first.AddSeconds(2)) with { Status = TelemetryResourceStatus.Stale },
            Instrument("instrument-other", "resource-other"),
            first.AddSeconds(2)));
        await store.WriteDurablyAsync(Batch(
            Resource("resource-new", first.AddSeconds(3)),
            Instrument("instrument-new", "resource-new"),
            first.AddSeconds(3)));

        var result = await store.QueryResourcesAsync(new()
        {
            Status = TelemetryResourceStatus.Active,
            Take = 1
        });

        Assert.Equal("resource-new", Assert.Single(result.Items).Id);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private static OpenTelemetryBatch Batch(TelemetryResource resource, MetricInstrument instrument, DateTimeOffset time) =>
        new([resource], [], [], [instrument], [], []);

    private static TelemetryResource Resource(string id, DateTimeOffset lastSeen) =>
        new(id, "orders", null, "dotnet", new Dictionary<string, string?>(), lastSeen, TelemetryResourceStatus.Active);

    private static MetricInstrument Instrument(string id, string resourceId) =>
        new(id, resourceId, "orders.duration", "ms", null, MetricKind.Gauge, new Dictionary<string, string?>());
}
