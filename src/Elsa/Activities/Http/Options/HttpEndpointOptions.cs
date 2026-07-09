namespace Elsa.Activities.Http.Options;

/// <summary>
/// Configuration for the HTTP endpoint request middleware (<c>HttpEndpointMiddleware</c>). Default-constructable
/// (init-only properties) so the standard options pipeline can materialize it and hosts can override it via
/// <c>services.Configure&lt;HttpEndpointOptions&gt;(...)</c>.
/// </summary>
public record HttpEndpointOptions
{
    /// <summary>
    /// The base path under which inbound requests are treated as workflow HTTP endpoints. A request whose path
    /// is this prefix followed by a path segment has the remainder used as the endpoint route (the stimulus
    /// key); other requests — including sibling paths that merely share the prefix text — pass through
    /// untouched. Defaults to <c>/workflows/http</c>. An empty or root (<c>/</c>) value disables the middleware
    /// entirely (everything passes through): workflow endpoints require a dedicated base path so they can never
    /// shadow the rest of the host.
    /// </summary>
    public string BasePath { get; init; } = "/workflows/http";

    /// <summary>
    /// The maximum request body size, in bytes, the middleware will read and forward as stimulus input. Bodies
    /// exceeding it are rejected with <c>413 Content Too Large</c> before any dispatch. This is a conservative
    /// transport guard: the stimulus payload is persisted durably on the started instance (spec 089 A), so an
    /// unbounded body would become unbounded durable state. Per-endpoint authored limits arrive with spec 089
    /// sub-unit C. Defaults to 1 MiB.
    /// </summary>
    public long MaxRequestBodyBytes { get; init; } = 1024 * 1024;
}
