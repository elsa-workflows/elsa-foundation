using System.Diagnostics;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Http;
using Elsa.Http.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Tasks;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

/// <summary>
/// #2190 at the composition level: <see cref="WorkflowsRuntimeHttpFeature"/> in a real CShells shell, with the real Http
/// feature, gets its convergence pump scheduled on activation, and the pump runs. The Tasks feature is not listed: CShells
/// enables a feature's DependsOn closure, so it arrives only because WorkflowsRuntimeHttp depends on it, and the test fails
/// if that dependency is dropped. The proof is an endpoint written straight to the shared trigger index after activation,
/// as another node would write it: no observer fires for it here and the startup refresh is already over, so only a pump
/// tick can bring it into the route table.
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

        await using (var writer = shell.BeginScope())
            await writer.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>()
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
/// index and bookmark lookup the route-table resolver reads. All three are scoped, as under a durable provider, whose
/// stores are scoped EF adapters; the in-memory data behind them is shared. CShells builds shell providers without scope
/// validation, so each scoped view refuses use after its scope ends: a pump or resolver that kept one past its scope
/// fails here instead of passing.
/// </summary>
[ShellFeature(name: "WorkflowsRuntimeTriggers", DisplayName = "Trigger index stand-in", Description = "In-memory trigger index and bookmark lookup for composition tests.")]
public sealed class TriggerIndexStandInFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<InMemoryWorkflowTriggerBindingStore>();
        services.AddSingleton<InMemoryBookmarkStateStore>();
        services.AddScoped<IWorkflowTriggerBindingStore>(sp => new ScopedTriggerBindingStore(sp.GetRequiredService<InMemoryWorkflowTriggerBindingStore>()));
        services.AddScoped<IBookmarkStimulusIndex>(sp => new ScopedBookmarkStimulusIndex(sp.GetRequiredService<InMemoryBookmarkStateStore>()));
        services.AddScoped<IGlobalBookmarkStimulusLookup, GlobalBookmarkStimulusLookup>();
    }

    /// <summary>A view of shared state that a disposed scope can no longer use.</summary>
    private abstract class ScopedView<T>(T shared) : IDisposable
    {
        private bool _disposed;

        protected T Shared => _disposed ? throw new ObjectDisposedException(GetType().Name, "Used after its scope ended: a captive dependency.") : shared;

        public void Dispose() => _disposed = true;
    }

    private sealed class ScopedTriggerBindingStore(InMemoryWorkflowTriggerBindingStore shared)
        : ScopedView<IWorkflowTriggerBindingStore>(shared), IWorkflowTriggerBindingStore
    {
        public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default) => Shared.SaveAsync(binding, cancellationToken);
        public ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => Shared.DeleteByArtifactAsync(artifactId, cancellationToken);
        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(WorkflowTriggerBindingPageQuery query, CancellationToken cancellationToken = default) => Shared.ListByStimulusAsync(query, cancellationToken);
        public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(WorkflowTriggerBindingArtifactPageQuery query, CancellationToken cancellationToken = default) => Shared.ListByArtifactAsync(query, cancellationToken);
        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(WorkflowTriggerBindingTypePageQuery query, CancellationToken cancellationToken = default) => Shared.ListByStimulusTypeAsync(query, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListActiveStimulusHashesAsync(string stimulusType, CancellationToken cancellationToken = default) => Shared.ListActiveStimulusHashesAsync(stimulusType, cancellationToken);
        public ValueTask<WorkflowActivationProjectionState> FindActivationStateAsync(string activationId, CancellationToken cancellationToken = default) => Shared.FindActivationStateAsync(activationId, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListServingActivationIdsAsync(string slotId, CancellationToken cancellationToken = default) => Shared.ListServingActivationIdsAsync(slotId, cancellationToken);
    }

    private sealed class ScopedBookmarkStimulusIndex(InMemoryBookmarkStateStore shared)
        : ScopedView<IBookmarkStimulusIndex>(shared), IBookmarkStimulusIndex
    {
        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusPageAsync(BookmarkStimulusPageQuery query, CancellationToken cancellationToken = default) => Shared.ListByStimulusPageAsync(query, cancellationToken);
        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusTypePageAsync(BookmarkStimulusTypePageQuery query, CancellationToken cancellationToken = default) => Shared.ListByStimulusTypePageAsync(query, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListWaitingStimulusHashesByTypeAsync(string stimulusType, DateTimeOffset evaluatedAt, CancellationToken cancellationToken = default) => Shared.ListWaitingStimulusHashesByTypeAsync(stimulusType, evaluatedAt, cancellationToken);
    }
}
