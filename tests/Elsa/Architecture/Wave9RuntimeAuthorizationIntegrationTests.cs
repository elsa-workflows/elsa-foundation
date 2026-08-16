using Elsa.Api.FastEndpoints.Abstractions;
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

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string? identity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (identity is not null)
        {
            request.Headers.TryAddWithoutValidation(RuntimeAuthentication.IdentityHeader, identity);
            request.Headers.TryAddWithoutValidation(TenantResourceHandler.TenantHeader, "tenant-a");
        }
        return await client.SendAsync(request);
    }

    private sealed class RuntimeAuthorizationHost(IHost host) : IAsyncDisposable
    {
        public HttpClient Client { get; } = host.GetTestClient();

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
                    services.AddSingleton<IRequestSender, RuntimeRequestSender>();
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

    private sealed class RuntimeRequestSender : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            Task.FromResult((T)(object)(request switch
            {
                ListWorkflowInstances => new WorkflowInstanceListView([], null, false, 0, 0),
                _ => throw new InvalidOperationException($"Unexpected Runtime authorization request '{request.GetType().FullName}'.")
            }));
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
            if (context.Principal.FindFirst(IdentityClaimTypes.TenantId) is null)
                return ValueTask.FromResult<PermissionEvaluationResult?>(null);

            var requestedTenant = httpContextAccessor.HttpContext?.Request.Headers[TenantHeader].ToString();
            return ValueTask.FromResult<PermissionEvaluationResult?>(
                string.Equals(context.TenantId, requestedTenant, StringComparison.Ordinal)
                    ? PermissionEvaluationResult.Success
                    : PermissionEvaluationResult.Denied("Wave 9 tenant mismatch."));
        }
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
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Wave9RuntimeAuthorization";
        public const string IdentityHeader = "X-Wave9-Runtime-Identity";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers[IdentityHeader].ToString();
            if (string.IsNullOrWhiteSpace(identity))
                return Task.FromResult(AuthenticateResult.NoResult());

            var permissions = identity switch
            {
                "exact" => new[] { WorkflowRuntimePermissions.WorkflowRuntimeRead },
                "implied" => new[] { "wave9.runtime.admin" },
                "wildcard" => new[] { PermissionKey.Wildcard },
                "tenant-match" or "tenant-mismatch" => new[] { WorkflowRuntimePermissions.WorkflowRuntimeRead },
                _ => new[] { "runtime.other" }
            };
            var claims = permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission)).ToList();
            claims.Add(new Claim(IdentityClaimTypes.Normalized, identity == "invalid-normalization" ? "invalid" : "v1"));
            if (identity is "tenant-match" or "tenant-mismatch")
                claims.Add(new Claim(IdentityClaimTypes.TenantId, identity == "tenant-mismatch" ? "tenant-b" : "tenant-a"));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Wave9AuthorizationHostCollection
{
    public const string Name = "wave9-runtime-authorization-host";
}
