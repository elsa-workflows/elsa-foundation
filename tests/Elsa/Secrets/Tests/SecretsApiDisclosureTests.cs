using System.Net;
using System.Text.Json;
using Elsa.Secrets.Tests.Support;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretsApiDisclosureTests
{
    [Fact]
    public async Task Create_and_rotate_never_echo_sensitive_value_configuration_or_provider_markers()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();
        const string valueMarker = "disclosure-value-marker";
        const string configurationMarker = "disclosure:configuration:marker";
        const string providerMarker = "disclosure-provider-marker";

        using var create = await host.Client.SendAsync(JsonRequest(
            HttpMethod.Post,
            "/secrets",
            "write",
            JsonSerializer.Serialize(new { name = "disclosure-secret", value = valueMarker, configurationKey = configurationMarker, metadata = new { provider = providerMarker }})));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        await AssertDoesNotContainMarkersAsync(create, valueMarker, configurationMarker, providerMarker);

        using var rotate = await host.Client.SendAsync(JsonRequest(
            HttpMethod.Post,
            "/secrets/disclosure-secret/rotate",
            "update-value",
            JsonSerializer.Serialize(new { value = valueMarker + "-rotated", configurationKey = configurationMarker + "-rotated", metadata = new { provider = providerMarker }})));
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        await AssertDoesNotContainMarkersAsync(rotate, valueMarker, configurationMarker, providerMarker);

        using var get = await host.Client.SendAsync(Request(HttpMethod.Get, "/secrets/disclosure-secret", "read"));
        await AssertDoesNotContainMarkersAsync(get, valueMarker, configurationMarker, providerMarker);
        using var document = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.TryGetProperty("value", out _));
        Assert.False(document.RootElement.TryGetProperty("configurationKey", out _));
        Assert.False(document.RootElement.TryGetProperty("payload", out _));
    }

    [Fact]
    public async Task Problem_details_headers_and_openapi_projection_do_not_disclose_sensitive_markers()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();
        const string valueMarker = "disclosure-error-value";
        const string configurationMarker = "disclosure:error:configuration";
        const string providerMarker = "disclosure-error-provider";

        using var duplicate = await host.Client.SendAsync(JsonRequest(
            HttpMethod.Post,
            "/secrets",
            "write",
            JsonSerializer.Serialize(new { name = "duplicate-disclosure", value = valueMarker, configurationKey = configurationMarker, metadata = new { provider = providerMarker }})));
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);

        using var rejected = await host.Client.SendAsync(JsonRequest(
            HttpMethod.Post,
            "/secrets",
            "write",
            JsonSerializer.Serialize(new { name = "duplicate-disclosure", value = valueMarker, configurationKey = configurationMarker, metadata = new { provider = providerMarker }})));
        Assert.True((int)rejected.StatusCode >= 400);
        await AssertDoesNotContainMarkersAsync(rejected, valueMarker, configurationMarker, providerMarker);

        var openApi = await host.GetCurrentOpenApiDocumentAsync();
        Assert.DoesNotContain(valueMarker, openApi, StringComparison.Ordinal);
        Assert.DoesNotContain(configurationMarker, openApi, StringComparison.Ordinal);
        Assert.DoesNotContain(providerMarker, openApi, StringComparison.Ordinal);
        Assert.DoesNotContain("protectedPayload", openApi, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_result_is_safe_and_never_contains_a_resolved_value()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();

        using var test = await host.Client.SendAsync(Request(HttpMethod.Post, "/secrets/revoked.secret/test", "test|tenant-alpha"));
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        var body = await test.Content.ReadAsStringAsync();
        Assert.Contains("succeeded", body, StringComparison.Ordinal);
        Assert.DoesNotContain("value", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", body, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? identity)
    {
        var request = new HttpRequestMessage(method, path);
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(SecretsCanaryHost.IdentityHeader, identity);
        return request;
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, string? identity, string body)
    {
        var request = Request(method, path, identity);
        request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        return request;
    }

    private static async Task AssertDoesNotContainMarkersAsync(HttpResponseMessage response, params string[] markers)
    {
        var body = await response.Content.ReadAsStringAsync();
        var headerValues = response.Headers.Concat(response.Content.Headers).SelectMany(header => header.Value).ToArray();
        foreach (var marker in markers)
        {
            Assert.DoesNotContain(marker, body, StringComparison.Ordinal);
            Assert.DoesNotContain(headerValues, value => value.Contains(marker, StringComparison.Ordinal));
        }
    }
}
