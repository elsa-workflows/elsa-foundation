using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Post-migration contract gates for the Publishing owner. These tests intentionally call the public
/// mapper and compare the resulting route surface with the immutable FastEndpoints-before oracle.
/// They are expected to fail to compile until <c>WorkflowsPublishingApi</c> is introduced.
/// </summary>
public sealed class PublishingApiContractTests
{
    private const string Owner = "Elsa.Workflows.Publishing.Api";

    [Fact]
    public void Publishing_mapper_exposes_exactly_the_frozen_23_operation_manifest()
    {
        using var provider = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(provider);

        WorkflowsPublishingApi.MapWorkflowsPublishingApi(routes);

        var manifest = EndpointManifestBuilder.Capture(routes.DataSources);
        Assert.Equal(23, manifest.Entries.Count);
        Assert.Equal(
            PublishingCompatibilityCases.Manifest
                .Select(route => route.Endpoint.ToString())
                .Order(StringComparer.Ordinal),
            manifest.Entries
                .SelectMany(entry => entry.Identities)
                .Select(identity => identity.ToString())
                .Order(StringComparer.Ordinal));
        Assert.All(manifest.Entries, entry =>
        {
            Assert.Equal(Owner, entry.Owner);
            Assert.Equal(EndpointAuthoringModels.MinimalApi, entry.AuthoringModel);
            Assert.NotNull(entry.SecurityDisposition);
            Assert.Equal(EndpointSecurityDispositionKind.Permission, entry.SecurityDisposition!.Kind);
            Assert.Contains(entry.Responses, response => response.StatusCode == StatusCodes.Status401Unauthorized);
            Assert.Contains(entry.Responses, response => response.StatusCode == StatusCodes.Status403Forbidden);
        });
    }

    [Fact]
    public void Publishing_mapper_publishes_stable_names_tags_permissions_and_wire_types()
    {
        using var provider = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(provider);
        WorkflowsPublishingApi.MapWorkflowsPublishingApi(routes);

        var endpoints = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.Equal(23, endpoints.Length);
        Assert.Equal(23, endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .Count());

        foreach (var endpoint in endpoints)
        {
            var route = PublishingCompatibilityCases.Manifest.Single(candidate =>
                candidate.Endpoint.Method.Value == endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Single() &&
                RouteMatches(candidate.Endpoint.Route.Value, endpoint.RoutePattern.RawText!));
            var name = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
            Assert.False(string.IsNullOrWhiteSpace(name), endpoint.DisplayName);
            Assert.StartsWith("ElsaWorkflowsPublishingApiEndpoints", name!, StringComparison.Ordinal);

            var tags = endpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags ?? [];
            Assert.Equal([Owner], tags);
            Assert.Equal(Owner, endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.Owner);
            Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);

            var authorization = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            var policy = new PermissionPolicyCodec().Parse(authorization.Policy!);
            Assert.Equal(PermissionPolicyParseStatus.Valid, policy.Status);
            var descriptor = Assert.IsType<PermissionPolicyDescriptor>(policy.Descriptor);
            Assert.Equal(PermissionRequirementMode.Single, descriptor.Mode);
            Assert.Equal(
                PermissionKey.Normalize(route.Action == "read"
                    ? Elsa.Foundation.Identity.Abstractions.Authorization.PermissionNames.WorkflowPublishingRead
                    : Elsa.Foundation.Identity.Abstractions.Authorization.PermissionNames.WorkflowPublishingManage),
                Assert.Single(descriptor.Permissions));
            Assert.NotEqual(PermissionKey.Wildcard, descriptor.Permissions[0]);

            var success = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
                .Single(response => response.StatusCode == StatusCodes.Status200OK);
            var responseType = success.Type?.FullName ?? success.Type?.Name ?? string.Empty;
            var responseLeaf = route.Response.Contains('<', StringComparison.Ordinal)
                ? route.Response[(route.Response.LastIndexOf('<') + 1)..^1]
                : route.Response;
            Assert.Contains(responseLeaf, responseType, StringComparison.Ordinal);
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(), response =>
                response.StatusCode == StatusCodes.Status401Unauthorized);
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(), response =>
                response.StatusCode == StatusCodes.Status403Forbidden);

            if (HasRequestMetadata(route))
            {
                var accepts = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAcceptsMetadata>(),
                    metadata => metadata.RequestType is not null);
                Assert.Contains(route.Request, accepts.RequestType?.Name ?? accepts.RequestType?.FullName ?? string.Empty, StringComparison.Ordinal);
                Assert.Contains("application/json", accepts.ContentTypes);
                if (route.Endpoint.Method.Value is "GET" or "DELETE")
                    Assert.Contains("*/*", accepts.ContentTypes);
            }
            else
                Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAcceptsMetadata>());
        }
    }

    [Fact]
    public async Task Frozen_http_and_openapi_corpus_replays_against_the_real_minimal_api_host()
    {
        await using var host = await PublishingMinimalApiHost.StartAsync();
        var afterHttp = new List<HttpCompatibilityObservation>(PublishingCompatibilityCases.All.Count);
        foreach (var testCase in PublishingCompatibilityCases.All)
            afterHttp.Add(await CaptureAsync(host.Client, testCase));

        var afterOpenApi = OpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync(), includeIdentityMetadata: true);
        var publishingOperations = afterOpenApi.Operations
            .Where(operation => operation.Endpoint.Route.Value.StartsWith("/publishing/", StringComparison.Ordinal) ||
                                operation.Endpoint.Route.Value.StartsWith("/design/activities/", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(23, publishingOperations.Length);

        var before = new CompatibilityEvidenceSet
        {
            Http = BaselineFile.Load<HttpCompatibilityObservation[]>(Path.Join(BaselineDirectory, "publishing-http-fastendpoints.json")),
            OpenApi = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory, "publishing-openapi-fastendpoints.json"))
        };
        var after = new CompatibilityEvidenceSet
        {
            Http = afterHttp,
            OpenApi = new OpenApiEvidenceDocument(publishingOperations)
        };

        var approvalsPath = Path.Join(BaselineDirectory, "publishing-approved-differences.json");
        if (!File.Exists(approvalsPath))
            approvalsPath = Path.Join(BaselineDirectory, "publishing-approved-differences.initial.json");
        var approvals = PublishingApprovalRegistry.Load(approvalsPath);
        var httpComparison = CompatibilityComparer.CompareBidirectional(
            before with { OpenApi = null },
            after with { OpenApi = null },
            approvals.Where(approval => approval.Case != "openapi"));
        Assert.True(httpComparison.IsCompatible, string.Join(Environment.NewLine, httpComparison.Deltas.Select(delta =>
            $"{delta.Endpoint} {delta.Case}: {delta.Facet}: {delta.Expected} -> {delta.Actual}")));

        var comparison = CompatibilityComparer.CompareBidirectional(before, after, approvals);
        Assert.True(comparison.IsCompatible, string.Join(Environment.NewLine, comparison.Deltas.Select(delta =>
            $"{delta.Endpoint} {delta.Case}: {delta.Facet}: {delta.Expected} -> {delta.Actual}")));
    }

    [Fact]
    public void Reviewed_openapi_approvals_are_exact_two_sided_and_mutation_bite_proof()
    {
        var approvalsPath = Path.Join(BaselineDirectory, "publishing-approved-differences.json");
        var approvals = PublishingApprovalRegistry.Load(approvalsPath);
        Assert.Equal(46, approvals.Length);
        Assert.Equal(23, approvals.Count(approval => !approval.Reverse));
        Assert.Equal(23, approvals.Count(approval => approval.Reverse));
        Assert.All(approvals, approval =>
        {
            Assert.Equal("openapi", approval.Case);
            Assert.Equal(CompatibilityFacet.OpenApi, approval.Facet);
            Assert.NotEqual(approval.Expected, approval.Actual);
        });

        var first = Assert.Single(approvals.Where(approval => !approval.Reverse).Take(1));
        var reverse = Assert.Single(approvals, approval => approval.Reverse &&
            approval.Endpoint == first.Endpoint && approval.Method == first.Method);
        var before = Evidence(first.Expected);
        var after = Evidence(first.Actual);

        Assert.True(CompatibilityComparer.CompareBidirectional(before, after, [first, reverse]).IsCompatible);
        Assert.Contains("Duplicate approved difference", Failure(before, after, [first, first, reverse]));
        Assert.Contains("Unused approved difference", Failure(before, after,
            [first with { Endpoint = "/publishing/stale" }, reverse]));
        Assert.Contains("Unused approved difference", Failure(before, after,
            [first with { Expected = first.Actual }, reverse]));
        Assert.False(CompatibilityComparer.CompareBidirectional(before, after, [first]).IsCompatible);
        Assert.Contains("Unused approved difference", Failure(before, after,
            [first with { Expected = first.Expected + " " }, reverse]));
        Assert.Contains("Unused approved difference", Failure(before, after,
            [first with { Actual = first.Actual + " " }, reverse]));

        var json = JsonNode.Parse(File.ReadAllText(approvalsPath))!.AsArray();
        json[0]!.AsObject()["unexpectedFacet"] = "ignored-value";
        var exception = Assert.Throws<InvalidDataException>(() =>
            PublishingApprovalRegistry.Parse(json.ToJsonString()));
        Assert.Contains("unexpectedFacet", exception.Message, StringComparison.Ordinal);
    }

    private static string Failure(
        CompatibilityEvidenceSet before,
        CompatibilityEvidenceSet after,
        ApprovedDifference[] approvals) =>
        string.Join(Environment.NewLine,
            CompatibilityComparer.CompareBidirectional(before, after, approvals).Failures);

    private static CompatibilityEvidenceSet Evidence(string canonical)
    {
        var value = JsonNode.Parse(canonical)!.AsObject();
        var endpointValue = value["endpoint"]!.GetValue<string>();
        var separator = endpointValue.IndexOf(' ');
        var endpoint = new EndpointIdentity(endpointValue[(separator + 1)..], endpointValue[..separator]);
        return new CompatibilityEvidenceSet
        {
            OpenApi = new OpenApiEvidenceDocument(
            [
                new OpenApiOperationEvidence
                {
                    Endpoint = endpoint,
                    OperationId = value["operationId"]?.GetValue<string>() ?? string.Empty,
                    Tags = value["tags"]?.GetValue<string>() ?? "[]",
                    Security = value["security"]?.GetValue<string>() ?? "[]",
                    Parameters = value["parameters"]!.GetValue<string>(),
                    RequestBody = value["requestBody"]!.GetValue<string>(),
                    Responses = value["responses"]!.GetValue<string>(),
                    MediaTypes = value["mediaTypes"]!.GetValue<string>(),
                    Schemas = value["schemas"]!.GetValue<string>()
                }
            ])
        };
    }

    private static async Task<HttpCompatibilityObservation> CaptureAsync(HttpClient client, HttpCompatibilityCase testCase)
    {
        try
        {
            return NormalizeVolatileFields(await HttpEvidenceCapture.CaptureAsync(client, testCase));
        }
        catch (Exception exception)
        {
            var terminal = exception.GetBaseException();
            return new HttpCompatibilityObservation
            {
                Endpoint = testCase.Endpoint,
                Case = testCase.Case,
                Binding = testCase.Binding ?? string.Empty,
                PagingFiltering = testCase.PagingFiltering ?? string.Empty,
                StatusCode = 0,
                TerminalState = $"Faulted:{terminal.GetType().FullName}"
            };
        }
    }

    private static HttpCompatibilityObservation NormalizeVolatileFields(HttpCompatibilityObservation observation)
    {
        var headers = observation.Headers
            .Where(header => !string.Equals(header.Key, "date", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(header => header.Key, header => header.Value, StringComparer.Ordinal);
        if (observation.Json.Length == 0)
            return observation with { Headers = headers };

        if (JsonNode.Parse(observation.Json) is not JsonObject body)
            return observation with { Headers = headers };

        var changed = false;
        if (body.ContainsKey("traceId"))
        {
            body["traceId"] = "<volatile-trace-id>";
            changed = true;
        }

        if (body.ContainsKey("preflightToken"))
        {
            body["preflightToken"] = "<volatile-preflight-token>";
            changed = true;
        }

        if (!changed)
            return observation with { Headers = headers };

        var normalized = CompatibilityJson.Canonicalize(body.ToJsonString());
        return observation with
        {
            Headers = headers,
            Json = normalized,
            Body = observation.Body == observation.Json ? normalized : observation.Body,
            ProblemDetails = observation.ProblemDetails == observation.Json ? normalized : observation.ProblemDetails
        };
    }

    private static bool RouteMatches(string expected, string actual) =>
        NormalizeRoute(expected).Equals(NormalizeRoute(actual), StringComparison.Ordinal);

    private static string NormalizeRoute(string value) =>
        Regex.Replace("/" + value.TrimStart('/'), "\\{[^{}]+\\}", "{param}", RegexOptions.CultureInvariant);

    private static bool HasRequestMetadata(PublishingRoute route) => route.Id is not
        ("IncidentStrategies.List" or
        "ValueConversionProfiles.List" or
        "ActivityPublications.GetReceipt" or
        "ActivityTestRuns.Get" or
        "ActivityTestRuns.GetByIdempotencyKey" or
        "ActivityTestRuns.Cancel");

    private static string BaselineDirectory => Path.Join(AppContext.BaseDirectory, "Baselines");

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}

internal static class PublishingApprovalRegistry
{
    private static readonly HashSet<string> AllowedProperties =
    [
        "endpoint", "method", "case", "facet", "expected", "actual", "owner", "reason", "followUp", "reverse"
    ];

    public static ApprovedDifference[] Load(string path) => Parse(File.ReadAllText(path));

    public static ApprovedDifference[] Parse(string json)
    {
        var document = JsonNode.Parse(json)?.AsArray()
            ?? throw new InvalidDataException("The Publishing approval registry must be a JSON array.");
        foreach (var (entry, index) in document.Select((value, index) => (value, index)))
        {
            var approval = entry?.AsObject()
                ?? throw new InvalidDataException($"Publishing approval {index} must be a JSON object.");
            var unknown = approval.Select(property => property.Key)
                .Where(property => !AllowedProperties.Contains(property))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (unknown.Length > 0)
                throw new InvalidDataException(
                    $"Publishing approval {index} contains unknown properties: {string.Join(", ", unknown)}.");
        }

        return CompatibilityJson.Deserialize<ApprovedDifference[]>(json);
    }
}

/// <summary>Real TestServer host used by the post-migration HTTP corpus and behavior tests.</summary>
internal sealed class PublishingMinimalApiHost(WebApplication app) : IAsyncDisposable
{
    public WebApplication App { get; } = app;
    public HttpClient Client { get; } = app.GetTestClient();

    public static async Task<PublishingMinimalApiHost> StartAsync(
        Func<IServiceProvider, IRequestSender>? requestSenderFactory = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = "elsa-workflows-publishing-after-migration"
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddRouting();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddWorkflowRuntime();
        builder.Services.AddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
        builder.Services.AddAuthentication(CaptureAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CaptureAuthenticationHandler>(CaptureAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>([CaptureAuthenticationHandler.SchemeName], StringComparer.Ordinal));
        builder.Services.AddOpenApi();
        builder.Services.AddSingleton<IWorkflowTriggerBindingExtractor>(new WorkflowTriggerBindingExtractor([]));
        builder.Services.AddSingleton<IWorkflowExecutableCompiler, CaptureWorkflowExecutableCompiler>();
        new WorkflowsPublishingFeature().ConfigureServices(builder.Services);
        new WorkflowsPublishingApiFeature().ConfigureServices(builder.Services);
        builder.Services.RemoveAll<TimeProvider>();
        builder.Services.AddSingleton<TimeProvider, CaptureTimeProvider>();
        if (requestSenderFactory is null)
            builder.Services.AddSingleton<IRequestSender, CaptureRequestSender>();
        else
            builder.Services.AddSingleton<IRequestSender>(sp => requestSenderFactory(sp));

        var app = builder.Build();
        _ = app.Services.GetRequiredService<IRequestSender>();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        WorkflowsPublishingApi.MapWorkflowsPublishingApi(app);
        app.MapOpenApi();
        await app.StartAsync();

        var slotStore = app.Services.GetRequiredService<IPublicationSlotStore>();
        await slotStore.TryActivateAsync(
            "definition-route",
            "default",
            "publication-capture",
            expectedRevision: 0,
            new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
        return new PublishingMinimalApiHost(app);
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
        await App.StopAsync();
        await App.DisposeAsync();
    }
}

internal sealed class CaptureAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PublishingAfterCapture";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = Request.Headers[PublishingCompatibilityCases.IdentityHeader].ToString();
        if (string.IsNullOrWhiteSpace(identity))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new ClaimsIdentity(Scheme.Name);
        claims.AddClaim(new Claim(IdentityClaimTypes.Normalized, "v1"));
        claims.AddClaim(new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard));
        claims.AddClaim(new Claim(IdentityClaimTypes.TenantId, "capture-tenant"));
        claims.AddClaim(new Claim(ClaimTypes.NameIdentifier, "capture-actor"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(claims), Scheme.Name)));
    }
}
