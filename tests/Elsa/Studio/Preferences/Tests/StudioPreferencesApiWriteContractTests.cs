using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Studio.Preferences.Tests.Support;
using Xunit;

namespace Elsa.Studio.Preferences.Tests;

public sealed class StudioPreferencesApiWriteContractTests
{
    [Fact]
    public async Task Migrated_put_http_evidence_matches_the_legacy_baseline_with_no_unapproved_differences()
    {
        var before = StudioPreferencesCompatibilityEvidence.LoadLegacyHttp("PUT");
        var after = await StudioPreferencesCanaryHost.CaptureAsync(
            StudioPreferencesCompatibilityCases.All.Where(testCase => testCase.Endpoint.Method.Value == "PUT").ToArray(),
            StudioPreferencesCanarySurface.MigratedMinimalApi);

        var result = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = before },
            new CompatibilityEvidenceSet { Http = after },
            StudioPreferencesCompatibilityEvidence.LoadApprovals());

        Assert.True(result.IsCompatible, string.Join(Environment.NewLine, result.Failures));
    }

    [Fact]
    public async Task Migrated_put_openapi_projection_matches_the_consumed_legacy_operation()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        var generated = Elsa.Api.Compatibility.Testing.OpenApi.OpenApiEvidenceCapture.Capture(
            await host.GetCurrentOpenApiDocumentAsync());
        var after = new OpenApiEvidenceDocument(generated.Operations
            .Where(operation => operation.Endpoint.Method.Value == "PUT" &&
                                operation.Endpoint.Route.Value == "/_elsa/studio/preferences/{param}")
            .ToArray());
        var before = StudioPreferencesCompatibilityEvidence.LoadLegacyOpenApi("PUT");

        var result = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { OpenApi = before },
            new CompatibilityEvidenceSet { OpenApi = after },
            StudioPreferencesCompatibilityEvidence.LoadApprovals());

        Assert.Single(after.Operations);
        Assert.True(result.IsCompatible, string.Join(Environment.NewLine, result.Failures));
    }

    [Fact]
    public async Task Put_creates_and_updates_documents_with_route_namespace_authority_and_quoted_etags()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();

        using var create = await host.Client.SendAsync(PutRequest(
            "write", "attention", StudioPreferencesCanaryHost.HostId,
            ifNoneMatch: "*", body: "{\"namespace\":\"dashboard\",\"schemaVersion\":1,\"value\":{\"badge\":true}}"));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal("\"rev-1\"", create.Headers.ETag?.ToString());
        using var createdDocument = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        Assert.Equal("attention", createdDocument.RootElement.GetProperty("namespace").GetString());

        using var update = await host.Client.SendAsync(PutRequest(
            "write", "dashboard", StudioPreferencesCanaryHost.HostId,
            ifMatch: "\"rev-1\"", body: "{\"namespace\":\"attention\",\"schemaVersion\":1,\"value\":{\"layout\":\"compact\"}}"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("\"rev-2\"", update.Headers.ETag?.ToString());
        using var updatedDocument = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
        Assert.Equal("dashboard", updatedDocument.RootElement.GetProperty("namespace").GetString());
    }

    [Fact]
    public async Task Put_rejects_stale_revisions_without_mutating_storage()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        var before = await host.FindDashboardAsync();

        using var response = await host.Client.SendAsync(PutRequest(
            "write", "dashboard", StudioPreferencesCanaryHost.HostId,
            ifMatch: "\"rev-0\"", body: "{\"schemaVersion\":1,\"value\":{\"layout\":\"changed\"}}"));

        var after = await host.FindDashboardAsync();
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Put_requires_exactly_one_valid_precondition()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();

        using var missing = await host.Client.SendAsync(PutRequest("write", "dashboard", StudioPreferencesCanaryHost.HostId));
        using var ambiguous = await host.Client.SendAsync(PutRequest(
            "write", "dashboard", StudioPreferencesCanaryHost.HostId,
            ifMatch: "\"rev-1\"", ifNoneMatch: "*"));
        using var malformed = await host.Client.SendAsync(PutRequest(
            "write", "dashboard", StudioPreferencesCanaryHost.HostId,
            ifMatch: "rev-1, rev-2"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ambiguous.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, malformed.StatusCode);
    }

    [Fact]
    public async Task Put_rejects_unknown_namespace_and_malformed_host()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();

        using var unknown = await host.Client.SendAsync(PutRequest(
            "write", "missing", StudioPreferencesCanaryHost.HostId,
            ifNoneMatch: "*"));
        using var malformed = await host.Client.SendAsync(PutRequest(
            "write", "dashboard", "host/segment",
            ifMatch: "\"rev-1\""));

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [Fact]
    public async Task Put_rejects_validation_quota_empty_and_malformed_json()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();

        using var validation = await host.Client.SendAsync(PutRequest(
            "write", "dashboard", StudioPreferencesCanaryHost.HostId,
            ifMatch: "\"rev-1\"", body: "{\"schemaVersion\":99,\"value\":{}}"));
        using var quota = await host.Client.SendAsync(PutRequest(
            "write", "dashboard", StudioPreferencesCanaryHost.HostId,
            ifMatch: "\"rev-1\"", body: $"{{\"schemaVersion\":1,\"value\":{{\"blob\":\"{new string('x', 70_000)}\"}}}}"));
        using var empty = await host.Client.SendAsync(PutRequest(
            "write", "dashboard", StudioPreferencesCanaryHost.HostId,
            ifMatch: "\"rev-1\"", body: ""));
        using var malformed = await host.Client.SendAsync(PutRequest(
            "write", "dashboard", StudioPreferencesCanaryHost.HostId,
            ifMatch: "\"rev-1\"", body: "{not-json"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, validation.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, quota.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    private static HttpRequestMessage PutRequest(
        string? identity,
        string @namespace,
        string? hostId,
        string? ifMatch = null,
        string? ifNoneMatch = null,
        string body = "{\"schemaVersion\":1,\"value\":{\"layout\":\"compact\"}}")
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/_elsa/studio/preferences/{@namespace}");
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(StudioPreferencesCanaryHost.IdentityHeader, identity);
        if (hostId is not null)
            request.Headers.TryAddWithoutValidation("X-Elsa-Studio-Host-Id", hostId);
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (ifNoneMatch is not null)
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }
}
