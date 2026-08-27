using System.Text.Json;
using CShells.AspNetCore.Features;
using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Secrets.Api.Features;
using Elsa.Secrets.Core.Permissions;
using Elsa.Secrets.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretsApiContractTests
{
    private static readonly string HttpBaselinePath = Path.Join(
        AppContext.BaseDirectory, "Baselines", "secrets-http-fastendpoints.json");
    private static readonly string OpenApiBaselinePath = Path.Join(
        AppContext.BaseDirectory, "Baselines", "secrets-openapi-fastendpoints.json");
    private static readonly string ManifestBaselinePath = Path.Join(
        AppContext.BaseDirectory, "Baselines", "secrets-manifest-fastendpoints.json");

    private static readonly IReadOnlyDictionary<string, string> ExpectedPermissions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GET /secrets"] = SecretsPermissions.Read,
            ["POST /secrets"] = SecretsPermissions.Write,
            ["GET /secrets/descriptors"] = SecretsPermissions.Read,
            ["POST /secrets/picker"] = SecretsPermissions.Read,
            ["DELETE /secrets/{param}"] = SecretsPermissions.Delete,
            ["GET /secrets/{param}"] = SecretsPermissions.Read,
            ["PUT /secrets/{param}"] = SecretsPermissions.Write,
            ["POST /secrets/{param}/revoke"] = SecretsPermissions.Delete,
            ["POST /secrets/{param}/rotate"] = SecretsPermissions.UpdateValue,
            ["POST /secrets/{param}/test"] = SecretsPermissions.Test
        };

    [Fact]
    public void Committed_fastendpoints_http_baseline_is_complete_stable_and_safe()
    {
        var expected = BaselineFile.Load<HttpCompatibilityObservation[]>(HttpBaselinePath);

        Assert.Equal(SecretsCompatibilityCases.All.Count, expected.Length);
        Assert.Equal(
            SecretsCompatibilityCases.All.Select(testCase => $"{testCase.Endpoint}|{testCase.Case}").Order(),
            expected.Select(observation => $"{observation.Endpoint}|{observation.Case}").Order());
        Assert.All(expected, observation =>
        {
            Assert.InRange(observation.StatusCode, 200, 599);
            Assert.Equal(observation.StatusCode.ToString(), observation.Status);
            AssertSafe(observation.Body);
            AssertSafe(observation.Json);
            AssertSafe(observation.ProblemDetails);
            AssertSafe(CompatibilityJson.Serialize(observation.Headers));
        });

        var captures = Enumerable.Range(0, 10)
            .Select(_ => CompatibilityJson.Serialize(BaselineFile.Load<HttpCompatibilityObservation[]>(HttpBaselinePath)))
            .ToArray();
        Assert.All(captures, capture => Assert.Equal(captures[0], capture));
    }

    [Fact]
    public void Committed_openapi_baseline_has_the_exact_consumed_surface_and_safe_response_models()
    {
        var expected = BaselineFile.Load<OpenApiEvidenceDocument>(OpenApiBaselinePath);

        Assert.Equal(ExpectedPermissions.Keys.Order(StringComparer.Ordinal),
            expected.Operations.Select(operation => operation.Endpoint.ToString()).Order(StringComparer.Ordinal));
        Assert.All(expected.Operations, operation =>
        {
            Assert.NotEmpty(operation.Responses);
            Assert.NotEmpty(operation.Schemas);
            AssertSafe(operation.Responses);

            using var schemas = JsonDocument.Parse(operation.Schemas);
            foreach (var schemaName in new[]
                     {
                         "ListSecretsResponse", "SecretMetadata", "SecretDescriptorsResponse",
                         "SecretPickerResponse", "SecretTestResult"
                     })
            {
                if (!schemas.RootElement.TryGetProperty(schemaName, out var responseSchema))
                    continue;
                var json = responseSchema.GetRawText();
                Assert.DoesNotContain("protectedPayload", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("protectedValue", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("configurationKey", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("\"value\"", json, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    [Fact]
    public void Committed_fastendpoints_manifest_pins_routes_ownership_authoring_and_permission_policies()
    {
        using var manifest = JsonDocument.Parse(BaselineFile.Read(ManifestBaselinePath));
        var entries = manifest.RootElement.GetProperty("entries").EnumerateArray().ToArray();

        Assert.Equal(10, entries.Length);
        Assert.Equal(ExpectedPermissions.Keys.Order(StringComparer.Ordinal),
            entries.Select(Identity).Order(StringComparer.Ordinal));
        Assert.All(entries, entry =>
        {
            Assert.Equal("Elsa.Secrets.Api", entry.GetProperty("owner").GetString());
            Assert.Equal("Module", entry.GetProperty("ownerKind").GetString());
            Assert.Equal(EndpointAuthoringModels.FastEndpoints, entry.GetProperty("authoringModel").GetString());
            Assert.StartsWith("ElsaSecretsApiEndpointsSecrets", entry.GetProperty("sourceIdentity").GetString(), StringComparison.Ordinal);
            var security = entry.GetProperty("securityDisposition");
            Assert.Equal("Permission", security.GetProperty("kind").GetString());
            var identity = Identity(entry);
            var parsed = new PermissionPolicyCodec().Parse(security.GetProperty("value").GetString()!);
            Assert.Equal(PermissionPolicyParseStatus.Valid, parsed.Status);
            Assert.Equal(PermissionRequirementMode.Any, parsed.Descriptor!.Mode);
            Assert.Equal(
                [PermissionKey.Normalize(PermissionKey.Wildcard), PermissionKey.Normalize(ExpectedPermissions[identity])],
                parsed.Descriptor.Permissions);
        });

        static string Identity(JsonElement entry) =>
            $"{entry.GetProperty("methods").EnumerateArray().Single().GetString()} {entry.GetProperty("route").GetProperty("value").GetString()}";
    }

    [Fact]
    public void Target_feature_exposes_one_explicit_ten_route_minimal_api_mapper()
    {
        Assert.True(typeof(IWebShellFeature).IsAssignableFrom(typeof(SecretsApiFeature)));

        using var services = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        var feature = new SecretsApiFeature();
        var mapEndpoints = typeof(SecretsApiFeature).GetMethod(
            nameof(IWebShellFeature.MapEndpoints),
            [typeof(IEndpointRouteBuilder), typeof(Microsoft.Extensions.Hosting.IHostEnvironment)]);

        Assert.NotNull(mapEndpoints);
        mapEndpoints.Invoke(feature, [routes, null]);
        var endpoints = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/secrets", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(10, endpoints.Length);
        Assert.Equal(ExpectedPermissions.Keys.Order(StringComparer.Ordinal),
            endpoints.Select(endpoint =>
            {
                var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Single();
                var route = new Elsa.Api.Compatibility.Testing.Manifests.NormalizedRoute(endpoint.RoutePattern.RawText!);
                return $"{method} {route.Value}";
            }).Order(StringComparer.Ordinal));
        Assert.All(endpoints, endpoint =>
        {
            Assert.Equal("Elsa.Secrets.Api", endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId);
            Assert.Equal(EndpointAuthoringModels.MinimalApi,
                endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
        });
    }

    private static void AssertSafe(string value)
    {
        Assert.DoesNotContain(SecretsCanaryHost.SensitiveMarker, value, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretsCanaryHost.ConfigurationValue, value, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretsCanaryHost.ConfigurationKey, value, StringComparison.Ordinal);
        Assert.DoesNotContain("beta-private-value", value, StringComparison.Ordinal);
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}
