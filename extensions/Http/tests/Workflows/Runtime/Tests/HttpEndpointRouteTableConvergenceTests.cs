using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Models;
using Elsa.Workflows.Runtime.Http.Services;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

/// <summary>
/// <see cref="HttpEndpointRouteTableSynchronizer.ConvergeAsync"/> (#2190): a node's route table picks up endpoints and
/// HTTP bookmarks another node wrote, which reach this node only through the shared stores, never through its observers.
/// Every change below is written straight to the store to stand for that other node. The check must rebuild exactly
/// when the route set moved: a missed rebuild is a silent 404, a needless one is the steady-state cost.
/// </summary>
[Collection(RouteTableTelemetryCollection.Name)]
public sealed class HttpEndpointRouteTableConvergenceTests
{
    private readonly InMemoryWorkflowTriggerBindingStore _bindings = new();
    private readonly InMemoryBookmarkStateStore _bookmarks = new();
    private readonly FakeRouteTable _routeTable = new();
    private readonly HttpEndpointRouteTableSynchronizer _synchronizer;

    public HttpEndpointRouteTableConvergenceTests() =>
        _synchronizer = Synchronizers.Build(_bindings, _routeTable, _bookmarks);

    [Fact]
    public async Task FirstCheck_Refreshes_BecauseNothingHasBeenLoadedYet()
    {
        await _bindings.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders", "GET"));

        Assert.True(await _synchronizer.ConvergeAsync());
        Assert.Equal(["orders"], _routeTable.RouteTemplates);
    }

    [Fact]
    public async Task Check_DoesNotRefresh_WhenNothingChangedSinceTheLastRefresh()
    {
        await _bindings.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders", "GET"));
        await _synchronizer.RefreshAsync();

        Assert.False(await _synchronizer.ConvergeAsync());
        Assert.False(await _synchronizer.ConvergeAsync());
        Assert.Equal(1, _routeTable.RefreshCount);
    }

    [Fact]
    public async Task Check_PicksUpAnEndpointPublishedOnAnotherNode()
    {
        await _synchronizer.RefreshAsync();

        await _bindings.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders", "GET"));

        Assert.True(await _synchronizer.ConvergeAsync());
        Assert.Equal(["orders"], _routeTable.RouteTemplates);
        Assert.False(await _synchronizer.ConvergeAsync());
    }

    [Fact]
    public async Task Check_PicksUpAnHttpBookmarkCreatedOnAnotherNode()
    {
        await _synchronizer.RefreshAsync();

        await _bookmarks.SaveAsync(Bookmarks.HttpEndpoint("wf1", "callbacks/{id}", "POST"));

        Assert.True(await _synchronizer.ConvergeAsync());
        Assert.Equal(["callbacks/{id}"], _routeTable.RouteTemplates);
    }

    [Fact]
    public async Task Check_DropsAnEndpointRetiredOnAnotherNode()
    {
        await _bindings.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders", "GET"));
        await _synchronizer.RefreshAsync();

        await _bindings.DeleteByArtifactAsync("a1");

        Assert.True(await _synchronizer.ConvergeAsync());
        Assert.Empty(_routeTable.RouteTemplates);
    }

    [Fact]
    public async Task Check_DoesNotRefresh_ForChangesThatLeaveTheRouteSetAsItIs()
    {
        // Bookmark churn on a routed template and non-HTTP publishes are the common case; rebuilding for them on every
        // node would be the regression the cheap check exists to avoid.
        await _bindings.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders", "GET"));
        await _synchronizer.RefreshAsync();

        await _bookmarks.SaveAsync(Bookmarks.HttpEndpoint("wf1", "orders", "GET"));
        await _bookmarks.SaveAsync(Bookmarks.HttpEndpoint("wf2", "orders", "GET"));
        await _bindings.SaveAsync(Bindings.Other("a2", "n2"));
        await _bookmarks.SaveAsync(Bookmarks.Other("wf3"));

        Assert.False(await _synchronizer.ConvergeAsync());
        Assert.Equal(1, _routeTable.RefreshCount);
    }

    [Fact]
    public async Task Check_KeepsTheFingerprintOfTheRowsItLoaded_SoARemovalUndoneBeforeTheNextCheckIsNotMissed()
    {
        // The cheap read sees {orders, products, invoices}; the last waiting instance on 'orders' resumes before the
        // refresh reads, so the table is built without it; then a new instance suspends on 'orders' again. Had the
        // check kept the cheap read's fingerprint, the second check would match it and 'orders' would 404 here until
        // some unrelated change.
        var store = new InterceptingBindingStore(_bindings);
        var synchronizer = Synchronizers.Build(store, _routeTable, _bookmarks);
        await _bindings.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "products", "GET"));
        var waiting = Bookmarks.HttpEndpoint("wf1", "orders", "GET");
        await _bookmarks.SaveAsync(waiting);
        await synchronizer.RefreshAsync();

        await _bindings.SaveAsync(Bindings.HttpEndpoint("a2", "n2", "invoices", "GET"));
        store.AfterNextHashRead = () => _bookmarks.DeleteAsync(waiting.WorkflowExecutionId, waiting.BookmarkId).AsTask();
        Assert.True(await synchronizer.ConvergeAsync());
        Assert.Equal(["invoices", "products"], _routeTable.RouteTemplates.Order(StringComparer.Ordinal));

        await _bookmarks.SaveAsync(Bookmarks.HttpEndpoint("wf2", "orders", "GET"));

        Assert.True(await synchronizer.ConvergeAsync());
        Assert.Equal(["invoices", "orders", "products"], _routeTable.RouteTemplates.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Check_Rebuilds_AfterAFailedRefresh_EvenWhenTheIndexIsUnchanged()
    {
        await _bindings.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders", "GET"));
        await _synchronizer.RefreshAsync();

        _routeTable.FailNextRefresh = true;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _synchronizer.RefreshAsync());

        Assert.True(await _synchronizer.ConvergeAsync());
        Assert.Equal(2, _routeTable.RefreshCount);
    }

    [Fact]
    public async Task Check_PropagatesAFailedFingerprintRead_AndRetriesOnTheNextCheck()
    {
        var store = new InterceptingBindingStore(_bindings);
        var synchronizer = Synchronizers.Build(store, _routeTable, _bookmarks);
        await synchronizer.RefreshAsync();
        await _bindings.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders", "GET"));

        store.FailNextHashRead = true;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await synchronizer.ConvergeAsync());
        Assert.Empty(_routeTable.RouteTemplates);

        Assert.True(await synchronizer.ConvergeAsync());
        Assert.Equal(["orders"], _routeTable.RouteTemplates);
    }

    [Fact]
    public async Task Check_HoldsTheRefreshLock_WhileItReadsTheFingerprint()
    {
        var fingerprintEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFingerprint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var synchronizer = Build(new GatedFingerprintResolver(async () =>
        {
            fingerprintEntered.SetResult();
            await releaseFingerprint.Task;
        }));

        var check = synchronizer.ConvergeAsync().AsTask();
        await fingerprintEntered.Task;
        var refresh = synchronizer.RefreshAsync().AsTask();

        Assert.NotSame(refresh, await Task.WhenAny(refresh, Task.Delay(200)));
        releaseFingerprint.SetResult();
        await Task.WhenAll(check, refresh);
        Assert.Equal(2, _routeTable.RefreshCount);
    }

    private HttpEndpointRouteTableSynchronizer Build(IHttpEndpointRoutesResolver resolver)
    {
        var services = new ServiceCollection();
        services.AddSingleton(resolver);
        services.AddSingleton<Elsa.Http.Core.Contracts.IRouteTable>(_routeTable);
        return new HttpEndpointRouteTableSynchronizer(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
    }

    /// <summary>A resolver whose fingerprint read waits on the test before it answers.</summary>
    private sealed class GatedFingerprintResolver(Func<Task> onFingerprint) : IHttpEndpointRoutesResolver
    {
        private const string Fingerprint = "orders";

        public ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<HttpRouteData>>([new("orders")]);

        public async ValueTask<HttpEndpointRouteSet> ResolveRouteSetAsync(CancellationToken cancellationToken = default) =>
            new(await ResolveRoutesAsync(cancellationToken), Fingerprint);

        public async ValueTask<string> ResolveRouteFingerprintAsync(CancellationToken cancellationToken = default)
        {
            await onFingerprint();
            return Fingerprint;
        }
    }

    /// <summary>
    /// A binding store that delegates to <paramref name="inner"/> and lets a test act between the convergence check's
    /// hash read and the refresh read that follows it.
    /// </summary>
    private sealed class InterceptingBindingStore(IWorkflowTriggerBindingStore inner) : IWorkflowTriggerBindingStore
    {
        public Func<Task>? AfterNextHashRead { get; set; }
        public bool FailNextHashRead { get; set; }

        public async ValueTask<IReadOnlyCollection<string>> ListActiveStimulusHashesAsync(string stimulusType, CancellationToken cancellationToken = default)
        {
            if (FailNextHashRead)
            {
                FailNextHashRead = false;
                throw new InvalidOperationException("Simulated hash read failure.");
            }

            var hashes = await inner.ListActiveStimulusHashesAsync(stimulusType, cancellationToken);
            var after = AfterNextHashRead;
            AfterNextHashRead = null;
            if (after is not null)
                await after();
            return hashes;
        }

        public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(binding, cancellationToken);

        public ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) =>
            inner.DeleteByArtifactAsync(artifactId, cancellationToken);

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(WorkflowTriggerBindingPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListByStimulusAsync(query, cancellationToken);

        public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(WorkflowTriggerBindingArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListByArtifactAsync(query, cancellationToken);

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(WorkflowTriggerBindingTypePageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListByStimulusTypeAsync(query, cancellationToken);

        public ValueTask<WorkflowActivationProjectionState> FindActivationStateAsync(string activationId, CancellationToken cancellationToken = default) =>
            inner.FindActivationStateAsync(activationId, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListServingActivationIdsAsync(string slotId, CancellationToken cancellationToken = default) =>
            inner.ListServingActivationIdsAsync(slotId, cancellationToken);
    }
}
