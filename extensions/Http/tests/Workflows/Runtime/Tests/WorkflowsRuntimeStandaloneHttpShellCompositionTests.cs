using System.Diagnostics;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Http;
using Elsa.Http.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Tasks;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

/// <summary>Activating the standalone HTTP feature starts convergence through its Tasks dependency.</summary>
public sealed class WorkflowsRuntimeStandaloneHttpShellCompositionTests : IAsyncDisposable
{
    private const string ShellName = "route-convergence";
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);
    private readonly ServiceProvider _root;

    public WorkflowsRuntimeStandaloneHttpShellCompositionTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        // The task executor needs a lock provider to exist; the pump takes no lock.
        services.AddSingleton<IDistributedLockProvider, NoLocks>();
        services.AddCShells(shells => shells
            .WithAssemblies(
                typeof(TasksFeature).Assembly,
                typeof(HttpFeature).Assembly,
                typeof(WorkflowsRuntimeStandaloneHttpFeature).Assembly,
                typeof(ReservationProjectionStandInFeature).Assembly)
            .AddShell(ShellName, shell => shell
                .WithFeature<HttpFeature>()
                .WithFeature<ReservationProjectionStandInFeature>()
                .WithFeature<WorkflowsRuntimeStandaloneHttpFeature>(feature => feature.RouteTableConvergenceIntervalSeconds = 0.05)));
        _root = services.BuildServiceProvider();
    }

    [Fact]
    public async Task The_tasks_feature_schedules_the_convergence_pump_and_a_tick_routes_an_endpoint_another_node_wrote()
    {
        var shell = await _root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var routeTable = scope.ServiceProvider.GetRequiredService<IRouteTable>();
        Assert.Empty(routeTable);

        // Write directly to the shared state, as a peer would, after startup and without a local notification.
        scope.ServiceProvider.GetRequiredService<ReservationProjectionStandInFeature.Reservations>().AddOrders();

        var elapsed = Stopwatch.StartNew();
        while (!routeTable.Any(route => route.Route == "orders"))
        {
            Assert.True(elapsed.Elapsed < Bound, $"No convergence tick routed the endpoint within {Bound}.");
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    private sealed class NoLocks : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

// Each view refuses use after its scope ends, so capturing it in the background pump fails the live shell check.
[ShellFeature(name: "TestHttpReservationProjection", DisplayName = "HTTP reservation stand-in", Description = "Scoped read view of shared reservations for composition tests.")]
public sealed class ReservationProjectionStandInFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<Reservations>();
        services.AddSingleton<IPersistenceAccessContextAccessor, Access>();
        services.AddScoped<IRuntimeIngressReservationReader, Reader>();
    }

    internal sealed class Reservations
    {
        private RuntimeIngressReservation[] _rows = [];
        internal IReadOnlyList<RuntimeIngressReservation> Rows => Volatile.Read(ref _rows);

        internal void AddOrders() => Volatile.Write(ref _rows,
            [new("orders-reservation", new(new("test-partition"), "test-tenant"),
                HttpEndpointRouting.StimulusType, "test-orders", RuntimeIngressReservation.StandaloneTriggerOwner,
                "orders-trigger", 1, 1, new Dictionary<string, string>
                {
                    [HttpEndpointRouting.TemplateMetadataKey] = "orders",
                    [HttpEndpointRouting.MethodMetadataKey] = "GET"
                })]);
    }

    private sealed class Access : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("test-partition"));
    }

    private sealed class Reader(Reservations reservations) : IRuntimeIngressReservationReader, IDisposable
    {
        private bool _disposed;

        public bool SupportsReservations => !_disposed;

        public ValueTask<RuntimeIngressReservationPage> ListInScopeAsync(
            PersistenceScope scope, RuntimeIngressReservationQuery query, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ValueTask.FromResult(new RuntimeIngressReservationPage(
                reservations.Rows.Where(row => row.Scope.Persistence == scope && row.StimulusType == query.StimulusType).ToArray(), null));
        }

        public ValueTask<RuntimeIngressReservation?> FindAsync(
            TriggerScope scope, string stimulusType, string stimulusHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RuntimeIngressReservationPage> ListAsync(
            TriggerScope scope, RuntimeIngressReservationQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose() => _disposed = true;
    }
}
