using Elsa.Activities.Bpmn.Interchange;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Modularity.Api;
using Elsa.Modularity.Api.Authorization;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Elsa.Workflows.ExecutionEvidence;
using Elsa3.Activities.Design.Import;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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

/// <summary>Proves the four Wave 2 Minimal API owners coexist with one unrelated FastEndpoints route.</summary>
[Collection(Wave1HostCollection.Name)]
public sealed class Wave2MixedHostCoexistenceTests : IAsyncLifetime
{
    private const string IdentityHeader = "X-Wave2-Mixed-Identity";
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal)
                        {
                            MixedHostAuthenticationHandler.SchemeName
                        });
                    services.AddAuthentication(MixedHostAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, MixedHostAuthenticationHandler>(
                            MixedHostAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization();

                    new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
                    new ModularityApiFeature().ConfigureServices(services);
                    services.AddSingleton<IFeatureManagementService, MixedFeatureManagementService>();
                    new WorkflowsExecutionEvidenceFeature().ConfigureServices(services);
                    new Elsa3ImportActivitiesFeature().ConfigureServices(services);
                    services.AddFastEndpoints(options =>
                    {
                        options.Assemblies = [typeof(Wave2MixedHostCoexistenceTests).Assembly];
                        options.AssemblyFilter = assembly => assembly == typeof(Wave2MixedHostCoexistenceTests).Assembly;
                        options.Filter = type => type == typeof(UnrelatedWave2FastEndpoint);
                    });
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        new ActivitiesBpmnInterchangeFeature().MapEndpoints(endpoints, null);
                        new ModularityApiFeature().MapEndpoints(endpoints, null);
                        new WorkflowsExecutionEvidenceFeature().MapEndpoints(endpoints, null);
                        new Elsa3ImportActivitiesFeature().MapEndpoints(endpoints, null);
                        endpoints.MapFastEndpoints();
                    });
                });
            })
            .Build();

        await _host.StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public void All_wave_two_minimal_routes_and_one_unrelated_fastendpoints_route_are_coexistent()
    {
        var routes = _host.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/'))
            .Where(path => path is not null)
            .Where(path => path != "_test_url_cache_")
            .ToArray();

        Assert.True(routes.Length == 14, string.Join(", ", routes));
        Assert.Equal(13, routes.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("wave2/mixed/fast", routes, StringComparer.Ordinal);
        Assert.Equal(13, routes.Count(path => path is not "wave2/mixed/fast"));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("denied", HttpStatusCode.Forbidden)]
    [InlineData("exact", HttpStatusCode.OK)]
    [InlineData("implied", HttpStatusCode.OK)]
    [InlineData("wildcard", HttpStatusCode.OK)]
    public async Task Minimal_and_FastEndpoints_routes_share_the_foundation_evaluator(
        string? identity,
        HttpStatusCode expected)
    {
        using var minimalRequest = Request("/modularity/features", identity);
        using var fastRequest = Request(UnrelatedWave2FastEndpoint.Route, identity);
        using var minimalResponse = await _client.SendAsync(minimalRequest);
        using var fastResponse = await _client.SendAsync(fastRequest);

        Assert.Equal(expected, minimalResponse.StatusCode);
        Assert.Equal(expected, fastResponse.StatusCode);
    }

    private static HttpRequestMessage Request(string path, string? identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(IdentityHeader, identity);
        return request;
    }

    private sealed class MixedHostAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "wave2-mixed";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers[IdentityHeader].ToString();
            if (string.IsNullOrWhiteSpace(identity))
                return Task.FromResult(AuthenticateResult.NoResult());

            var permissions = identity switch
            {
                "exact" => ["module-management.read"],
                "implied" => ["module-management.manage"],
                "wildcard" => [PermissionKey.Wildcard],
                _ => Array.Empty<string>()
            };
            var claims = new List<Claim>
            {
                new(IdentityClaimTypes.Normalized, "v1"),
                new(IdentityClaimTypes.Provider, "wave2-mixed")
            };
            claims.AddRange(permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission)));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private sealed class MixedFeatureManagementService : IFeatureManagementService
    {
        public Task<FeatureCatalogResponse> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new FeatureCatalogResponse("wave2-mixed", []));

        public Task<FeatureApplyResult> ApplyAsync(FeatureApplyRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FeatureApplyResult(new FeatureCatalogResponse(request.Revision, []), 0, 0));
    }
}

internal sealed class UnrelatedWave2FastEndpoint : ElsaEndpointWithoutRequest<string>
{
    public const string Route = "/wave2/mixed/fast";

    public override void Configure()
    {
        Get(Route);
        ConfigurePermissions("module-management.read");
    }

    public override Task HandleAsync(CancellationToken cancellationToken) =>
        Send.OkAsync("wave2-mixed-fastendpoints", cancellationToken);
}
