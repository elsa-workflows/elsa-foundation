using System.Collections.Concurrent;
using Elsa.Http.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Http.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// Keeps the per-shell HTTP <see cref="IRouteTable"/> in step with the trigger index (spec 089 B). Registered as
/// an <see cref="IWorkflowTriggerIndexObserver"/>, it runs after the indexer has written an artifact's current
/// bindings and, when that publish could change the HTTP route set, rebuilds the whole route table from the
/// resolver.
/// </summary>
/// <remarks>
/// <para>
/// <b>HTTP-affecting publishes only (spec 089 efficiency #8).</b> The indexer notifies this observer on every
/// publish of <em>any</em> workflow, and a route-table refresh re-lists and deserializes every HTTP-endpoint
/// binding in the durable index. Publishing a workflow that neither declares nor previously declared an HTTP
/// endpoint cannot change the route set, so the observer skips the refresh for it. It decides this from the
/// snapshot's own bindings (<see cref="WorkflowTriggerIndexSnapshot.Bindings"/>) — the just-published set — plus
/// a record of which artifacts it has previously seen contribute HTTP routes: it refreshes when the new set
/// contains an HTTP binding, or when the artifact used to contribute one and now may have dropped it (republish
/// is delete-and-resave, so a removed route must still be reconciled out of the table). This keeps the removal
/// case correct while avoiding a full re-projection for the common non-HTTP publish.
/// </para>
/// <para>
/// <b>Full refresh, not incremental diff.</b> When a refresh is warranted the observer still re-projects the
/// whole table rather than diffing this artifact's routes against its prior set: a full re-projection from the
/// durable index is simpler and always correct (the index is the source of truth). The per-artifact HTTP tracking
/// only gates <em>whether</em> to refresh, never <em>what</em> the refreshed table contains.
/// </para>
/// <para>
/// <b>Lifetime.</b> The indexer is a shell singleton, but the resolver and route table are scoped (the route
/// table's state lives in the shared memory cache, so any scope mutates the same table). This observer is a
/// singleton that opens a fresh scope per notification and resolves the scoped services inside it. An exception
/// propagates and fails the publish, matching the indexer's failure policy. The per-artifact HTTP-contributor
/// record is a process-local hint that only ever <em>widens</em> when a refresh runs, so a fresh process that
/// has not yet seen an artifact simply falls back to refreshing on that artifact's first HTTP binding; the
/// startup task rebuilds the whole table from the durable index on shell start regardless.
/// </para>
/// </remarks>
public sealed class RouteTableTriggerIndexObserver(IServiceScopeFactory scopeFactory) : IWorkflowTriggerIndexObserver
{
    // Artifacts previously seen to contribute at least one HTTP-endpoint binding. Used to catch the removal case:
    // an artifact that dropped its last HTTP route publishes a snapshot with no HTTP binding, but the table still
    // has to reconcile the vanished route out. Only ever added to (under a refresh), never a source of staleness.
    private readonly ConcurrentDictionary<string, byte> _httpContributingArtifacts = new(StringComparer.Ordinal);

    public async ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var declaresHttp = snapshot.Bindings.Any(binding =>
            StringComparer.Ordinal.Equals(binding.StimulusType, HttpEndpointRouting.StimulusType));

        // Skip the re-projection when this publish cannot change the HTTP route set: the new binding set has no
        // HTTP endpoint AND we have never seen this artifact contribute one (so nothing to reconcile out).
        if (!declaresHttp && !_httpContributingArtifacts.ContainsKey(snapshot.ArtifactId))
            return;

        if (declaresHttp)
            _httpContributingArtifacts[snapshot.ArtifactId] = 0;
        else
            _httpContributingArtifacts.TryRemove(snapshot.ArtifactId, out _);

        await using var scope = scopeFactory.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IHttpEndpointRoutesResolver>();
        var routeTable = scope.ServiceProvider.GetRequiredService<IRouteTable>();

        var routes = await resolver.ResolveRoutesAsync(cancellationToken);
        await routeTable.Refresh(routes);
    }
}
