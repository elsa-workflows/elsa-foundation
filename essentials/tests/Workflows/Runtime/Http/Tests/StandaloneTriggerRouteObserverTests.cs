using Elsa.Events.Core.Contracts;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Http.Services;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

public sealed class StandaloneTriggerRouteObserverTests
{
    [Fact]
    public async Task Committed_HTTP_changes_refresh_the_real_route_table_without_waiting_for_a_pump_tick()
    {
        using var fixture = new Fixture();
        Assert.Empty(fixture.Routes);
        await fixture.Handler.Handle(Notification("http"), default);
        Assert.Equal("orders", Assert.Single(fixture.Routes).Route);
        fixture.Resolver.Template = "changed-orders";
        await fixture.Handler.Handle(Notification("http"), default);
        Assert.Equal("changed-orders", Assert.Single(fixture.Routes).Route);
    }

    [Fact]
    public async Task Other_resource_types_do_not_read_or_refresh_HTTP_routes()
    {
        using var fixture = new Fixture();
        await fixture.Handler.Handle(Notification("event"), default);
        Assert.Equal(0, fixture.Resolver.Reads);
        Assert.Empty(fixture.Routes);
    }

    [Fact]
    public async Task Read_failure_is_reported_and_replay_can_repair_the_same_committed_change()
    {
        using var fixture = new Fixture();
        fixture.Resolver.Fail = true;
        var failure = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Handler.Handle(Notification("http"), default));
        Assert.Equal("unavailable", failure.Code);
        Assert.IsType<IOException>(failure.InnerException);
        Assert.Equal("Committed", failure.Fields!["commitState"]);
        Assert.Equal("shell", failure.Fields["scope"]);
        Assert.Equal("tenant", failure.Fields["tenantId"]);
        Assert.Equal("trigger", failure.Fields["triggerId"]);
        Assert.Equal("2", failure.Fields["revision"]);
        Assert.Empty(fixture.Routes);
        fixture.Resolver.Fail = false;
        await fixture.Handler.Handle(Notification("http"), default);
        Assert.Equal("orders", Assert.Single(fixture.Routes).Route);
    }

    [Fact]
    public async Task Requested_cancellation_is_preserved_without_becoming_a_projection_failure()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Handler.Handle(Notification("http"), cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Empty(fixture.Routes);
    }

    private static StandaloneTriggerResourceCommitted Notification(string type) => new(new(new("shell"), "tenant"), "trigger", type, 2);
    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider provider;
        private readonly IServiceScope scope;
        private readonly MemoryCache cache = new(new MemoryCacheOptions());
        internal readonly Resolver Resolver = new();
        internal IRouteTable Routes { get; }
        internal IEventHandler<StandaloneTriggerResourceCommitted> Handler { get; }
        internal Fixture()
        {
            Routes = new RouteTable(cache, NullLogger<RouteTable>.Instance);
            var services = new ServiceCollection().AddLogging();
            new WorkflowsRuntimeHttpFeature().ConfigureServices(services);
            services.AddSingleton(Routes);
            services.AddSingleton<IHttpEndpointRoutesResolver>(Resolver);
            provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            scope = provider.CreateScope();
            Handler = scope.ServiceProvider.GetRequiredService<IEventHandler<StandaloneTriggerResourceCommitted>>();
        }
        public void Dispose() { scope.Dispose(); provider.Dispose(); cache.Dispose(); }
    }
    private sealed class Resolver : IHttpEndpointRoutesResolver
    {
        internal string Template = "orders";
        internal bool Fail;
        internal int Reads;
        public ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<HttpRouteData>>([new(Template)]);
        public async ValueTask<HttpEndpointRouteSet> ResolveRouteSetAsync(CancellationToken cancellationToken = default) => new(await ResolveRoutesAsync(cancellationToken), Template);
        public ValueTask<string> ResolveRouteFingerprintAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            if (Fail) throw new IOException("Owned committed-route read failure");
            return ValueTask.FromResult(Template);
        }
    }
}
