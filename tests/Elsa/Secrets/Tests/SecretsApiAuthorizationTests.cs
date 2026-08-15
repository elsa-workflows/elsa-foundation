using System.Net;
using Elsa.Secrets.Tests.Support;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretsApiAuthorizationTests
{
    public static IEnumerable<object[]> ReadAuthorizationCases() =>
    [
        ["anonymous", null!, HttpStatusCode.Unauthorized],
        ["missing", "denied", HttpStatusCode.Forbidden],
        ["adjacent", "delete", HttpStatusCode.Forbidden],
        ["exact", "read", HttpStatusCode.OK],
        ["write-implies-read", "write", HttpStatusCode.OK],
        ["wildcard", "wildcard", HttpStatusCode.OK],
        ["untrusted", "untrusted", HttpStatusCode.Unauthorized],
        ["ambiguous", "ambiguous", HttpStatusCode.Forbidden],
        ["resource-denied", "resource-denied", HttpStatusCode.Forbidden],
        ["missing-tenant", "read|no-tenant", HttpStatusCode.Forbidden]
    ];

    [Theory]
    [MemberData(nameof(ReadAuthorizationCases))]
    public async Task Read_authorization_matrix_is_explicit_and_fail_closed(
        string _, string? identity, HttpStatusCode expected)
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();

        using var response = await host.Client.SendAsync(Request(HttpMethod.Get, "/secrets", identity));

        Assert.Equal(expected, response.StatusCode);
    }

    public static IEnumerable<object[]> LifecycleAuthorizationCases() =>
    [
        ["create", HttpMethod.Post, "/secrets", "write", "denied", "{}"],
        ["rotate", HttpMethod.Post, "/secrets/auth-secret/rotate", "update-value", "write", "{\"value\":\"auth-marker\"}"],
        ["delete", HttpMethod.Delete, "/secrets/auth-secret", "delete", "read", null!],
        ["test", HttpMethod.Post, "/secrets/auth-secret/test", "test", "delete", null!]
    ];

    [Theory]
    [MemberData(nameof(LifecycleAuthorizationCases))]
    public async Task Lifecycle_actions_require_their_own_permission(
        string _, HttpMethod method, string path, string exactIdentity, string adjacentIdentity, string? body)
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();

        using var adjacent = await host.Client.SendAsync(Request(method, path, adjacentIdentity, body));
        using var exact = await host.Client.SendAsync(Request(method, path, exactIdentity, body));

        Assert.Equal(HttpStatusCode.Forbidden, adjacent.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, exact.StatusCode);
    }

    [Fact]
    public async Task Rejected_create_does_not_mutate_storage()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();
        const string marker = "authorization-rejected-marker";

        using var response = await host.Client.SendAsync(JsonRequest(
            HttpMethod.Post,
            "/secrets",
            "denied",
            System.Text.Json.JsonSerializer.Serialize(new { name = "authorization-rejected", value = marker })));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var read = await host.Client.SendAsync(Request(HttpMethod.Get, "/secrets/authorization-rejected", "read"));
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    public async Task Missing_tenant_is_forbidden_without_invoking_data_operations()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();

        foreach (var (method, path, identity, body) in new[]
                 {
                     (HttpMethod.Get, "/secrets", "read|no-tenant", (string?)null),
                     (HttpMethod.Post, "/secrets", "write|no-tenant", "{}"),
                     (HttpMethod.Put, "/secrets/auth-secret", "write|no-tenant", "{}"),
                     (HttpMethod.Delete, "/secrets/auth-secret", "delete|no-tenant", (string?)null),
                     (HttpMethod.Post, "/secrets/auth-secret/rotate", "update-value|no-tenant", "{}"),
                     (HttpMethod.Post, "/secrets/auth-secret/revoke", "delete|no-tenant", (string?)null),
                     (HttpMethod.Post, "/secrets/auth-secret/test", "test|no-tenant", (string?)null),
                     (HttpMethod.Post, "/secrets/picker", "read|no-tenant", "{}")
                 })
        {
            using var response = await host.Client.SendAsync(Request(method, path, identity, body));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? identity, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(SecretsCanaryHost.IdentityHeader, identity);
        if (body is not null)
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        return request;
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, string? identity, string body) =>
        Request(method, path, identity, body);
}
