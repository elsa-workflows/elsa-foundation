using CShells.AspNetCore.Features;
using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Tests.Support;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogsApiContractTests
{
    private static readonly string HttpBaselinePath = Path.Join(
        AppContext.BaseDirectory, "Baselines", "structured-logs-http-fastendpoints.json");
    private static readonly string OpenApiBaselinePath = Path.Join(
        AppContext.BaseDirectory, "Baselines", "structured-logs-openapi-fastendpoints.json");

    [Fact]
    public void Committed_fastendpoints_http_baseline_is_complete_and_cursor_safe()
    {
        var expected = BaselineFile.Load<HttpCompatibilityObservation[]>(HttpBaselinePath);

        Assert.Equal(StructuredLogsCompatibilityCases.All.Count, expected.Length);
        Assert.Equal(
            StructuredLogsCompatibilityCases.All.Select(testCase => $"{testCase.Endpoint}|{testCase.Case}").Order(),
            expected.Select(observation => $"{observation.Endpoint}|{observation.Case}").Order());
        Assert.All(expected, observation =>
        {
            Assert.InRange(observation.StatusCode, 200, 599);
            Assert.Equal(observation.StatusCode.ToString(), observation.Status);
            Assert.DoesNotContain("event: dropped", observation.Streaming, StringComparison.Ordinal);
            AssertSafe(observation.Body);
            AssertSafe(observation.Json);
            AssertSafe(CompatibilityJson.Serialize(observation.Headers));
        });
    }

    [Fact]
    public async Task Replacement_non_timing_evidence_is_byte_stable_across_ten_real_captures()
    {
        var stableCases = StructuredLogsCompatibilityCases.All
            .Where(testCase => testCase.Case is not "stream-heartbeat" and not "stream-cancelled")
            .ToArray();
        var captures = new List<string>();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var observations = await StructuredLogsApiHost.CaptureAsync(stableCases);
            captures.Add(CompatibilityJson.Serialize(observations));
        }

        Assert.All(captures, capture => Assert.Equal(captures[0], capture));
    }

    [Fact]
    public async Task Minimal_api_http_capture_matches_the_immutable_fastendpoints_baseline()
    {
        var expected = BaselineFile.Load<HttpCompatibilityObservation[]>(HttpBaselinePath);
        var actual = await StructuredLogsApiHost.CaptureAsync(StructuredLogsCompatibilityCases.All);

        Assert.Equal(CompatibilityJson.Serialize(expected), CompatibilityJson.Serialize(actual));
    }

    [Fact]
    public async Task Replacement_stream_cursors_are_present_valid_and_bounded_without_public_drop_frames()
    {
        await using var host = await StructuredLogsApiHost.StartReplacementAsync(seed: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, StructuredLogsCompatibilityCases.StreamPath);
        request.Headers.TryAddWithoutValidation(StructuredLogsApiHost.IdentityHeader, StructuredLogsApiHost.ExactIdentity);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var capture = await StructuredLogsStreamReader.CaptureAsync(
            host.Client,
            request,
            new StructuredLogsStreamReaderOptions(maxFrames: 1, maxBytes: 16 * 1024),
            cancellation.Token,
            _ => host.AppendAsync(new StructuredLogEntry
            {
                Sequence = 41,
                Timestamp = new DateTimeOffset(2026, 8, 15, 12, 1, 0, TimeSpan.Zero),
                Level = LogLevel.Information,
                Category = "Canary.Cursor",
                Message = "cursor-evidence",
                SourceId = "structured-logs-canary"
            }));

        var cursor = Assert.Single(capture.CursorEvidence);
        Assert.True(cursor.Present);
        Assert.True(cursor.Valid);
        Assert.True(cursor.Bounded);
        Assert.DoesNotContain("event: dropped", capture.FrameText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Committed_openapi_baseline_projects_exactly_the_three_legacy_operations()
    {
        var expected = BaselineFile.Load<OpenApiEvidenceDocument>(OpenApiBaselinePath);
        Assert.Equal(
            [
                "GET /_elsa/studio/diagnostics/structured-logs/recent",
                "GET /_elsa/studio/diagnostics/structured-logs/sources",
                "GET /_elsa/studio/diagnostics/structured-logs/stream"
            ],
            expected.Operations.Select(operation => operation.Endpoint.ToString()).Order(StringComparer.Ordinal));
        Assert.All(expected.Operations, operation =>
        {
            Assert.NotEmpty(operation.Responses);
            AssertSafe(operation.Responses);
            AssertSafe(operation.Schemas);
        });

        await using var host = await StructuredLogsApiHost.StartReplacementAsync();
        var actual = OpenApiEvidenceCapture.Capture(await host.GetCurrentOpenApiDocumentAsync());
        Assert.Equal(CompatibilityJson.Serialize(expected), CompatibilityJson.Serialize(actual));
    }

    [Fact]
    public async Task Replacement_manifest_pins_three_routes_owner_minimal_authoring_and_any_permission_policy()
    {
        await using var host = await StructuredLogsApiHost.StartReplacementAsync();
        var manifest = EndpointManifestBuilder.Capture(
                host.EndpointDataSources,
                new EndpointManifestBuilderOptions(ValidateMetadata: false))
            .Entries
            .Where(entry => entry.Route.Value.Contains("structured-logs", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(3, manifest.Length);
        Assert.Equal(
            [
                "GET /_elsa/studio/diagnostics/structured-logs/recent",
                "GET /_elsa/studio/diagnostics/structured-logs/sources",
                "GET /_elsa/studio/diagnostics/structured-logs/stream"
            ],
            manifest.SelectMany(entry => entry.Identities).Select(identity => identity.ToString()).Order(StringComparer.Ordinal));

        var codec = new PermissionPolicyCodec();
        Assert.All(manifest, entry =>
        {
            Assert.Equal("Elsa.Diagnostics.StructuredLogs", entry.Owner);
            Assert.Equal(EndpointOwnerKind.Module, entry.OwnerKind);
            Assert.Equal(EndpointAuthoringModels.MinimalApi, entry.AuthoringModel);
            Assert.Single(entry.Methods);
            Assert.NotNull(entry.SecurityDisposition);
            var policy = codec.Parse(entry.SecurityDisposition!.Value!);
            Assert.Equal(PermissionPolicyParseStatus.Valid, policy.Status);
            Assert.Equal(PermissionRequirementMode.Any, policy.Descriptor!.Mode);
            Assert.Equal([PermissionKey.Wildcard, "DIAGNOSTICS:STRUCTUREDLOGS"], policy.Descriptor.Permissions);
        });
    }

    [Fact]
    public void Replacement_seam_requires_the_feature_to_implement_iwebshellfeature_and_map_three_routes()
    {
        // This is intentionally red before the production migration: the missing interface is the expected
        // seam failure, and the assertions below pin the exact replacement contract for the next phase.
        Assert.True(
            typeof(IWebShellFeature).IsAssignableFrom(typeof(StructuredLogsFeature)),
            "Expected StructuredLogsFeature to implement CShells IWebShellFeature before mapping replacement routes.");

        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        var feature = new StructuredLogsFeature();
        var mapEndpoints = typeof(StructuredLogsFeature).GetMethod(
            nameof(IWebShellFeature.MapEndpoints),
            [typeof(IEndpointRouteBuilder), typeof(Microsoft.Extensions.Hosting.IHostEnvironment)]);

        Assert.NotNull(mapEndpoints);
        mapEndpoints!.Invoke(feature, [routes, null]);
        var endpoints = routes.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("structured-logs", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(3, endpoints.Length);
        Assert.Equal(3, endpoints.Select(endpoint => endpoint.RoutePattern.RawText).Distinct(StringComparer.Ordinal).Count());
    }

    private static void AssertSafe(string value)
    {
        Assert.DoesNotContain("0H", value, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets", value, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}
