using Elsa.Foundation.Identity.Oidc;
using Elsa.Foundation.Identity.Oidc.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests;

/// <summary>
/// Covers the W4 (MS-13) OIDC authentication registration behaviour: an Elsa server secured by
/// default must reject an unauthenticated call with 401, not fault with a 500. The interactive
/// OpenID Connect handler is a remote-authentication request handler that eagerly validates its
/// options (ClientId is required) for every request, so registering it while unconfigured turns
/// every request into a 500 before authorization runs. It is therefore only registered when a
/// ClientId is configured; the JWT bearer scheme is always registered and made the default
/// challenge scheme so unauthenticated calls are challenged (401).
/// </summary>
public sealed class OidcAuthenticationRegistrationTests
{
    [Fact]
    public async Task Registers_the_jwt_bearer_scheme_even_when_no_client_id_is_configured()
    {
        var provider = BuildProvider();

        var schemes = await provider.GetService<IAuthenticationSchemeProvider>()!.GetAllSchemesAsync();

        Assert.Contains(schemes, s => s.Name == new OidcAuthenticationOptions().JwtBearerScheme);
    }

    [Fact]
    public async Task Does_not_register_the_interactive_openid_connect_scheme_when_no_client_id_is_configured()
    {
        var provider = BuildProvider();

        var schemes = await provider.GetService<IAuthenticationSchemeProvider>()!.GetAllSchemesAsync();

        Assert.DoesNotContain(schemes, s => s.Name == new OidcAuthenticationOptions().AuthenticationScheme);
    }

    [Fact]
    public async Task Registers_the_interactive_openid_connect_scheme_when_a_client_id_is_configured()
    {
        var provider = BuildProvider(options =>
        {
            options.ClientId = "elsa-client";
            options.Authority = "https://localhost/";
            options.RequireHttpsMetadata = false;
        });

        var schemes = await provider.GetService<IAuthenticationSchemeProvider>()!.GetAllSchemesAsync();

        Assert.Contains(schemes, s => s.Name == new OidcAuthenticationOptions().AuthenticationScheme);
    }

    [Fact]
    public void Makes_the_jwt_bearer_scheme_the_default_challenge_scheme_when_oidc_is_default()
    {
        var provider = BuildProvider();

        var authenticationOptions = provider.GetService<IOptions<AuthenticationOptions>>()!.Value;

        var jwtScheme = new OidcAuthenticationOptions().JwtBearerScheme;
        Assert.Equal(jwtScheme, authenticationOptions.DefaultChallengeScheme);
        Assert.Equal(jwtScheme, authenticationOptions.DefaultAuthenticateScheme);
    }

    private static ServiceProvider BuildProvider(Action<OidcAuthenticationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationIdentityOidc(configure);
        return services.BuildServiceProvider();
    }
}
