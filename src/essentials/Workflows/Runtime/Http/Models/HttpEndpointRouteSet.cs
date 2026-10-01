using Elsa.Http.Core.Models;

namespace Elsa.Workflows.Runtime.Http.Models;

/// <summary>
/// The routes a refresh loads into the per-shell route table, with the fingerprint of the durable stimulus identities
/// they were projected from.
/// </summary>
/// <param name="Routes">The routes to load.</param>
/// <param name="Fingerprint">The fingerprint of the stimulus identities read to build <paramref name="Routes"/>, computed from the same rows.</param>
public sealed record HttpEndpointRouteSet(IReadOnlyCollection<HttpRouteData> Routes, string Fingerprint);
