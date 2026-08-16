using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Secrets.Tests.Support;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretsApiLifecycleContractTests
{
    private static readonly IReadOnlySet<string> LifecycleEndpoints = new HashSet<string>(StringComparer.Ordinal)
    {
        "POST /secrets",
        "PUT /secrets/{param}",
        "POST /secrets/{param}/rotate",
        "POST /secrets/{param}/revoke",
        "DELETE /secrets/{param}",
        "POST /secrets/{param}/test"
    };

    [Fact]
    public async Task Migrated_lifecycle_http_and_openapi_match_the_immutable_fastendpoints_evidence()
    {
        var beforeHttp = SecretsCompatibilityEvidence.LoadLegacyHttp(LifecycleEndpoints);
        var afterHttp = await SecretsCanaryHost.CaptureAsync(SecretsCompatibilityEvidence.Cases(LifecycleEndpoints));
        await using var host = await SecretsCanaryHost.StartMigratedAsync();
        var beforeOpenApi = SecretsCompatibilityEvidence.LoadLegacyOpenApi(LifecycleEndpoints);
        var afterOpenApi = SecretsCompatibilityEvidence.CaptureOpenApi(
            await host.GetCurrentOpenApiDocumentAsync(), LifecycleEndpoints);

        var result = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = beforeHttp, OpenApi = beforeOpenApi },
            new CompatibilityEvidenceSet { Http = afterHttp, OpenApi = afterOpenApi });

        Assert.True(result.IsCompatible, string.Join(Environment.NewLine, result.Failures));
    }

    [Fact]
    public async Task Create_supports_encrypted_and_configuration_inputs_but_returns_metadata_only()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();
        const string valueMarker = "lifecycle-value-marker";
        const string configurationMarker = "lifecycle:configuration:marker";

        using var encrypted = await host.Client.SendAsync(JsonRequest(
            HttpMethod.Post,
            "/secrets",
            "write",
            JsonSerializer.Serialize(new { name = "  Lifecycle.Encrypted  ", value = valueMarker, metadata = new { provider = valueMarker }})));
        Assert.Equal(HttpStatusCode.Created, encrypted.StatusCode);
        await AssertSafeAsync(encrypted, valueMarker);
        using var encryptedDocument = JsonDocument.Parse(await encrypted.Content.ReadAsStringAsync());
        Assert.Equal("lifecycle.encrypted", encryptedDocument.RootElement.GetProperty("name").GetString());

        using var configuration = await host.Client.SendAsync(JsonRequest(
            HttpMethod.Post,
            "/secrets",
            "write",
            JsonSerializer.Serialize(new { name = "lifecycle.configuration", storeName = "configuration", configurationKey = configurationMarker })));
        Assert.Equal(HttpStatusCode.Created, configuration.StatusCode);
        await AssertSafeAsync(configuration, configurationMarker);
    }

    [Fact]
    public async Task Update_uses_route_name_as_authority_and_changes_metadata_only()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();
        const string routeName = "lifecycle-route-authority";

        using (var create = await host.Client.SendAsync(JsonRequest(
                   HttpMethod.Post,
                   "/secrets",
                   "write",
                   JsonSerializer.Serialize(new { name = routeName, displayName = "Original", value = "never-returned" }))))
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var update = await host.Client.SendAsync(JsonRequest(
            HttpMethod.Put,
            $"/secrets/{routeName}",
            "write",
            JsonSerializer.Serialize(new { name = "different-body-name", displayName = "Updated", description = "safe description" })));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        await AssertSafeAsync(update, "never-returned");
        using var updatedDocument = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
        Assert.Equal(routeName, updatedDocument.RootElement.GetProperty("name").GetString());
        Assert.Equal("Updated", updatedDocument.RootElement.GetProperty("displayName").GetString());

        using var read = await host.Client.SendAsync(Request(HttpMethod.Get, $"/secrets/{routeName}", "read"));
        using var readDocument = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal("Updated", readDocument.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Duplicate_create_is_rejected_without_overwriting_existing_metadata()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();
        const string name = "lifecycle-duplicate";

        using (var create = await host.Client.SendAsync(JsonRequest(
                   HttpMethod.Post,
                   "/secrets",
                   "write",
                   JsonSerializer.Serialize(new { name, displayName = "Original", value = "original-value" }))))
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var duplicate = await host.Client.SendAsync(JsonRequest(
            HttpMethod.Post,
            "/secrets",
            "write",
            JsonSerializer.Serialize(new { name, displayName = "Overwritten", value = "duplicate-marker" })));
        Assert.True((int)duplicate.StatusCode >= 400);
        await AssertSafeAsync(duplicate, "duplicate-marker");

        using var read = await host.Client.SendAsync(Request(HttpMethod.Get, $"/secrets/{name}", "read"));
        using var document = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal("Original", document.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Rotate_revoke_delete_and_test_preserve_lifecycle_semantics()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();
        const string name = "lifecycle-transitions";

        using (var create = await host.Client.SendAsync(JsonRequest(
                   HttpMethod.Post,
                   "/secrets",
                   "write",
                   JsonSerializer.Serialize(new { name, value = "first-value" }))))
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var rotate = await host.Client.SendAsync(JsonRequest(
            HttpMethod.Post,
            $"/secrets/{name}/rotate",
            "update-value",
            "{\"value\":\"second-value\"}"));
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        using var rotatedDocument = JsonDocument.Parse(await rotate.Content.ReadAsStringAsync());
        Assert.Equal(2, rotatedDocument.RootElement.GetProperty("currentVersion").GetInt32());
        await AssertSafeAsync(rotate, "second-value");

        using var revoke = await host.Client.SendAsync(Request(HttpMethod.Post, $"/secrets/{name}/revoke", "delete"));
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        using var revokedDocument = JsonDocument.Parse(await revoke.Content.ReadAsStringAsync());
        Assert.Equal("revoked", revokedDocument.RootElement.GetProperty("status").GetString());

        using var test = await host.Client.SendAsync(Request(HttpMethod.Post, $"/secrets/{name}/test", "test"));
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        using var testDocument = JsonDocument.Parse(await test.Content.ReadAsStringAsync());
        Assert.False(testDocument.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.Equal("inactive", testDocument.RootElement.GetProperty("code").GetString());

        using var delete = await host.Client.SendAsync(Request(HttpMethod.Delete, $"/secrets/{name}", "delete"));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        using var repeatedDelete = await host.Client.SendAsync(Request(HttpMethod.Delete, $"/secrets/{name}", "delete"));
        Assert.Equal(HttpStatusCode.NotFound, repeatedDelete.StatusCode);
    }

    [Fact]
    public async Task Malformed_or_empty_lifecycle_bodies_fail_without_mutation()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();
        const string name = "lifecycle-malformed";

        using var malformed = await host.Client.SendAsync(JsonRequest(HttpMethod.Post, "/secrets", "write", "{not-json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        using var empty = await host.Client.SendAsync(JsonRequest(HttpMethod.Post, "/secrets", "write", ""));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        using var missing = await host.Client.SendAsync(Request(HttpMethod.Get, $"/secrets/{name}", "read"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
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

    private static async Task AssertSafeAsync(HttpResponseMessage response, string marker)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(marker, body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            response.Headers.Concat(response.Content.Headers).SelectMany(header => header.Value),
            value => value.Contains(marker, StringComparison.Ordinal));
    }
}
