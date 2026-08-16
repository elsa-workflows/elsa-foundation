using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;

namespace Elsa.Diagnostics.StructuredLogs.Tests.Support;

/// <summary>
/// Stable, named requests used to capture the current FastEndpoints surface. The request set deliberately
/// contains the binding and stream boundaries that are easy for a framework migration to change silently.
/// </summary>
public static class StructuredLogsCompatibilityCases
{
    public const string RecentPath = "/_elsa/studio/diagnostics/structured-logs/recent";
    public const string SourcesPath = "/_elsa/studio/diagnostics/structured-logs/sources";
    public const string StreamPath = "/_elsa/studio/diagnostics/structured-logs/stream";

    public const string CustomRecentPath = "/canary/structured-logs/recent";
    public const string CustomSourcesPath = "/canary/structured-logs/sources";
    public const string CustomStreamPath = "/canary/structured-logs/stream";

    public static IReadOnlyList<HttpCompatibilityCase> All { get; } =
    [
        Recent("recent-default", RecentPath),
        Recent("recent-filtered", RecentPath + "?minLevel=Warning&category=Canary.Warning&source=structured-logs-canary&take=2"),
        Recent("recent-invalid-level", RecentPath + "?minLevel=NotALevel"),
        Recent("recent-invalid-take", RecentPath + "?take=-1"),
        Recent("recent-take-zero", RecentPath + "?take=0"),
        Recent("recent-repeated-values", RecentPath + "?minLevel=Warning&minLevel=Error&take=2&take=1"),
        Recent("recent-custom-path", CustomRecentPath + "?take=1"),

        Sources("sources-default", SourcesPath),
        Sources("sources-custom-path", CustomSourcesPath),

        Stream("stream-initial-entry", StreamPath),
        Stream("stream-valid-resume", StreamPath),
        Stream("stream-filtered-entry", StreamPath + "?minLevel=Warning&category=Canary.Warning"),
        Stream("stream-malformed-cursor", StreamPath, lastEventId: " "),
        Stream("stream-unavailable-cursor", StreamPath, lastEventId: "not-a-committed-cursor"),
        Stream("stream-heartbeat", StreamPath),
        Stream("stream-cancelled", StreamPath),
        Stream("stream-custom-path", CustomStreamPath)
    ];

    private static HttpCompatibilityCase Recent(string name, string path) =>
        Request(new EndpointIdentity(RouteWithoutQuery(path), "GET"), name, path, binding: "query=minLevel,category,source,take",
            pagingFiltering: Query(path));

    private static HttpCompatibilityCase Sources(string name, string path) =>
        Request(new EndpointIdentity(path, "GET"), name, path, binding: "");

    private static HttpCompatibilityCase Stream(string name, string path, string? lastEventId = null) =>
        Request(new EndpointIdentity(RouteWithoutQuery(path), "GET"), name, path,
            binding: "query=minLevel,category,source;header=Last-Event-ID", pagingFiltering: Query(path),
            lastEventId: lastEventId, boundedStreaming: true, maxStreamFrames: 1, maxStreamBytes: 16 * 1024);

    private static HttpCompatibilityCase Request(
        EndpointIdentity endpoint,
        string name,
        string path,
        string? binding = null,
        string? pagingFiltering = null,
        string? lastEventId = null,
        bool boundedStreaming = false,
        int maxStreamFrames = 128,
        int maxStreamBytes = 64 * 1024) =>
        new(endpoint, name, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation(StructuredLogsApiHost.IdentityHeader, StructuredLogsApiHost.ExactIdentity);
            if (lastEventId is not null)
                request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
            return request;
        })
        {
            Binding = binding,
            PagingFiltering = pagingFiltering,
            BoundedStreaming = boundedStreaming,
            MaxStreamFrames = maxStreamFrames,
            MaxStreamBytes = maxStreamBytes
        };

    private static string RouteWithoutQuery(string path) => path.Split('?', 2)[0];

    private static string Query(string path) => path.Contains('?', StringComparison.Ordinal)
        ? path[(path.IndexOf('?', StringComparison.Ordinal))..]
        : string.Empty;
}
