namespace Elsa.Activities.Http.Options;

/// <summary>
/// Configuration for the HTTP endpoint request middleware (<c>HttpEndpointMiddleware</c>). Default-constructable
/// (init-only property) so the standard options pipeline can materialize it and hosts can override it via
/// <c>services.Configure&lt;HttpEndpointOptions&gt;(...)</c>.
/// </summary>
public record HttpEndpointOptions
{
    /// <summary>
    /// The base path under which inbound requests are treated as workflow HTTP endpoints. A request whose path
    /// starts with this prefix has the remainder used as the endpoint route (the stimulus key); other requests
    /// pass through untouched. Defaults to <c>/workflows/http</c>.
    /// </summary>
    public string BasePath { get; init; } = "/workflows/http";
}
