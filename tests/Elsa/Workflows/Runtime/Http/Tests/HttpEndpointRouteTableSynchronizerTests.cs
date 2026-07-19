using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

/// <summary>
/// Unit coverage for <see cref="HttpEndpointRouteTableSynchronizer"/> — the single serialization point every
/// route-table refresh routes through (spec 089 D review fix). Proves that two concurrent refreshes never
/// interleave (the second waits for the first to release the lock) and that a fresh scope is opened per refresh so
/// the resolver reads the durable sources anew.
/// </summary>
public sealed class HttpEndpointRouteTableSynchronizerTests
{
    [Fact]
    public async Task RefreshAsync_SerializesConcurrentRefreshes_SecondWaitsForTheFirst()
    {
        // A resolver that blocks inside its first ResolveRoutesAsync until the test releases it, and signals when a
        // SECOND call is entered. If the synchronizer serialized correctly, the second RefreshAsync cannot enter the
        // resolver while the first holds the lock — so entered2 must NOT complete until the first is released.
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var resolver = new BlockingResolver(async () =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                firstEntered.SetResult();
                await releaseFirst.Task;
            }
            else
            {
                secondEntered.SetResult();
            }
        });

        var synchronizer = Build(resolver);

        var first = synchronizer.RefreshAsync().AsTask();
        await firstEntered.Task; // The first refresh is now inside the resolver, holding the lock.

        var second = synchronizer.RefreshAsync().AsTask();

        // The second must be blocked on the lock: give it a real chance to (wrongly) proceed, then assert it hasn't.
        var raced = await Task.WhenAny(secondEntered.Task, Task.Delay(200));
        Assert.NotSame(secondEntered.Task, raced);
        Assert.False(second.IsCompleted);

        // Release the first; the second is now free to run and both complete.
        releaseFirst.SetResult();
        await Task.WhenAll(first, second);
        await secondEntered.Task;
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RefreshAsync_ResolvesFreshly_ReflectingRoutesAddedBetweenRefreshes()
    {
        var bindings = new Elsa.Workflows.Runtime.Core.Services.InMemoryWorkflowTriggerBindingStore();
        var routeTable = new FakeRouteTable();
        var synchronizer = Synchronizers.Build(bindings, routeTable);

        await bindings.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "first", "GET"));
        await synchronizer.RefreshAsync();
        Assert.Single(routeTable.RouteTemplates);

        await bindings.SaveAsync(Bindings.HttpEndpoint("a2", "n2", "second", "GET"));
        await synchronizer.RefreshAsync();

        Assert.Equal(
            new[] { "first", "second" }.OrderBy(x => x),
            routeTable.RouteTemplates.OrderBy(x => x));
    }

    [Fact]
    public async Task RefreshAsync_ProjectsOnlyTheAuthoritativePublication_AcrossActivationAndCompensation()
    {
        var bindings = new Elsa.Workflows.Runtime.Core.Services.InMemoryWorkflowTriggerBindingStore();
        var routeTable = new FakeRouteTable();
        var synchronizer = Synchronizers.Build(bindings, routeTable);
        var oldBinding = PublicationBinding("publication-old", "artifact-old", "foo");
        var candidateBinding = PublicationBinding("publication-new", "artifact-new", "bar");

        await bindings.PreparePublicationAsync("publication-old", [oldBinding]);
        await bindings.ActivatePublicationAsync("publication-old", replacedPublicationId: null);
        await synchronizer.RefreshAsync();
        Assert.Equal(new[] { "foo" }, routeTable.RouteTemplates);

        // Preparation is deliberately invisible until authority changes.
        await bindings.PreparePublicationAsync("publication-new", [candidateBinding]);
        await synchronizer.RefreshAsync();
        Assert.Equal(new[] { "foo" }, routeTable.RouteTemplates);

        // Activation retires the old projection; compensation performs the inverse authority transition.
        await bindings.ActivatePublicationAsync("publication-new", "publication-old");
        await synchronizer.RefreshAsync();
        Assert.Equal(new[] { "bar" }, routeTable.RouteTemplates);

        await bindings.ActivatePublicationAsync("publication-old", "publication-new");
        await synchronizer.RefreshAsync();
        Assert.Equal(new[] { "foo" }, routeTable.RouteTemplates);
    }

    [Fact]
    public async Task RefreshAsync_PropagatesResolverFailure_AndReleasesTheLock()
    {
        // The resolver throws on the FIRST resolve, then succeeds. The synchronizer must propagate the first failure
        // AND release the lock in its finally — so the second refresh (on the same instance) runs rather than
        // deadlocking on a lock the failed refresh never released.
        var calls = 0;
        var synchronizer = Build(new BlockingResolver(() =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("boom");
            return Task.CompletedTask;
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await synchronizer.RefreshAsync());

        // Would hang here if the lock leaked; the test's own timeout would surface that.
        await synchronizer.RefreshAsync();
        Assert.Equal(2, calls);
    }

    private static HttpEndpointRouteTableSynchronizer Build(IHttpEndpointRoutesResolver resolver, IRouteTable? routeTable = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(resolver);
        services.AddSingleton(routeTable ?? new FakeRouteTable());
        var provider = services.BuildServiceProvider();
        return new HttpEndpointRouteTableSynchronizer(provider.GetRequiredService<IServiceScopeFactory>());
    }

    private static Elsa.Workflows.Runtime.Core.Models.WorkflowTriggerBinding PublicationBinding(
        string publicationId,
        string artifactId,
        string template)
    {
        var binding = Bindings.HttpEndpoint(artifactId, "node-http", template, "GET");
        return binding with
        {
            TriggerBindingId = Elsa.Workflows.Runtime.Core.Models.WorkflowTriggerBinding.BuildId(
                publicationId,
                artifactId,
                binding.ExecutableNodeId,
                binding.StimulusHash),
            PublicationId = publicationId,
            SlotId = "slot-default",
            IsActive = false
        };
    }

    /// <summary>A resolver whose <see cref="ResolveRoutesAsync"/> runs a supplied action, then returns no routes.</summary>
    private sealed class BlockingResolver(Func<Task> onResolve) : IHttpEndpointRoutesResolver
    {
        public async ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken cancellationToken = default)
        {
            await onResolve();
            return Array.Empty<HttpRouteData>();
        }
    }
}
