using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.OpenApi;
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
using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Contract gates for the Publishing owner. These tests call the public mapper and the real Minimal
/// API host and pin the published route, metadata, and OpenAPI surface.
/// </summary>
public sealed class PublishingApiContractTests
{
    private const string Owner = "Elsa.Workflows.Publishing.Api";

    [Fact]
    public void Publishing_mapper_exposes_exactly_the_current_23_operation_manifest()
    {
        using var provider = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(provider);

        WorkflowsPublishingApi.MapWorkflowsPublishingApi(routes);

        var manifest = EndpointManifestBuilder.Capture(routes.DataSources);
        Assert.Equal(23, manifest.Entries.Count);
        Assert.Equal(
            PublishingCurrentSurface.Manifest
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
        using var provider = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
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
            var route = PublishingCurrentSurface.Manifest.Single(candidate =>
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
                    ? Elsa.Workflows.Publishing.Api.Authorization.WorkflowPublishingPermissions.Read
                    : Elsa.Workflows.Publishing.Api.Authorization.WorkflowPublishingPermissions.Manage),
                Assert.Single(descriptor.Permissions));
            Assert.NotEqual(PermissionKey.Wildcard, descriptor.Permissions[0]);

            var success = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
                .Single(response => response.StatusCode == StatusCodes.Status200OK);
            var responseType = success.Type?.FullName ?? success.Type?.Name ?? string.Empty;
            var declaredResponse = PublishingCurrentSurface.ResponseFor(route);
            var responseLeaf = declaredResponse.Contains('<', StringComparison.Ordinal)
                ? declaredResponse[(declaredResponse.LastIndexOf('<') + 1)..^1]
                : declaredResponse;
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
    public async Task Real_minimal_api_host_publishes_exactly_the_23_publishing_openapi_operations()
    {
        await using var host = await PublishingMinimalApiHost.StartAsync();
        var openApi = OpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync(), includeIdentityMetadata: true);
        var publishingOperations = openApi.Operations
            .Where(operation => operation.Endpoint.Route.Value.StartsWith("/publishing/", StringComparison.Ordinal) ||
                                operation.Endpoint.Route.Value.StartsWith("/design/activities/", StringComparison.Ordinal))
            .Select(operation => operation.Endpoint.ToString())
            .ToArray();

        Assert.Equal(23, publishingOperations.Length);
        Assert.Equal(23, publishingOperations.Distinct(StringComparer.Ordinal).Count());
    }

    private static bool RouteMatches(string expected, string actual) =>
        NormalizeRoute(expected).Equals(NormalizeRoute(actual), StringComparison.Ordinal);

    private static string NormalizeRoute(string value) =>
        Regex.Replace("/" + value.TrimStart('/'), "\\{[^{}]+\\}", "{param}", RegexOptions.CultureInvariant);

    private static bool HasRequestMetadata(PublishingRoute route) => route.Id is not
        ("IncidentStrategies.List" or
        "ValueConversionProfiles.List" or
        "WorkflowExecutable.Export" or
        "ActivityPublications.GetReceipt" or
        "ActivityTestRuns.Get" or
        "ActivityTestRuns.GetByIdempotencyKey" or
        "ActivityTestRuns.Cancel");

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}

/// <summary>Real TestServer host used by the Publishing contract and behavior tests.</summary>
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
        PublishingDomainSeams.Register(builder.Services);
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

        var activationAuthority = app.Services.GetRequiredService<IWorkflowActivationAuthority>();
        await activationAuthority.TryActivateAsync(new WorkflowActivationSlotRequest(
            "definition-route",
            "default",
            "publication-capture",
            WorkflowActivationSource.Publishing,
            ExpectedRevision: 0,
            new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero)));
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
