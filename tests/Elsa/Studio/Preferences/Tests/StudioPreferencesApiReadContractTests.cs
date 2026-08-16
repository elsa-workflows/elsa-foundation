using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Studio.Preferences.Tests.Support;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Elsa.Studio.Preferences.Tests;

public sealed class StudioPreferencesApiReadContractTests
{
    [Fact]
    public async Task Migrated_get_http_evidence_matches_the_legacy_baseline_with_no_unapproved_differences()
    {
        var before = StudioPreferencesCompatibilityEvidence.LoadLegacyHttp("GET");
        var after = StudioPreferencesCompatibilityEvidence.NormalizeVolatileFields(
            await StudioPreferencesCanaryHost.CaptureAsync(
                StudioPreferencesCompatibilityCases.All.Where(testCase => testCase.Endpoint.Method.Value == "GET").ToArray()));

        var result = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = before },
            new CompatibilityEvidenceSet { Http = after },
            StudioPreferencesCompatibilityEvidence.LoadApprovals("GET"));

        Assert.True(result.IsCompatible, string.Join(Environment.NewLine, result.Failures));
    }

    [Fact]
    public async Task Migrated_get_openapi_projection_matches_the_consumed_legacy_operation()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        var generated = OpenApiEvidenceCapture.Capture(await host.GetCurrentOpenApiDocumentAsync());
        var after = new OpenApiEvidenceDocument(generated.Operations
            .Where(operation => operation.Endpoint.Method.Value == "GET" &&
                                operation.Endpoint.Route.Value == "/_elsa/studio/preferences/{param}")
            .ToArray());
        var before = StudioPreferencesCompatibilityEvidence.LoadLegacyOpenApi("GET");

        var result = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { OpenApi = before },
            new CompatibilityEvidenceSet { OpenApi = after },
            StudioPreferencesCompatibilityEvidence.LoadApprovals("GET"));

        Assert.Single(after.Operations);
        Assert.True(result.IsCompatible, string.Join(Environment.NewLine, result.Failures));
    }

    [Fact]
    public async Task Get_returns_the_seeded_document_and_quoted_etag()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        using var response = await host.Client.SendAsync(GetRequest("read", StudioPreferencesCanaryHost.HostId, "dashboard"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.ETag is not null);
        Assert.Equal("\"rev-1\"", response.Headers.ETag!.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("dashboard", document.RootElement.GetProperty("namespace").GetString());
        Assert.Equal("wide", document.RootElement.GetProperty("value").GetProperty("layout").GetString());
    }

    [Fact]
    public async Task Get_returns_not_found_for_missing_and_unknown_namespaces()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();

        using var missing = await host.Client.SendAsync(GetRequest("read", StudioPreferencesCanaryHost.HostId, "attention"));
        using var unknown = await host.Client.SendAsync(GetRequest("read", StudioPreferencesCanaryHost.HostId, "missing"));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Get_rejects_missing_and_malformed_host_ids_with_bad_request()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();

        using var missing = await host.Client.SendAsync(GetRequest("read", null, "dashboard"));
        using var malformed = await host.Client.SendAsync(GetRequest("read", "host/segment", "dashboard"));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    private static HttpRequestMessage GetRequest(string? identity, string? hostId, string @namespace)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/_elsa/studio/preferences/{@namespace}");
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(StudioPreferencesCanaryHost.IdentityHeader, identity);
        if (hostId is not null)
            request.Headers.TryAddWithoutValidation("X-Elsa-Studio-Host-Id", hostId);
        return request;
    }

}
