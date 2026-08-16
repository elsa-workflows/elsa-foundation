using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Authorization;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Architecture.Tests;

[Collection(Wave1FastEndpointsCollection.Name)]
public sealed class Wave1AuthorizationIntegrationTests : IAsyncLifetime
{
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
                    services.AddAuthentication(Wave1AuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, Wave1AuthenticationHandler>(Wave1AuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal)
                        {
                            Wave1AuthenticationHandler.SchemeName
                        });
                    new ApiCapabilitiesFeature().ConfigureServices(services);
                    services.AddPermissionContributor<Wave1AuthorizationContributor>();
                    services.AddFastEndpoints(options =>
                    {
                        options.Assemblies = [typeof(Wave1AuthorizationIntegrationTests).Assembly];
                        options.Filter = type => type == typeof(ConcurrentFastEndpoint);
                    });
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        ApiCapabilitiesApi.MapApiCapabilitiesApi(endpoints);
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

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("denied", HttpStatusCode.Forbidden)]
    [InlineData("exact", HttpStatusCode.OK)]
    [InlineData("implied", HttpStatusCode.OK)]
    [InlineData("wildcard", HttpStatusCode.OK)]
    public async Task Migrated_minimal_and_concurrent_fastendpoints_routes_share_the_same_permission_outcomes(
        string? identity,
        HttpStatusCode expected)
    {
        using var minimal = await SendAsync("/capabilities", identity);
        using var fast = await SendAsync("/wave1/fast", identity);

        Assert.Equal(expected, minimal.StatusCode);
        Assert.Equal(expected, fast.StatusCode);
    }

    private Task<HttpResponseMessage> SendAsync(string path, string? identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(Wave1AuthenticationHandler.IdentityHeader, identity);
        return _client.SendAsync(request);
    }

    private sealed class Wave1AuthorizationContributor : IPermissionContributor
    {
        public string OwnerId => "Elsa.Architecture.Tests";

        public IEnumerable<Permission> Contribute() =>
        [
            new(
                "wave1.admin",
                "Wave 1 authorization test admin",
                "Tests",
                "Only used to prove implication through the shared evaluator.",
                new HashSet<string>(StringComparer.Ordinal) { ApiCapabilitiesPermissions.Read })
        ];
    }

    private sealed class ConcurrentFastEndpoint : ElsaEndpointWithoutRequest<string>
    {
        public override void Configure()
        {
            Get("wave1/fast");
            ConfigurePermissions(ApiCapabilitiesPermissions.Read);
        }

        public override Task HandleAsync(CancellationToken cancellationToken) =>
            Send.OkAsync("fast", cancellationToken);
    }

    private sealed class Wave1AuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Wave1Test";
        public const string IdentityHeader = "X-Wave1-Identity";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers[IdentityHeader].ToString();
            if (string.IsNullOrWhiteSpace(identity))
                return Task.FromResult(AuthenticateResult.NoResult());

            var permissions = identity switch
            {
                "exact" => [ApiCapabilitiesPermissions.Read],
                "implied" => ["wave1.admin"],
                "wildcard" => [PermissionKey.Wildcard],
                _ => Array.Empty<string>()
            };
            var claims = permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission)).ToList();
            claims.Add(new Claim(IdentityClaimTypes.Normalized, "v1"));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Wave1FastEndpointsCollection
{
    public const string Name = "wave1-fastendpoints-host";
}
