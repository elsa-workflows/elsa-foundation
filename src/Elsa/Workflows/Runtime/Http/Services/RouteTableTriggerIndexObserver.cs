using Elsa.Http.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Http.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// Keeps the per-shell HTTP <see cref="IRouteTable"/> in step with the trigger index on every publish (spec
/// 089 B). Registered as an <see cref="IWorkflowTriggerIndexObserver"/>, it runs after the indexer has written
/// an artifact's current bindings and rebuilds the whole route table from the resolver.
/// </summary>
/// <remarks>
/// <para>
/// <b>Full refresh, not incremental diff.</b> The snapshot carries only the just-published artifact's bindings,
/// but republish is delete-and-resave — a superseded route can vanish, so a per-artifact add/remove diff would
/// have to reconcile against the artifact's prior route set. A full re-projection from the durable index is
/// simpler, always correct (the index is the source of truth), and cheap at this scale. So the snapshot's
/// bindings are ignored in favour of re-listing every HTTP binding.
/// </para>
/// <para>
/// <b>Lifetime.</b> The indexer is a shell singleton, but the resolver and route table are scoped (the route
/// table's state lives in the shared memory cache, so any scope mutates the same table). This observer is a
/// singleton that opens a fresh scope per notification and resolves the scoped services inside it. An exception
/// propagates and fails the publish, matching the indexer's failure policy.
/// </para>
/// </remarks>
public sealed class RouteTableTriggerIndexObserver(IServiceScopeFactory scopeFactory) : IWorkflowTriggerIndexObserver
{
    public async ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await using var scope = scopeFactory.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IHttpEndpointRoutesResolver>();
        var routeTable = scope.ServiceProvider.GetRequiredService<IRouteTable>();

        var routes = await resolver.ResolveRoutesAsync(cancellationToken);
        await routeTable.Refresh(routes);
    }
}
