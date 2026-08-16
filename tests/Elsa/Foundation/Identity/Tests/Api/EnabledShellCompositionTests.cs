using CShells.AspNetCore.Features;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.Api.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Extensions;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Elsa.Foundation.Identity.Tests.Api;

/// <summary>
/// Plan Workstream D "prove it": composes the enabled-by-default identity stack end to end (ASP.NET Core
/// Identity + cookie sign-in + login, OpenIddict issuance + bearer validation, the identity API surface with
/// its FastEndpoints claim-type bridge) in front of a real <see cref="ConfigurePermissions"/>-secured Elsa
/// endpoint — the same guard the D5 sweep applied across Design/Publishing/Activities. It asserts:
/// (a) an anonymous request to the permission-secured endpoint is rejected with 401; and (b) the full
/// login → token → bearer flow yields a token whose <c>elsa.identity.permission</c> claims satisfy
/// <c>ConfigurePermissions()</c> (proving the claim-type bridge registered by the identity API feature).
/// The Development-only behaviour of the ApiSecurity.AllowAnonymous kill-switch (c) is proven directly by
/// <c>ApiSecurityConfiguratorTests</c> and <c>PerShellApiSecurityIntegrationTests</c>.
/// </summary>
[Collection(FastEndpointsHostCollection.Name)]
public sealed class EnabledShellCompositionTests : IAsyncLifetime
{
    private const string TokenRoute = "/_elsa/identity/token";
    private const string SecuredRoute = "/" + SecuredPingEndpoint.Route;

    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        var databaseSuffix = Guid.NewGuid().ToString("n");

        _host = new HostBuilder()
            // The dev/demo identity features register a DevelopmentOrDemoGuard that hard-fails startup outside
            // Development (HostBuilder defaults to Production). This composition runs legitimately in dev/demo
            // mode, so declare the Development environment.
            .UseEnvironment(Environments.Development)
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddAuthorization();

                    services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
                        isDevelopmentOrDemo: true,
                        configureDbContext: builder => builder.UseInMemoryDatabase($"identity-{databaseSuffix}"),
                        initialAdmin: TestAdmin.SeedOptions());

                    services.AddFoundationIdentityOpenIddict(
                        options => options.IsDevelopmentOrDemo = true,
                        configureDbContext: builder => builder.UseInMemoryDatabase($"openiddict-{databaseSuffix}"));

                    // Registers the identity API services. The protocol endpoints are mapped through the
                    // explicit IWebShellFeature seam below; the unrelated canary remains FastEndpoints.
                    services.AddFoundationIdentityApi();

                    services.AddFastEndpoints(o => o.Assemblies =
                    [
                        typeof(SecuredPingEndpoint).Assembly             // the ConfigurePermissions() endpoint
                    ]);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapFastEndpoints(config =>
                            config.Security.PermissionsClaimType = IdentityClaimTypes.Permission);
                        ((IWebShellFeature)new FoundationIdentityApiFeature()).MapEndpoints(endpoints, null);
                        ((IWebShellFeature)new AspNetCoreIdentityFeature()).MapEndpoints(endpoints, null);
                    });
                });
            })
            .Build();

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database.EnsureCreatedAsync();
        }

        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Anonymous_Request_To_A_Permission_Secured_Endpoint_Is_Rejected_With_401()
    {
        var response = await _host.GetTestClient().GetAsync(SecuredRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_Then_Token_Yields_A_Bearer_That_Satisfies_ConfigurePermissions()
    {
        var client = _host.GetTestClient();
        await LoginTestHelper.LoginAsync(client);

        var accessToken = await FetchAccessTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, SecuredRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pong", (await response.Content.ReadAsStringAsync()).Trim('"'));
    }

    private static async Task<string> FetchAccessTokenAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<AccessTokenPayload>(TokenRoute);
        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response!.AccessToken));
        return response.AccessToken;
    }

    private sealed record AccessTokenPayload(string AccessToken, DateTimeOffset ExpiresAt);
}

/// <summary>
/// A permission-secured Elsa endpoint standing in for a D5-swept endpoint: it applies <c>Permissions("*")</c>
/// via <see cref="ElsaEndpointWithoutRequest{TResponse}.ConfigurePermissions"/>, so whether a caller is
/// admitted depends solely on carrying an accepted permission claim — exactly what the enabled-shell flow
/// must satisfy.
/// </summary>
public sealed class SecuredPingEndpoint : ElsaEndpointWithoutRequest<string>
{
    public const string Route = "test/secured-ping";

    public override void Configure()
    {
        Get(Route);
        ConfigurePermissions();
    }

    public override Task HandleAsync(CancellationToken ct) => Send.OkAsync("pong", ct);
}
