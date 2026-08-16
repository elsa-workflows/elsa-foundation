using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Workflows.Runtime.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class Wave9RuntimeMinimalApiCompositionTests
{
    private static readonly string BaselineDirectory = Path.Join(AppContext.BaseDirectory, "Baselines");

    [Fact]
    public async Task Runtime_minimal_mapper_publishes_all_24_routes_with_baseline_openapi_coverage()
    {
        var receipt = BaselineFile.Load<JsonElement>(Path.Join(BaselineDirectory, "wave9-runtime-before-capture-receipt.json"));
        Assert.Equal(24, receipt.GetProperty("registrationCount").GetInt32());
        Assert.Equal(24, receipt.GetProperty("operationCount").GetInt32());

        await using var host = await RuntimeHost.StartAsync();
        var openApi = OpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync());
        Assert.Equal(24, openApi.Operations.Count);

        var expected = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory, "wave9-runtime-openapi-fastendpoints.json"));
        Assert.Equal(
            expected.Operations.Select(operation => operation.Endpoint.Method.Value + " " + Normalize(operation.Endpoint.Route.Value)).Order(StringComparer.Ordinal),
            openApi.Operations.Select(operation => operation.Endpoint.Method.Value + " " + Normalize(operation.Endpoint.Route.Value)).Order(StringComparer.Ordinal));

        var endpoints = host.Endpoints;
        Assert.Equal(24, endpoints.Count(endpoint => endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model == EndpointAuthoringModels.MinimalApi));
        Assert.All(endpoints.Where(endpoint => endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model == EndpointAuthoringModels.MinimalApi), endpoint =>
        {
            Assert.Equal("Elsa.Workflows.Runtime.Api", endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.Owner);
            Assert.NotNull(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
            Assert.NotNull(endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>());
        });
    }

    [Fact]
    public async Task Runtime_openapi_differences_are_consumed_by_pinned_two_sided_approvals()
    {
        var before = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory, "wave9-runtime-openapi-fastendpoints.json"));
        using var approvalDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Join(BaselineDirectory, "wave9-runtime-openapi-approved-differences.json")));
        var approvals = approvalDocument.RootElement.GetProperty("differences").EnumerateArray().ToArray();
        await using var host = await RuntimeHost.StartAsync();
        var after = OpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync());
        var beforeOperations = BuildOperations(before);
        var afterOperations = BuildOperations(after);
        var approvedFacetKeys = RuntimeOpenApiApprovalValidator.Validate(beforeOperations, afterOperations, approvals);
        var consumedFacetKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in beforeOperations.Keys.Union(afterOperations.Keys, StringComparer.Ordinal))
        {
            Assert.True(beforeOperations.TryGetValue(key, out var beforeOperation), $"Missing before OpenAPI operation '{key}'.");
            Assert.True(afterOperations.TryGetValue(key, out var afterOperation), $"Missing after OpenAPI operation '{key}'.");
            var routeApprovals = approvals.Where(approval => RuntimeOpenApiApprovalValidator.EndpointKey(approval) == key).ToArray();
            foreach (var approval in routeApprovals)
            {
                RuntimeOpenApiApprovalValidator.RemoveApprovedFacets(beforeOperation!, afterOperation!, approval, consumedFacetKeys);
            }

            Assert.True(JsonNode.DeepEquals(beforeOperation, afterOperation),
                $"Unapproved OpenAPI change at {key}. Before={beforeOperation}; After={afterOperation}");
        }

        Assert.Equal(approvedFacetKeys.Order(StringComparer.Ordinal), consumedFacetKeys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Runtime_openapi_approval_validator_rejects_a_mutated_after_facet()
    {
        var before = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory, "wave9-runtime-openapi-fastendpoints.json"));
        using var approvalDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Join(BaselineDirectory, "wave9-runtime-openapi-approved-differences.json")));
        var approvals = approvalDocument.RootElement.GetProperty("differences").EnumerateArray().ToArray();
        await using var host = await RuntimeHost.StartAsync();
        var after = OpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync());
        var beforeOperations = BuildOperations(before);
        var afterOperations = BuildOperations(after);
        var approval = approvals[0];
        var key = RuntimeOpenApiApprovalValidator.EndpointKey(approval);
        afterOperations[key]["requestBody"] = "{\"mutated\":true}";

        var exception = Assert.Throws<RuntimeOpenApiApprovalValidationException>(() =>
            RuntimeOpenApiApprovalValidator.Validate(beforeOperations, afterOperations, approvals));
        Assert.Equal($"route:{key}", exception.Key);
        Assert.Equal($"route:{key}: after requestBody value does not match document.", exception.Message);
    }

    [Fact]
    public void Runtime_openapi_approval_validator_rejects_malformed_facets_with_typed_failures()
    {
        var before = new Dictionary<string, JsonObject>(StringComparer.Ordinal)
        {
            ["GET /sample"] = Operation("GET /sample", "[]", "{}", "{}", "[]")
        };
        var after = new Dictionary<string, JsonObject>(StringComparer.Ordinal)
        {
            ["GET /sample"] = Operation("GET /sample", "[\"application/json\"]", "{\"request\":true}", "{}", "[]")
        };

        AssertApprovalFailure(() => RuntimeOpenApiApprovalValidator.Validate(before, after, [Approval("GET", "/sample", "[\"application/json\"]", "[\"application/json\"]")]),
            "route:GET /sample", "mediaTypes before and after values must differ.");
        AssertApprovalFailure(() => RuntimeOpenApiApprovalValidator.Validate(before, after, [Approval("GET", "/sample", "[]", "[\"application/json\"]", ("extra", JsonValue.Create(true)))]),
            "route:GET /sample", "Unknown approval property 'extra'.");
        AssertApprovalFailure(() => RuntimeOpenApiApprovalValidator.Validate(before, after, [Approval("GET", "/sample", "[]", null)]),
            "route:GET /sample", "Approval facet requires both beforeMediaTypes and afterMediaTypes.");
        AssertApprovalFailure(() => RuntimeOpenApiApprovalValidator.Validate(before, after, [Approval("GET", "/sample", "[]", "[\"application/json\"]"), Approval("GET", "/sample", "[]", "[\"application/json\"]")]),
            "route:GET /sample:mediaTypes", "Duplicate approval facet.");
        AssertApprovalFailure(() => RuntimeOpenApiApprovalValidator.Validate(before, after, [Approval("GET", "/stale", "[]", "[\"application/json\"]")]),
            "route:GET /stale", "Route is absent from the before document.");
        AssertApprovalFailure(() => RuntimeOpenApiApprovalValidator.Validate(before, after, [Approval("GET", "/sample", "[\"wrong\"]", "[\"application/json\"]")]),
            "route:GET /sample", "before mediaTypes value does not match document.");
    }

    [Fact]
    public async Task Every_runtime_route_challenges_anonymous_callers()
    {
        await using var host = await RuntimeHost.StartAsync();
        var runtimeEndpoints = host.Endpoints.Where(endpoint => endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model == EndpointAuthoringModels.MinimalApi).ToArray();
        Assert.Equal(24, runtimeEndpoints.Length);
        foreach (var endpoint in runtimeEndpoints)
        {
            var method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Single() ?? HttpMethods.Get;
            var path = endpoint is RouteEndpoint routeEndpoint
                ? System.Text.RegularExpressions.Regex.Replace(routeEndpoint.RoutePattern.RawText ?? string.Empty, "\\{[^}]+\\}", "sample")
                : throw new Xunit.Sdk.XunitException("Runtime endpoint is missing a route pattern.");
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method is "POST" or "PUT")
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var response = await host.Client.SendAsync(request);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Every_runtime_route_forbids_authenticated_callers_without_the_catalog_action()
    {
        await using var host = await RuntimeHost.StartAsync();
        var runtimeEndpoints = host.Endpoints.Where(endpoint => endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model == EndpointAuthoringModels.MinimalApi).ToArray();
        Assert.Equal(24, runtimeEndpoints.Length);
        foreach (var endpoint in runtimeEndpoints)
        {
            var method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Single() ?? HttpMethods.Get;
            var path = endpoint is RouteEndpoint routeEndpoint
                ? System.Text.RegularExpressions.Regex.Replace(routeEndpoint.RoutePattern.RawText ?? string.Empty, "\\{[^}]+\\}", "sample")
                : throw new Xunit.Sdk.XunitException("Runtime endpoint is missing a route pattern.");
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Headers.TryAddWithoutValidation("X-Wave9-Authenticated", "true");
            if (method is "POST" or "PUT")
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var response = await host.Client.SendAsync(request);
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public void Runtime_baseline_receipt_is_pinned_and_comparer_detects_http_mutations()
    {
        var receiptPath = Path.Join(BaselineDirectory, "wave9-runtime-before-capture-receipt.json");
        var httpPath = Path.Join(BaselineDirectory, "wave9-runtime-http-fastendpoints.json");
        var receipt = BaselineFile.Load<JsonElement>(receiptPath);
        var observations = BaselineFile.Load<HttpCompatibilityObservation[]>(httpPath);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(httpPath))).ToLowerInvariant();
        Assert.Equal(receipt.GetProperty("httpSha256").GetString(), hash);
        Assert.Equal(receipt.GetProperty("caseCount").GetInt32(), observations.Length);
        Assert.Equal(24, observations.Count(observation => observation.StatusCode == StatusCodes.Status401Unauthorized));
        Assert.Equal(24, observations.Count(observation => observation.Case.EndsWith("|trusted-success", StringComparison.Ordinal)));
        Assert.Equal(24, observations.Select(observation => observation.Endpoint.ToString()).Distinct(StringComparer.Ordinal).Count());

        var before = new CompatibilityEvidenceSet { Http = observations };
        var mutated = observations.Select(observation => observation.Case == "execute|trusted-success"
                ? observation with { StatusCode = StatusCodes.Status201Created }
                : observation).ToArray();
        var result = CompatibilityComparer.Compare(before, new CompatibilityEvidenceSet { Http = mutated });
        var delta = Assert.Single(result.Deltas);
        Assert.Equal(CompatibilityFacet.Status, delta.Facet);
        Assert.False(result.IsCompatible);
    }

    private static string Normalize(string route) =>
        System.Text.RegularExpressions.Regex.Replace(route, "\\{[^}]+\\}", "{param}");

    private static Dictionary<string, JsonObject> BuildOperations(OpenApiEvidenceDocument document) =>
        document.Operations.ToDictionary(operation => operation.Endpoint.ToString(), operation => JsonNode.Parse(operation.Canonical)!.AsObject(), StringComparer.Ordinal);

    private static JsonObject Operation(string endpoint, string mediaTypes, string requestBody, string responses, string schemas) => new()
    {
        ["endpoint"] = endpoint,
        ["mediaTypes"] = mediaTypes,
        ["parameters"] = "[]",
        ["requestBody"] = requestBody,
        ["responses"] = responses,
        ["schemas"] = schemas
    };

    private static JsonElement Approval(string method, string path, string beforeMediaTypes, string? afterMediaTypes, (string Name, JsonNode Value)? extra = null)
    {
        var node = new JsonObject
        {
            ["method"] = method,
            ["path"] = path,
            ["reason"] = "test",
            ["beforeMediaTypes"] = JsonNode.Parse(beforeMediaTypes)
        };
        if (afterMediaTypes is not null)
            node["afterMediaTypes"] = JsonNode.Parse(afterMediaTypes);
        if (extra is { } item)
            node[item.Name] = item.Value;
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    private static void AssertApprovalFailure(Action action, string key, string reason)
    {
        var exception = Assert.Throws<RuntimeOpenApiApprovalValidationException>(action);
        Assert.Equal(key, exception.Key);
        Assert.Equal($"{key}: {reason}", exception.Message);
    }

    private sealed class RuntimeOpenApiApprovalValidationException(string key, string reason) : Exception($"{key}: {reason}")
    {
        public string Key { get; } = key;
    }

    private static class RuntimeOpenApiApprovalValidator
    {
        private static readonly IReadOnlySet<string> AllowedProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "method", "path", "reason",
            "beforeMediaTypes", "afterMediaTypes",
            "beforeRequestBody", "afterRequestBody",
            "beforeSchemas", "afterSchemas",
            "beforeResponseSchemas", "afterResponseSchemas"
        };

        private static readonly (string Before, string After, string Facet)[] Facets =
        [
            ("beforeMediaTypes", "afterMediaTypes", "mediaTypes"),
            ("beforeRequestBody", "afterRequestBody", "requestBody"),
            ("beforeSchemas", "afterSchemas", "schemas"),
            ("beforeResponseSchemas", "afterResponseSchemas", "responseSchemas")
        ];

        public static string EndpointKey(JsonElement approval) =>
            $"{approval.GetProperty("method").GetString()!.ToUpperInvariant()} {approval.GetProperty("path").GetString()!}";

        public static IReadOnlySet<string> Validate(
            IReadOnlyDictionary<string, JsonObject> beforeOperations,
            IReadOnlyDictionary<string, JsonObject> afterOperations,
            IReadOnlyList<JsonElement> approvals)
        {
            var facetKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var approval in approvals)
            {
                var method = approval.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
                var path = approval.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
                var key = $"route:{method?.ToUpperInvariant() ?? "<unknown>"} {path ?? "<unknown>"}";
                Require(method is not null, key, "Approval is missing 'method'.");
                Require(path is not null, key, "Approval is missing 'path'.");
                ValidateProperties(approval, key);
                var endpointKey = $"{method!.ToUpperInvariant()} {path}";
                Require(beforeOperations.TryGetValue(endpointKey, out var beforeOperation), key, "Route is absent from the before document.");
                Require(afterOperations.TryGetValue(endpointKey, out var afterOperation), key, "Route is absent from the after document.");

                var recognized = 0;
                foreach (var (beforeName, afterName, facet) in Facets)
                {
                    var hasBefore = approval.TryGetProperty(beforeName, out var before);
                    var hasAfter = approval.TryGetProperty(afterName, out var after);
                    Require(hasBefore == hasAfter, key, $"Approval facet requires both {beforeName} and {afterName}.");
                    if (!hasBefore)
                        continue;

                    Require(!JsonElement.DeepEquals(before, after), key, $"{facet} before and after values must differ.");
                    var facetKey = $"{key}:{facet}";
                    Require(facetKeys.Add(facetKey), facetKey, "Duplicate approval facet.");
                    Require(JsonNode.DeepEquals(ParseApprovalValue(before), GetFacet(beforeOperation!, facet)), key, $"before {facet} value does not match document.");
                    Require(JsonNode.DeepEquals(ParseApprovalValue(after), GetFacet(afterOperation!, facet)), key, $"after {facet} value does not match document.");
                    recognized++;
                }

                Require(recognized > 0, key, "Approval does not declare a recognized changed facet.");
            }

            return facetKeys;
        }

        public static void RemoveApprovedFacets(JsonObject beforeOperation, JsonObject afterOperation, JsonElement approval, ISet<string> consumedFacetKeys)
        {
            var key = EndpointKey(approval);
            foreach (var (beforeName, afterName, facet) in Facets)
            {
                if (!approval.TryGetProperty(beforeName, out _))
                    continue;

                consumedFacetKeys.Add($"route:{key}:{facet}");
                if (facet == "responseSchemas")
                {
                    RemoveResponseSchemas(beforeOperation, approval.GetProperty(beforeName));
                    RemoveResponseSchemas(afterOperation, approval.GetProperty(afterName));
                }
                else
                {
                    beforeOperation.Remove(facet);
                    afterOperation.Remove(facet);
                }
            }
        }

        private static void ValidateProperties(JsonElement approval, string key)
        {
            foreach (var property in approval.EnumerateObject())
                Require(AllowedProperties.Contains(property.Name), key, $"Unknown approval property '{property.Name}'.");
        }

        private static JsonNode ParseApprovalValue(JsonElement value) => JsonNode.Parse(value.GetRawText())!;

        private static JsonNode GetFacet(JsonObject operation, string facet) =>
            facet == "responseSchemas"
                ? GetResponseSchemas(operation)
                : JsonNode.Parse(operation[facet]!.GetValue<string>())!;

        private static JsonObject GetResponseSchemas(JsonObject operation)
        {
            var responses = JsonNode.Parse(operation["responses"]!.GetValue<string>())!.AsObject();
            var result = new JsonObject();
            foreach (var (status, responseNode) in responses)
            {
                var content = responseNode?["content"]?.AsObject();
                if (content is null)
                    continue;

                var mediaTypes = new JsonObject();
                foreach (var (mediaType, mediaNode) in content)
                {
                    var schema = mediaNode?["schema"];
                    if (schema is not null)
                        mediaTypes[mediaType] = schema.DeepClone();
                }

                if (mediaTypes.Count > 0)
                    result[status] = mediaTypes;
            }

            return result;
        }

        private static void RemoveResponseSchemas(JsonObject operation, JsonElement approval)
        {
            var responses = JsonNode.Parse(operation["responses"]!.GetValue<string>())!.AsObject();
            foreach (var status in approval.EnumerateObject())
            {
                var content = responses[status.Name]?["content"]?.AsObject();
                if (content is null)
                    continue;
                foreach (var mediaType in status.Value.EnumerateObject())
                    content[mediaType.Name]?.AsObject().Remove("schema");
            }

            operation["responses"] = responses.ToJsonString();
        }

        private static void Require(bool condition, string key, string reason)
        {
            if (!condition)
                throw new RuntimeOpenApiApprovalValidationException(key, reason);
        }
    }

    private sealed class RuntimeHost(IHost host) : IAsyncDisposable
    {
        public HttpClient Client { get; } = host.GetTestClient();
        public IReadOnlyList<Endpoint> Endpoints => host.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        public static async Task<RuntimeHost> StartAsync()
        {
            var host = new HostBuilder().ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.ApplicationKey, "Elsa.Workflows.Runtime.Api");
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddOpenApi();
                    services.AddAuthentication(RuntimeAuthentication.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, RuntimeAuthentication>(RuntimeAuthentication.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>([RuntimeAuthentication.SchemeName], StringComparer.Ordinal));
                    new WorkflowsRuntimeApiFeature().ConfigureServices(services);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(endpoints);
                        endpoints.MapOpenApi();
                    });
                });
            }).Build();
            await host.StartAsync();
            return new(host);
        }

        public async Task<string> GetOpenApiAsync()
        {
            using var response = await Client.GetAsync("/openapi/v1.json");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync();
            host.Dispose();
        }
    }

    private sealed class RuntimeAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Wave9Runtime";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Wave9-Authenticated"))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(Scheme.Name);
            identity.AddClaim(new Claim(IdentityClaimTypes.Normalized, "v1"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
