using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Http.Models;

namespace Elsa.Workflows.Runtime.Http.Contracts;

/// <summary>
/// Projects the current HTTP-endpoint trigger bindings into the route templates that back the per-shell
/// route table (spec 089 B).
/// </summary>
/// <remarks>
/// <para>
/// Reshaped from the A-era <c>GetRoutes(string path)</c> single-path echo to a listing over the trigger
/// index: this contract's only consumer is <c>Elsa.Workflows.Runtime.Http</c> (the route-table startup task
/// and the publish-time index observer), so the signature change carries no external cost (pre-release,
/// no shim).
/// </para>
/// <para>
/// <b>Convergence across nodes (#2190).</b> Every node keeps its own route table, so a change made on another node is
/// picked up by comparing <see cref="ResolveRouteFingerprintAsync"/> with the fingerprint the last
/// <see cref="ResolveRouteSetAsync"/> returned. A resolver must implement the two together, over the same sources: the
/// defaults report no fingerprint, and a route table built without one is rebuilt on every convergence check.
/// </para>
/// </remarks>
public interface IHttpEndpointRoutesResolver
{
    /// <summary>
    /// Resolves every distinct HTTP route template currently registered by an HTTP-endpoint trigger binding,
    /// as <see cref="HttpRouteData"/>. Templates are deduplicated; a template shared by several bindings
    /// (e.g. one binding per method) yields a single route.
    /// </summary>
    ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the same routes as <see cref="ResolveRoutesAsync"/> together with the fingerprint of the stimulus
    /// identities they were projected from, computed from the rows that were read rather than from a second read, so
    /// it describes exactly what the route table will hold. The default reports no fingerprint.
    /// </summary>
    async ValueTask<HttpEndpointRouteSet> ResolveRouteSetAsync(CancellationToken cancellationToken = default) =>
        new(await ResolveRoutesAsync(cancellationToken), Fingerprint: null);

    /// <summary>
    /// Reads the fingerprint <see cref="ResolveRouteSetAsync"/> would report now, without projecting any route: the
    /// cheap check a node runs on every convergence interval. Returns <c>null</c> when the resolver cannot tell, which
    /// makes every check a full refresh. The default returns <c>null</c>.
    /// </summary>
    ValueTask<string?> ResolveRouteFingerprintAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<string?>(null);
}
