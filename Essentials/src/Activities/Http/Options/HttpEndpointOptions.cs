namespace Elsa.Activities.Http.Options;

/// <summary>
/// Configuration for the HTTP endpoint request middleware (<c>HttpEndpointMiddleware</c>). Default-constructable
/// so the standard options pipeline can materialize it and hosts can override it via
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
    /// shadow the rest of the host. Configure a decoded path, without percent escapes; capability discovery
    /// rejects escaped mounts because ASP.NET decodes incoming paths before matching this option.
    /// </summary>
    public string BasePath { get; set; } = "/workflows/http";

    private TimeSpan _syncRequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The maximum duration of a synchronous endpoint request after routing selects its settings, including
    /// request-body reading, inline dispatch and response delivery. Defaults to 30 seconds. An authored
    /// RequestTimeout may shorten this limit; hosts must raise this limit to allow longer requests. Cancellation
    /// is propagated through RequestAborted. Cooperating operations return HTTP 408 before response headers start;
    /// a timeout after response start aborts the connection. Async requests retain
    /// their authored timeout only. A finite, positive duration is required.
    /// </summary>
    public TimeSpan SyncRequestTimeout
    {
        get => _syncRequestTimeout;
        set => _syncRequestTimeout = value > TimeSpan.Zero && value.TotalMilliseconds <= uint.MaxValue - 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "The synchronous request timeout must be finite and positive, and at most 4294967294 milliseconds.");
    }

    /// <summary>
    /// The maximum request body size, in bytes, the middleware will read and forward as stimulus input. Bodies
    /// exceeding it are rejected with <c>413 Content Too Large</c> before any dispatch. This is a conservative
    /// transport guard: the stimulus payload is persisted durably on the started instance, so an
    /// unbounded body would become unbounded durable state. An endpoint's authored <c>RequestSizeLimit</c>
    /// overrides it. Defaults to 1 MiB.
    /// </summary>
    public long MaxRequestBodyBytes { get; init; } = 1024 * 1024;
}
