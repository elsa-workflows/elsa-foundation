using Elsa.Http.Core.Contracts;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Http.Contracts;

namespace Elsa.Workflows.Runtime.Http.Tasks;

/// <summary>
/// Populates the per-shell HTTP <see cref="IRouteTable"/> from the durable trigger index at startup (spec
/// 089 B). The route table is an in-memory projection of the HTTP-endpoint trigger bindings, so a fresh host
/// (or a restart) must rebuild it from the persisted bindings before the endpoint middleware can match any
/// route. Publish-time freshness is handled separately by the index observer; this task covers the cold-start
/// case where no publish has occurred in this process yet.
/// </summary>
public sealed class UpdateRouteTableStartupTask(IHttpEndpointRoutesResolver resolver, IRouteTable routeTable) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var routes = await resolver.ResolveRoutesAsync(cancellationToken);

        // Refresh = clear + populate: the durable index is the source of truth, so a full replace keeps the
        // table exactly in step with it (and is safe to re-run).
        await routeTable.Refresh(routes);
    }
}
