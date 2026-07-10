using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Http.Contracts;

namespace Elsa.Workflows.Runtime.Http.Tasks;

/// <summary>
/// Populates the per-shell HTTP route table from the durable trigger index at startup (spec 089 B). The route table
/// is an in-memory projection of the HTTP-endpoint trigger bindings (∪ waiting bookmarks), so a fresh host (or a
/// restart) must rebuild it before the endpoint middleware can match any route. Publish-time and run-time freshness
/// are handled separately by the index/bookmark observers; this task covers the cold-start case where no
/// publish/suspension has been observed in this process yet.
/// </summary>
/// <remarks>
/// Delegates the read-then-swap to the shared <see cref="IHttpEndpointRouteTableSynchronizer"/> so the startup
/// rebuild is serialized against any concurrent observer refresh under the same lock (spec 089 D review fix);
/// a throw propagates as before.
/// </remarks>
public sealed class UpdateRouteTableStartupTask(IHttpEndpointRouteTableSynchronizer synchronizer) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        synchronizer.RefreshAsync(cancellationToken).AsTask();
}
