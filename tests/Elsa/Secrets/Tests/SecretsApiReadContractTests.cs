using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Secrets.Tests.Support;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretsApiReadContractTests
{
    private static readonly IReadOnlySet<string> ReadEndpoints = new HashSet<string>(StringComparer.Ordinal)
    {
        "GET /secrets",
        "GET /secrets/descriptors",
        "POST /secrets/picker",
        "GET /secrets/{param}"
    };

    [Fact]
    public async Task Migrated_read_http_and_openapi_match_the_immutable_fastendpoints_evidence()
    {
        var beforeHttp = SecretsCompatibilityEvidence.LoadLegacyHttp(ReadEndpoints);
        var afterHttp = await SecretsCanaryHost.CaptureAsync(SecretsCompatibilityEvidence.Cases(ReadEndpoints));
        await using var host = await SecretsCanaryHost.StartMigratedAsync();
        var beforeOpenApi = SecretsCompatibilityEvidence.LoadLegacyOpenApi(ReadEndpoints);
        var afterOpenApi = SecretsCompatibilityEvidence.CaptureOpenApi(
            await host.GetCurrentOpenApiDocumentAsync(), ReadEndpoints);

        var result = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = beforeHttp, OpenApi = beforeOpenApi },
            new CompatibilityEvidenceSet { Http = afterHttp, OpenApi = afterOpenApi });

        Assert.True(result.IsCompatible, string.Join(Environment.NewLine, result.Failures));
    }

    [Fact]
    public async Task List_preserves_filters_paging_tenant_isolation_and_safe_metadata()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();
        const string marker = "read-contract-sensitive-value";

        using (var create = await host.Client.SendAsync(JsonRequest(
                   HttpMethod.Post,
                   "/secrets",
                   "write",
                   JsonSerializer.Serialize(new { name = "read-contract-secret", displayName = "Tenant A", value = marker, metadata = new { providerMarker = marker }}))))
        {
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            await AssertSafeAsync(create, marker);
        }

        using var response = await host.Client.SendAsync(Request(
            HttpMethod.Get,
            "/secrets?search=read-contract&typeNames=text&typeNames=rsa-key&storeNames=encrypted&status=active&activeOnly=true&page=-1&pageSize=999",
            "read"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertSafeAsync(response, marker);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.GetProperty("items");
        Assert.Contains(items.EnumerateArray(), item => item.GetProperty("name").GetString() == "read-contract-secret");
        Assert.DoesNotContain(items.EnumerateArray(), item => item.TryGetProperty("value", out _));
        Assert.DoesNotContain(items.EnumerateArray(), item => item.TryGetProperty("configurationKey", out _));
    }

    [Fact]
    public async Task Get_and_picker_are_tenant_scoped_and_deleted_records_are_not_discoverable()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();
        const string name = "same-name-read-contract";

        using (var createA = await host.Client.SendAsync(JsonRequest(
                   HttpMethod.Post,
                   "/secrets",
                   "write",
                   JsonSerializer.Serialize(new { name, displayName = "Tenant A secret", value = "tenant-a-value" }))))
            Assert.Equal(HttpStatusCode.Created, createA.StatusCode);

        using (var createB = await host.Client.SendAsync(JsonRequest(
                   HttpMethod.Post,
                   "/secrets",
                   "write|tenant-beta",
                   JsonSerializer.Serialize(new { name, displayName = "Tenant B secret", value = "tenant-b-value" }))))
            Assert.Equal(HttpStatusCode.Created, createB.StatusCode);

        using var getA = await host.Client.SendAsync(Request(HttpMethod.Get, $"/secrets/{name}", "read"));
        using var getB = await host.Client.SendAsync(Request(HttpMethod.Get, $"/secrets/{name}", "read|tenant-beta"));
        Assert.Equal(HttpStatusCode.OK, getA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, getB.StatusCode);

        using var aDocument = JsonDocument.Parse(await getA.Content.ReadAsStringAsync());
        using var bDocument = JsonDocument.Parse(await getB.Content.ReadAsStringAsync());
        Assert.Equal("Tenant A secret", aDocument.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("Tenant B secret", bDocument.RootElement.GetProperty("displayName").GetString());
        Assert.NotEqual(
            aDocument.RootElement.GetProperty("tenantId").GetString(),
            bDocument.RootElement.GetProperty("tenantId").GetString());

        using var picker = await host.Client.SendAsync(JsonRequest(
            HttpMethod.Post,
            "/secrets/picker",
            "read",
            JsonSerializer.Serialize(new { search = name, typeNames = new[] { "text" }, storeNames = new[] { "encrypted" }, activeOnly = true })));
        Assert.Equal(HttpStatusCode.OK, picker.StatusCode);
        using var pickerDocument = JsonDocument.Parse(await picker.Content.ReadAsStringAsync());
        Assert.True(pickerDocument.RootElement.GetProperty("canCreateInline").GetBoolean());
        Assert.All(pickerDocument.RootElement.GetProperty("items").EnumerateArray(), item =>
            Assert.Equal(SecretsCanaryHost.AlphaTenant, item.GetProperty("tenantId").GetString()));

        using (var delete = await host.Client.SendAsync(Request(HttpMethod.Delete, $"/secrets/{name}", "delete")))
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using var missing = await host.Client.SendAsync(Request(HttpMethod.Get, $"/secrets/{name}", "read"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Descriptors_are_read_authorized_but_do_not_require_a_tenant_claim()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();

        using var response = await host.Client.SendAsync(Request(HttpMethod.Get, "/secrets/descriptors", "read|no-tenant"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotEmpty(document.RootElement.GetProperty("types").EnumerateArray());
        Assert.NotEmpty(document.RootElement.GetProperty("stores").EnumerateArray());
    }

    [Fact]
    public async Task List_and_get_preserve_singular_filters_and_lifecycle_visibility()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync();

        using var revokedList = await host.Client.SendAsync(Request(
            HttpMethod.Get,
            "/secrets?typeName=text&storeName=encrypted&status=revoked&page=0&pageSize=1",
            "read"));
        Assert.Equal(HttpStatusCode.OK, revokedList.StatusCode);
        using var revokedDocument = JsonDocument.Parse(await revokedList.Content.ReadAsStringAsync());
        Assert.Contains(
            revokedDocument.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "revoked.secret");

        using var expired = await host.Client.SendAsync(Request(HttpMethod.Get, "/secrets/expired.secret", "read"));
        Assert.Equal(HttpStatusCode.OK, expired.StatusCode);
        using var expiredDocument = JsonDocument.Parse(await expired.Content.ReadAsStringAsync());
        Assert.Equal("active", expiredDocument.RootElement.GetProperty("status").GetString());

        using var activeOnly = await host.Client.SendAsync(Request(
            HttpMethod.Get,
            "/secrets?search=expired.secret&activeOnly=true",
            "read"));
        using var activeOnlyDocument = JsonDocument.Parse(await activeOnly.Content.ReadAsStringAsync());
        Assert.Empty(activeOnlyDocument.RootElement.GetProperty("items").EnumerateArray());

        using var deleted = await host.Client.SendAsync(Request(HttpMethod.Get, "/secrets/deleted.secret", "read"));
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
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
        request.Content = JsonContent.Create(JsonDocument.Parse(body).RootElement);
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
