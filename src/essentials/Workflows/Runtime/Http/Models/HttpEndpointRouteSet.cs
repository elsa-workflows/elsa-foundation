using Elsa.Http.Core.Models;

namespace Elsa.Workflows.Runtime.Http.Models;

/// <summary>
/// The routes a refresh loads into the per-shell route table, with the fingerprint of the durable stimulus identities
/// they were projected from.
/// </summary>
/// <param name="Routes">The routes to load.</param>
/// <param name="Fingerprint">
/// The fingerprint of the stimulus identities read to build <paramref name="Routes"/>, computed from the same rows, or
/// <c>null</c> when the resolver cannot tell. A route table refreshed without one is rebuilt on every convergence check.
/// </param>
public sealed record HttpEndpointRouteSet(IReadOnlyCollection<HttpRouteData> Routes, string? Fingerprint);
