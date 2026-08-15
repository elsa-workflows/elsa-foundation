using System.Text;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;

namespace Elsa.Secrets.Tests.Support;

/// <summary>Stable, named before-evidence requests for the current Secrets API.</summary>
public static class SecretsCompatibilityCases
{
    public static IReadOnlyList<HttpCompatibilityCase> All { get; } =
    [
        List("anonymous", null),
        List("denied", "denied|tenant-alpha"),
        List("exact-read", "read|tenant-alpha", "?page=0&pageSize=2&search=shared&typeNames=text&storeNames=encrypted&scope=workflow"),
        List("wildcard", "wildcard|tenant-alpha"),
        List("missing-tenant", "read|no-tenant"),
        List("untrusted", "read|tenant-alpha|untrusted"),
        List("resource-denied", "resource-denied|tenant-alpha"),
        List("tenant-beta", "read|tenant-beta"),

        Create("duplicate-sensitive-marker", "write|tenant-alpha", $"{{\"name\":\"{SecretsCanaryHost.ActiveName}\",\"displayName\":\"Duplicate\",\"value\":\"{SecretsCanaryHost.SensitiveMarker}\"}}"),
        Create("malformed-json", "write|tenant-alpha", "{\"name\":"),
        Create("missing-tenant", "write|no-tenant", $"{{\"name\":\"new.secret\",\"value\":\"{SecretsCanaryHost.SensitiveMarker}\"}}"),

        Descriptors("anonymous", null),
        Descriptors("exact-read", "read|tenant-alpha"),
        Descriptors("wildcard", "wildcard|tenant-alpha"),

        Picker("exact-read-filtered", "read|tenant-alpha", "{\"search\":\"shared\",\"typeNames\":[\"text\"],\"storeNames\":[\"encrypted\"],\"scope\":\"workflow\",\"activeOnly\":true}"),
        Picker("missing-tenant", "read|no-tenant", "{\"activeOnly\":true}"),

        Get("exact-read", "read|tenant-alpha", SecretsCanaryHost.ActiveName),
        Get("missing", "read|tenant-alpha", "missing.secret"),
        Get("cross-tenant", "read|tenant-beta", "configuration.secret"),
        Get("missing-tenant", "read|no-tenant", SecretsCanaryHost.ActiveName),

        Update("exact-write", "write|tenant-alpha", SecretsCanaryHost.ActiveName),
        Update("cross-tenant", "write|tenant-beta", "configuration.secret"),
        Update("malformed-json", "write|tenant-alpha", SecretsCanaryHost.ActiveName, "{\"displayName\":"),

        Rotate("exact-update-value", "update-value|tenant-alpha", SecretsCanaryHost.ActiveName),
        Rotate("cross-tenant", "update-value|tenant-beta", "configuration.secret"),

        Revoke("exact-delete", "delete|tenant-alpha", "configuration.secret"),
        Revoke("missing", "delete|tenant-alpha", "missing.secret"),
        Revoke("cross-tenant", "delete|tenant-beta", "configuration.secret"),

        Delete("exact-delete", "delete|tenant-alpha", "configuration.secret"),
        Delete("missing", "delete|tenant-alpha", "missing.secret"),
        Delete("cross-tenant", "delete|tenant-beta", "configuration.secret"),

        Test("exact-test", "test|tenant-alpha", SecretsCanaryHost.ActiveName),
        Test("inactive", "test|tenant-alpha", "revoked.secret"),
        Test("missing", "test|tenant-alpha", "missing.secret"),
        Test("cross-tenant", "test|tenant-beta", SecretsCanaryHost.ActiveName)
    ];

    private static HttpCompatibilityCase List(string name, string? identity, string query = "") =>
        Request(new EndpointIdentity("/secrets", "GET"), name, HttpMethod.Get, $"/secrets{query}", identity,
            binding: "query=search,typeName,typeNames,storeName,storeNames,scope,status,activeOnly,page,pageSize",
            pagingFiltering: query);

    private static HttpCompatibilityCase Create(string name, string identity, string body) =>
        Request(new EndpointIdentity("/secrets", "POST"), name, HttpMethod.Post, "/secrets", identity,
            Json(body), "body=name,displayName,description,typeName,storeName,scope,tags,value,configurationKey,expiresAt,metadata");

    private static HttpCompatibilityCase Descriptors(string name, string? identity) =>
        Request(new EndpointIdentity("/secrets/descriptors", "GET"), name, HttpMethod.Get, "/secrets/descriptors", identity,
            binding: "");

    private static HttpCompatibilityCase Picker(string name, string identity, string body) =>
        Request(new EndpointIdentity("/secrets/picker", "POST"), name, HttpMethod.Post, "/secrets/picker", identity,
            Json(body), "body=search,typeNames,storeNames,scope,activeOnly");

    private static HttpCompatibilityCase Get(string name, string identity, string secretName) =>
        Request(new EndpointIdentity("/secrets/{name}", "GET"), name, HttpMethod.Get, $"/secrets/{secretName}", identity,
            binding: "route=name");

    private static HttpCompatibilityCase Update(string name, string identity, string secretName, string body = "{\"displayName\":\"Updated display\",\"description\":\"Updated description\"}") =>
        Request(new EndpointIdentity("/secrets/{name}", "PUT"), name, HttpMethod.Put, $"/secrets/{secretName}", identity,
            Json(body), "route=name;body=displayName,description");

    private static HttpCompatibilityCase Rotate(string name, string identity, string secretName) =>
        Request(new EndpointIdentity("/secrets/{name}/rotate", "POST"), name, HttpMethod.Post, $"/secrets/{secretName}/rotate", identity,
            Json($"{{\"value\":\"{SecretsCanaryHost.SensitiveMarker}\",\"metadata\":{{\"marker\":\"{SecretsCanaryHost.SensitiveMarker}\"}}}}"),
            "route=name;body=value,configurationKey,expiresAt,metadata");

    private static HttpCompatibilityCase Revoke(string name, string identity, string secretName) =>
        Request(new EndpointIdentity("/secrets/{name}/revoke", "POST"), name, HttpMethod.Post, $"/secrets/{secretName}/revoke", identity,
            Json("{}"), "route=name;body=empty");

    private static HttpCompatibilityCase Delete(string name, string identity, string secretName) =>
        Request(new EndpointIdentity("/secrets/{name}", "DELETE"), name, HttpMethod.Delete, $"/secrets/{secretName}", identity,
            binding: "route=name");

    private static HttpCompatibilityCase Test(string name, string identity, string secretName) =>
        Request(new EndpointIdentity("/secrets/{name}/test", "POST"), name, HttpMethod.Post, $"/secrets/{secretName}/test", identity,
            Json("{}"), "route=name;body=empty");

    private static HttpCompatibilityCase Request(
        EndpointIdentity endpoint,
        string name,
        HttpMethod method,
        string path,
        string? identity,
        HttpContent? content = null,
        string? binding = null,
        string? pagingFiltering = null) =>
        new(endpoint, name, () =>
        {
            var request = new HttpRequestMessage(method, path) { Content = content is null ? null : Clone(content) };
            if (identity is not null)
                request.Headers.TryAddWithoutValidation(SecretsCanaryHost.IdentityHeader, identity);
            return request;
        })
        {
            Binding = binding,
            PagingFiltering = pagingFiltering
        };

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static HttpContent Clone(HttpContent content)
    {
        // Cases are captured repeatedly; recreate StringContent so each request has a fresh stream.
        var body = content.ReadAsStringAsync().GetAwaiter().GetResult();
        return new StringContent(body, Encoding.UTF8, content.Headers.ContentType?.MediaType ?? "application/json");
    }
}
