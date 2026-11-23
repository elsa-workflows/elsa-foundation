using Elsa.Api.Compatibility.Testing.Manifests;

namespace Elsa.Api.Compatibility.Testing.Http;

/// <summary>Describes one deterministic request made against a before or after host.</summary>
public sealed record HttpCompatibilityCase
{
    private readonly Func<HttpRequestMessage>? _requestFactory;

    public HttpCompatibilityCase(EndpointIdentity endpoint, string caseName, Func<HttpRequestMessage> requestFactory)
    {
        Endpoint = endpoint;
        Case = Require(caseName, nameof(caseName));
        _requestFactory = requestFactory ?? throw new ArgumentNullException(nameof(requestFactory));
    }

    public HttpCompatibilityCase(EndpointIdentity endpoint, string caseName, HttpMethod method, string path,
        HttpContent? content = null)
        : this(endpoint, caseName, () => new HttpRequestMessage(method, path) { Content = content })
    {
    }

    public EndpointIdentity Endpoint { get; init; }
    public string Case { get; init; }
    public string? Binding { get; init; }
    public string? PagingFiltering { get; init; }
    public bool BoundedStreaming { get; init; }
    public int MaxStreamBytes { get; init; } = 64 * 1024;
    public int MaxStreamFrames { get; init; } = 128;
    public Func<HttpRequestMessage>? RequestFactory => _requestFactory;

    public HttpRequestMessage CreateRequest()
    {
        var request = _requestFactory?.Invoke() ?? new HttpRequestMessage(new HttpMethod(Endpoint.Method.Value), Endpoint.Route.Value);
        request.Method = new HttpMethod(Endpoint.Method.Value);
        return request;
    }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", name) : value.Trim();
}

/// <summary>A canonical, bounded observation of one HTTP compatibility case.</summary>
public sealed record HttpCompatibilityObservation
{
    public required EndpointIdentity Endpoint { get; init; }
    public required string Case { get; init; }
    public string Binding { get; init; } = "";
    public string Json { get; init; } = "";
    public int StatusCode { get; init; }
    public string Status => StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string ContentType { get; init; } = "";
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new SortedDictionary<string, string>(StringComparer.Ordinal);
    public string Body { get; init; } = "";
    public string ProblemDetails { get; init; } = "";
    public string PagingFiltering { get; init; } = "";
    public string Streaming { get; init; } = "";
    public string TerminalState { get; init; } = "Completed";

    public string Facet(string facet) => facet switch
    {
        CompatibilityFacet.Binding => Binding,
        CompatibilityFacet.Json => Json,
        CompatibilityFacet.Status => Status,
        CompatibilityFacet.MediaTypes => ContentType,
        CompatibilityFacet.Headers => Serialization.CompatibilityJson.Serialize(Headers),
        CompatibilityFacet.ProblemDetails => ProblemDetails,
        CompatibilityFacet.PagingFiltering => PagingFiltering,
        CompatibilityFacet.Streaming => Streaming,
        CompatibilityFacet.TerminalState => TerminalState,
        CompatibilityFacet.Body => Body,
        _ => throw new ArgumentException($"Unknown HTTP compatibility facet '{facet}'.", nameof(facet))
    };
}

public static class CompatibilityFacet
{
    public const string Route = "route";
    public const string Method = "method";
    public const string Binding = "binding";
    public const string Json = "json";
    public const string Status = "status";
    public const string MediaTypes = "media-types";
    public const string Headers = "headers";
    public const string ProblemDetails = "problem-details";
    public const string PagingFiltering = "paging-filtering";
    public const string Streaming = "streaming";
    public const string TerminalState = "terminal-state";
    public const string Body = "body";
    public const string OpenApi = "openapi";
}
