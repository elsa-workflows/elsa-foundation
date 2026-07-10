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
/// binding in the durable index. The observer skips the refresh only when it is provably redundant. It decides
/// this from the snapshot's own bindings (<see cref="WorkflowTriggerIndexSnapshot.Bindings"/>) — the
/// just-published set — plus a process-local set of artifacts <em>positively known to contribute no HTTP
/// routes</em>: a publish whose snapshot declares no HTTP binding is skipped only when a prior successful
/// refresh already ran for that artifact's no-HTTP state in this process, so the table can hold nothing of its
/// to reconcile out. Any other no-HTTP publish — including the first one seen after a process restart —
/// refreshes, because the artifact may have just dropped its last HTTP route (republish is delete-and-resave)
/// and the vanished route must be reconciled out of the table. Steady state, the common case — repeated
/// publishes of non-HTTP workflows — costs exactly one refresh per artifact per process lifetime.
/// </para>
/// <para>
/// <b>Full refresh, not incremental diff.</b> When a refresh is warranted the observer still re-projects the
/// whole table rather than diffing this artifact's routes against its prior set: a full re-projection from the
/// durable index is simpler and always correct (the index is the source of truth). The per-artifact tracking
/// only gates <em>whether</em> to refresh, never <em>what</em> the refreshed table contains.
/// </para>
/// <para>
/// <b>Lifetime.</b> The indexer is a shell singleton, but the resolver and route table are scoped (the route
/// table's state lives in the shared memory cache, so any scope mutates the same table). This observer is a
/// singleton that opens a fresh scope per notification and resolves the scoped services inside it. An exception
/// propagates and fails the publish, matching the indexer's failure policy — and the known-non-HTTP set is
/// mutated only <em>after</em> a successful refresh, so a failed refresh leaves no memory that could make the
/// retried publish skip. The set is purely a skip hint: losing it on restart costs one extra refresh per
/// non-HTTP artifact, never a stale route. The startup task rebuilds the whole table from the durable index on
/// shell start regardless.
/// </para>
/// </remarks>
public sealed class RouteTableTriggerIndexObserver(IServiceScopeFactory scopeFactory) : IWorkflowTriggerIndexObserver
{
    // Artifacts POSITIVELY known (in this process) to contribute no HTTP routes: a successful refresh already ran
    // for their latest no-HTTP publish, so a repeat no-HTTP publish has nothing to reconcile and can skip.
    // Deliberately inverted from "known HTTP contributors": absence means "unknown — refresh to be safe", which
    // stays correct across restarts (a fresh process refreshes once per artifact before skipping) and across
    // failed refreshes (the set is mutated only after success, so a retried publish refreshes again).
    private readonly ConcurrentDictionary<string, byte> _knownNonHttpArtifacts = new(StringComparer.Ordinal);

    public async ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var declaresHttp = snapshot.Bindings.Any(binding =>
            StringComparer.Ordinal.Equals(binding.StimulusType, HttpEndpointRouting.StimulusType));

        // Skip only when provably redundant: this publish declares no HTTP binding AND a prior successful refresh
        // already covered this artifact's no-HTTP state, so the table holds nothing of its to reconcile out.
        if (!declaresHttp && _knownNonHttpArtifacts.ContainsKey(snapshot.ArtifactId))
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IHttpEndpointRoutesResolver>();
        var routeTable = scope.ServiceProvider.GetRequiredService<IRouteTable>();

        var routes = await resolver.ResolveRoutesAsync(cancellationToken);
        await routeTable.Refresh(routes);

        // Record the outcome only after the refresh succeeded — a throw above fails the publish and must leave the
        // memory untouched so the retried publish refreshes again.
        if (declaresHttp)
            _knownNonHttpArtifacts.TryRemove(snapshot.ArtifactId, out _);
        else
            _knownNonHttpArtifacts[snapshot.ArtifactId] = 0;
    }
}
