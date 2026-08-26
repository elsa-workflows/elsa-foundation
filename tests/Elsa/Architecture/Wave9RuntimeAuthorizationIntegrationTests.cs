using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Endpoints;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Xunit;

namespace Elsa.Architecture.Tests;

[Collection(Wave9AuthorizationHostCollection.Name)]
public sealed class Wave9RuntimeAuthorizationIntegrationTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("denied", HttpStatusCode.Forbidden)]
    [InlineData("invalid-normalization", HttpStatusCode.Unauthorized)]
    [InlineData("exact", HttpStatusCode.OK)]
    [InlineData("implied", HttpStatusCode.OK)]
    [InlineData("wildcard", HttpStatusCode.OK)]
    [InlineData("tenant-match", HttpStatusCode.OK)]
    [InlineData("tenant-mismatch", HttpStatusCode.Forbidden)]
    [InlineData("external", HttpStatusCode.OK)]
    [InlineData("external-resource-denied", HttpStatusCode.Forbidden)]
    [InlineData("external-no-tenant", HttpStatusCode.OK)]
    [InlineData("external-no-tenant-resource-denied", HttpStatusCode.Forbidden)]
    [InlineData("resource-allow-no-permission", HttpStatusCode.OK)]
    [InlineData("implied-resource-denied", HttpStatusCode.Forbidden)]
    [InlineData("wildcard-resource-denied", HttpStatusCode.Forbidden)]
    public async Task Minimal_and_retained_FastEndpoints_routes_share_the_Foundation_Identity_permission_evaluator(
        string? identity,
        HttpStatusCode expected)
    {
        await using var host = await RuntimeAuthorizationHost.StartAsync();
        using var minimal = await SendAsync(host.Client, "/runtime/workflows/instances", identity);
        using var fast = await SendAsync(host.Client, "/wave9/runtime-fast", identity);

        Assert.Equal(expected, minimal.StatusCode);
        Assert.Equal(expected, fast.StatusCode);
    }

    [Fact]
    public async Task Runtime_routes_publish_their_catalog_owned_execute_manage_and_publishing_read_actions()
    {
        await using var host = await RuntimeAuthorizationHost.StartAsync();
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime/workflows/executables/{artifactId}/execute"] = WorkflowRuntimePermissions.WorkflowRuntimeExecute,
            ["runtime/workflows/dispatches/{dispatchId}/redrive"] = WorkflowRuntimePermissions.WorkflowRuntimeManage,
            ["runtime/workflows/executables/{artifactId}/source-references/{sourceReferenceId}/input-sources"] = WorkflowRuntimePermissions.WorkflowPublishingRead
        };

        foreach (var (route, permission) in expected)
        {
            var endpoint = Assert.Single(host.Endpoints.OfType<RouteEndpoint>(), endpoint =>
                string.Equals(endpoint.RoutePattern.RawText, route, StringComparison.Ordinal));
            Assert.Equal(new PermissionPolicyCodec().Format(PermissionPolicyDescriptor.Single(permission)), endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>()?.Value);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string? identity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (identity is not null)
        {
            request.Headers.TryAddWithoutValidation(RuntimeAuthentication.IdentityHeader, identity);
            request.Headers.TryAddWithoutValidation(TenantResourceHandler.TenantHeader, "tenant-a");
            if (identity.Contains("resource-denied", StringComparison.Ordinal))
                request.Headers.TryAddWithoutValidation(TenantResourceHandler.ResourceHeader, "deny");
            else if (identity == "resource-allow-no-permission")
                request.Headers.TryAddWithoutValidation(TenantResourceHandler.ResourceHeader, "allow");
        }
        return await client.SendAsync(request);
    }

    private sealed class RuntimeAuthorizationHost(IHost host) : IAsyncDisposable
    {
        public HttpClient Client { get; } = host.GetTestClient();
        public IReadOnlyList<Endpoint> Endpoints => host.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        public static async Task<RuntimeAuthorizationHost> StartAsync()
        {
            var host = new HostBuilder().ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.ApplicationKey, "Elsa.Workflows.Runtime.Api");
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddAuthentication(RuntimeAuthentication.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, RuntimeAuthentication>(RuntimeAuthentication.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>([RuntimeAuthentication.SchemeName], StringComparer.Ordinal));
                    services.AddPermissionContributor<Wave9AuthorizationContributor>();
                    services.AddScoped<IPermissionResourceHandler, TenantResourceHandler>();
                    services.AddSingleton<Elsa.Workflows.Runtime.Api.Handlers.IWorkflowInstanceListService>(new EmptyInstanceListService());
                    new WorkflowsRuntimeApiFeature().ConfigureServices(services);
                    services.AddFastEndpoints(options =>
                    {
                        options.Assemblies = [typeof(Wave9RuntimeAuthorizationIntegrationTests).Assembly];
                        options.Filter = type => type == typeof(Wave9FastEndpoint);
                    });
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(endpoints);
                        endpoints.MapFastEndpoints();
                    });
                });
            }).Build();

            await host.StartAsync();
            return new(host);
        }

        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            host.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyInstanceListService : Elsa.Workflows.Runtime.Api.Handlers.IWorkflowInstanceListService
    {
        public Task<WorkflowInstanceListView> ListAsync(ListWorkflowInstances request, CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowInstanceListView([], null, false, 0, 0));
    }

    private sealed class Wave9AuthorizationContributor : IPermissionContributor
    {
        public string OwnerId => "Elsa.Architecture.Tests.Wave9";

        public IEnumerable<Permission> Contribute() =>
        [
            new(
                "wave9.runtime.admin",
                "Wave 9 runtime authorization test admin",
                "Wave 9",
                "Test-only implied permission.",
                new HashSet<string>(StringComparer.Ordinal) { WorkflowRuntimePermissions.WorkflowRuntimeRead })
        ];
    }

    private sealed class TenantResourceHandler(IHttpContextAccessor httpContextAccessor) : IPermissionResourceHandler
    {
        public const string TenantHeader = "X-Wave9-Runtime-Tenant";

        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            var resourceDisposition = httpContextAccessor.HttpContext?.Request.Headers[ResourceHeader].ToString();
            if (string.Equals(resourceDisposition, "deny", StringComparison.Ordinal))
                return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Denied("Wave 9 resource mismatch."));

            if (context.Principal.FindFirst(IdentityClaimTypes.TenantId) is null)
                return ValueTask.FromResult<PermissionEvaluationResult?>(null);

            var requestedTenant = httpContextAccessor.HttpContext?.Request.Headers[TenantHeader].ToString();
            return ValueTask.FromResult<PermissionEvaluationResult?>(
                string.Equals(context.TenantId, requestedTenant, StringComparison.Ordinal)
                    ? PermissionEvaluationResult.Success
                    : PermissionEvaluationResult.Denied("Wave 9 tenant mismatch."));
        }

        public const string ResourceHeader = "X-Wave9-Runtime-Resource";
    }

    private sealed class Wave9FastEndpoint : ElsaEndpointWithoutRequest<string>
    {
        public override void Configure()
        {
            Get("wave9/runtime-fast");
            ConfigurePermissions(WorkflowRuntimePermissions.WorkflowRuntimeRead);
        }

        public override Task HandleAsync(CancellationToken cancellationToken) =>
            Send.OkAsync("fast", cancellationToken);
    }

    private sealed class RuntimeAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IClaimsNormalizer claimsNormalizer)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Wave9RuntimeAuthorization";
        public const string IdentityHeader = "X-Wave9-Runtime-Identity";

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers[IdentityHeader].ToString();
            if (string.IsNullOrWhiteSpace(identity))
                return AuthenticateResult.NoResult();

            if (identity is "external" or "external-resource-denied")
            {
                var externalPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "wave9-external-user")], Scheme.Name));
                var normalized = await claimsNormalizer.NormalizeAsync(new ClaimsNormalizationContext(
                    externalPrincipal,
                    "tenant-a",
                    "external-provider",
                    [new ClaimMappingRule(
                        "wave9-external-read",
                        "tenant-a",
                        "external-provider",
                        ClaimTypes.Name,
                        "wave9-external-user",
                        new HashSet<string>(StringComparer.Ordinal),
                        new HashSet<string>(StringComparer.Ordinal) { WorkflowRuntimePermissions.WorkflowRuntimeRead },
                        0,
                        StopOnMatch: true)],
                    Scheme.Name));
                return AuthenticateResult.Success(new AuthenticationTicket(normalized.Principal, Scheme.Name));
            }

            var noTenant = identity.StartsWith("external-no-tenant", StringComparison.Ordinal);
            var noPermission = identity == "resource-allow-no-permission";

            var permissions = identity switch
            {
                "exact" => new[] { WorkflowRuntimePermissions.WorkflowRuntimeRead },
                "implied" => new[] { "wave9.runtime.admin" },
                "wildcard" => new[] { PermissionKey.Wildcard },
                "tenant-match" or "tenant-mismatch" or "external-no-tenant" or "external-no-tenant-resource-denied" => new[] { WorkflowRuntimePermissions.WorkflowRuntimeRead },
                "implied-resource-denied" => new[] { "wave9.runtime.admin" },
                "wildcard-resource-denied" => new[] { PermissionKey.Wildcard },
                "resource-allow-no-permission" => Array.Empty<string>(),
                _ => new[] { "runtime.other" }
            };
            var claims = permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission)).ToList();
            claims.Add(new Claim(IdentityClaimTypes.Normalized, identity == "invalid-normalization" ? "invalid" : "v1"));
            if (!noTenant && (identity is "tenant-match" or "tenant-mismatch" or "implied-resource-denied" or "wildcard-resource-denied" or "resource-allow-no-permission"))
                claims.Add(new Claim(IdentityClaimTypes.TenantId, identity == "tenant-mismatch" ? "tenant-b" : "tenant-a"));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Wave9AuthorizationHostCollection
{
    public const string Name = "wave9-runtime-authorization-host";
}
