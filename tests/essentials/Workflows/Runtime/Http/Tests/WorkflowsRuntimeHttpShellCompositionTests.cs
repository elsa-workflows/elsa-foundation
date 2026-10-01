using System.Diagnostics;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Http;
using Elsa.Http.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Tasks;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

/// <summary>
/// #2190 at the composition level: <see cref="WorkflowsRuntimeHttpFeature"/> in a real CShells shell, through the real
/// Tasks and Http features, gets its convergence pump scheduled on activation, and the pump runs. The proof is an
/// endpoint written straight to the shared trigger index after activation, as another node would write it: no observer
/// fires for it here and the startup refresh is already over, so only a pump tick can bring it into the route table.
/// </summary>
public sealed class WorkflowsRuntimeHttpShellCompositionTests : IAsyncDisposable
{
    private const string ShellName = "route-convergence";
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);
    private readonly ServiceProvider _root;

    public WorkflowsRuntimeHttpShellCompositionTests()
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
                typeof(WorkflowsRuntimeHttpFeature).Assembly,
                typeof(TriggerIndexStandInFeature).Assembly)
            .AddShell(ShellName, shell => shell
                .WithFeature<TasksFeature>()
                .WithFeature<HttpFeature>()
                .WithFeature<TriggerIndexStandInFeature>()
                .WithFeature<WorkflowsRuntimeHttpFeature>(feature => feature.RouteTableConvergenceIntervalSeconds = 0.05)));
        _root = services.BuildServiceProvider();
    }

    [Fact]
    public async Task The_tasks_feature_schedules_the_convergence_pump_and_a_tick_routes_an_endpoint_another_node_wrote()
    {
        var shell = await _root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var routeTable = scope.ServiceProvider.GetRequiredService<IRouteTable>();
        Assert.Empty(routeTable);

        await shell.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>()
            .SaveAsync(Bindings.HttpEndpoint("artifact-other-node", "node-http", "orders", "GET"));

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

/// <summary>
/// Stands in for the WorkflowsRuntimeTriggers dependency, which brings the whole runtime API with it: only the trigger
/// index and bookmark lookup the route-table resolver reads, over in-memory stores shared by the shell.
/// </summary>
[ShellFeature(name: "WorkflowsRuntimeTriggers", DisplayName = "Trigger index stand-in", Description = "In-memory trigger index and bookmark lookup for composition tests.")]
public sealed class TriggerIndexStandInFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
        services.AddSingleton<IBookmarkStimulusIndex, InMemoryBookmarkStateStore>();
        services.AddSingleton<IGlobalBookmarkStimulusLookup, GlobalBookmarkStimulusLookup>();
    }
}
