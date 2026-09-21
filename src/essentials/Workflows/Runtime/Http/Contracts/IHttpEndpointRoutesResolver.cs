using Elsa.Http.Core.Models;

namespace Elsa.Workflows.Runtime.Http.Contracts;

/// <summary>
/// Projects the current HTTP-endpoint trigger bindings into the route templates that back the per-shell
/// route table (spec 089 B).
/// </summary>
/// <remarks>
/// Reshaped from the A-era <c>GetRoutes(string path)</c> single-path echo to a listing over the trigger
/// index: this contract's only consumer is <c>Elsa.Workflows.Runtime.Http</c> (the route-table startup task
/// and the publish-time index observer), so the signature change carries no external cost (pre-release,
/// no shim).
/// </remarks>
public interface IHttpEndpointRoutesResolver
{
    /// <summary>
    /// Resolves every distinct HTTP route template currently registered by an HTTP-endpoint trigger binding,
    /// as <see cref="HttpRouteData"/>. Templates are deduplicated; a template shared by several bindings
    /// (e.g. one binding per method) yields a single route.
    /// </summary>
    ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken cancellationToken = default);
}
