using Elsa.Foundation.Identity.Oidc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests;

/// <summary>
/// Composes <c>FoundationIdentityOidc</c> from shell configuration, the way a <c>shells.json</c> does (#1581). A setting
/// the feature does not declare is silently ignored, so before these settings existed an Authority or ClientId in
/// <c>shells.json</c> left the provider unconfigured: every external bearer token was refused as having an invalid
/// issuer, and the interactive OpenID Connect handler was never registered.
/// </summary>
public sealed class OidcAuthenticationFeatureTests : IAsyncDisposable
{
    private const string Authority = "https://idp.example.com/realms/elsa";
    private const string ClientId = "elsa-server";
    private const string ClientSecret = "client-secret";

    private static readonly OidcAuthenticationOptions Defaults = new();

    private static readonly Dictionary<string, string?> FullSettings = new()
    {
        ["IsDefault"] = "false",
        ["Authority"] = Authority,
        ["ClientId"] = ClientId,
        ["ClientSecret"] = ClientSecret,
        ["RequireHttpsMetadata"] = "false"
    };

    private WebApplication? _host;

    [Fact]
    public async Task Shell_settings_configure_the_provider_options()
    {
        var services = await ActivateAsync(FullSettings);

        var options = services.GetRequiredService<IOptions<OidcAuthenticationOptions>>().Value;

        Assert.Equal(Authority, options.Authority);
        Assert.Equal(ClientId, options.ClientId);
        Assert.Equal(ClientSecret, options.ClientSecret);
        Assert.False(options.RequireHttpsMetadata);
        Assert.False(options.IsDefault);
    }

    [Fact]
    public async Task A_client_id_setting_registers_and_configures_the_interactive_openid_connect_handler()
    {
        var services = await ActivateAsync(FullSettings);

        var schemes = await services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        var openIdConnect = services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(Defaults.AuthenticationScheme);

        Assert.Contains(schemes, scheme => scheme.Name == Defaults.AuthenticationScheme);
        Assert.Equal(Authority, openIdConnect.Authority);
        Assert.Equal(ClientId, openIdConnect.ClientId);
        Assert.Equal(ClientSecret, openIdConnect.ClientSecret);
        Assert.False(openIdConnect.RequireHttpsMetadata);
    }

    [Fact]
    public async Task Authority_and_client_id_settings_configure_bearer_validation()
    {
        var services = await ActivateAsync(FullSettings);

        var jwtBearer = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(Defaults.JwtBearerScheme);

        Assert.Equal(Authority, jwtBearer.Authority);
        Assert.Equal(ClientId, jwtBearer.Audience);
        Assert.False(jwtBearer.RequireHttpsMetadata);
    }

    [Fact]
    public async Task An_authority_without_a_client_id_validates_bearer_tokens_but_registers_no_interactive_handler()
    {
        var services = await ActivateAsync(new Dictionary<string, string?> { ["Authority"] = Authority });

        var schemes = await services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        var jwtBearer = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(Defaults.JwtBearerScheme);

        Assert.DoesNotContain(schemes, scheme => scheme.Name == Defaults.AuthenticationScheme);
        Assert.Equal(Authority, jwtBearer.Authority);
        Assert.True(jwtBearer.RequireHttpsMetadata);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
    }

    private async Task<IServiceProvider> ActivateAsync(IReadOnlyDictionary<string, string?> settings)
    {
        _host = await ShellActivationHost.StartFromConfigurationAsync<OidcAuthenticationFeature>(
            Environments.Development,
            "FoundationIdentityOidc",
            settings);
        var shell = await _host.ActivateShellAsync();
        return shell.ServiceProvider;
    }
}
