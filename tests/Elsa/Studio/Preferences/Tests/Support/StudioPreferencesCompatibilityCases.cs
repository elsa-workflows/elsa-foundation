using System.Text;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Studio.Preferences.Api;
using Elsa.Studio.Preferences.Api.Services;

namespace Elsa.Studio.Preferences.Tests.Support;

/// <summary>Stable, named before-evidence requests for the current Studio Preferences API.</summary>
public static class StudioPreferencesCompatibilityCases
{
    public static IReadOnlyList<HttpCompatibilityCase> All { get; } =
    [
        Get("anonymous", null, StudioPreferencesCanaryHost.HostId, "dashboard"),
        Get("denied", "denied", StudioPreferencesCanaryHost.HostId, "dashboard"),
        Get("exact-read", "read", StudioPreferencesCanaryHost.HostId, "dashboard"),
        Get("implied-write-read", "write", StudioPreferencesCanaryHost.HostId, "dashboard"),
        Get("wildcard", "wildcard", StudioPreferencesCanaryHost.HostId, "dashboard"),
        Get("resource-denied", "resource-denied", StudioPreferencesCanaryHost.HostId, "dashboard"),
        Get("unknown-namespace", "read", StudioPreferencesCanaryHost.HostId, "missing"),
        Get("invalid-host", "read", "host/segment", "dashboard"),

        Put("anonymous", null, StudioPreferencesCanaryHost.HostId, "dashboard", "\"rev-1\"", null),
        Put("read-only", "read", StudioPreferencesCanaryHost.HostId, "dashboard", "\"rev-1\"", null),
        Put("exact-write", "write", StudioPreferencesCanaryHost.HostId, "dashboard", "\"rev-1\"", null),
        Put("wildcard", "wildcard", StudioPreferencesCanaryHost.HostId, "dashboard", "\"rev-1\"", null),
        Put("resource-denied", "resource-denied", StudioPreferencesCanaryHost.HostId, "dashboard", "\"rev-1\"", null),
        Put("unknown-namespace", "write", StudioPreferencesCanaryHost.HostId, "missing", "\"rev-1\"", null),
        Put("invalid-host", "write", "host/segment", "dashboard", "\"rev-1\"", null),
        Put("missing-precondition", "write", StudioPreferencesCanaryHost.HostId, "dashboard", null, null),
        Put("stale-revision", "write", StudioPreferencesCanaryHost.HostId, "dashboard", "\"rev-0\"", null),
        Put("validation", "write", StudioPreferencesCanaryHost.HostId, "dashboard", "\"rev-1\"", "{\"schemaVersion\":99,\"value\":{}}"),
        Put("quota", "write", StudioPreferencesCanaryHost.HostId, "dashboard", "\"rev-1\"", QuotaBody())
    ];

    private static HttpCompatibilityCase Get(string name, string? identity, string hostId, string @namespace) =>
        new(new EndpointIdentity("/_elsa/studio/preferences/{namespace}", "GET"), name, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/_elsa/studio/preferences/{@namespace}");
            AddHeaders(request, identity, hostId);
            return request;
        })
        {
            Binding = "route=namespace;header=X-Elsa-Studio-Host-Id",
            PagingFiltering = ""
        };

    private static HttpCompatibilityCase Put(
        string name,
        string? identity,
        string hostId,
        string @namespace,
        string? ifMatch,
        string? body) =>
        new(new EndpointIdentity("/_elsa/studio/preferences/{namespace}", "PUT"), name, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Put, $"/_elsa/studio/preferences/{@namespace}");
            AddHeaders(request, identity, hostId);
            if (ifMatch is not null)
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            request.Content = new StringContent(
                body ?? "{\"schemaVersion\":1,\"value\":{\"layout\":\"compact\"}}",
                Encoding.UTF8,
                "application/json");
            return request;
        })
        {
            Binding = "route=namespace;header=X-Elsa-Studio-Host-Id;body=schemaVersion,value",
            PagingFiltering = ""
        };

    private static void AddHeaders(HttpRequestMessage request, string? identity, string hostId)
    {
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(StudioPreferencesCanaryHost.IdentityHeader, identity);
        request.Headers.TryAddWithoutValidation(StudioPreferenceScopeResolver.StudioHostIdHeader, hostId);
    }

    private static string QuotaBody() => $"{{\"schemaVersion\":1,\"value\":{{\"blob\":\"{new string('x', 70_000)}\"}}}}";
}
