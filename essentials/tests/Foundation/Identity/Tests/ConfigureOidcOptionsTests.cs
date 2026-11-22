using Elsa.Foundation.Identity.Oidc;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests;

public sealed class ConfigureOidcOptionsTests
{
    private const string Scheme = "test-oidc";

    [Fact]
    public void Configure_for_the_registered_scheme_copies_interactive_settings()
    {
        var settings = new OidcAuthenticationOptions
        {
            AuthenticationScheme = Scheme,
            Authority = "https://issuer.example/",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RequireHttpsMetadata = false
        };
        var target = new OpenIdConnectOptions();

        new ConfigureOidcOptions(Options.Create(settings)).Configure(Scheme, target);

        Assert.Equal(settings.Authority, target.Authority);
        Assert.Equal(settings.ClientId, target.ClientId);
        Assert.Equal(settings.ClientSecret, target.ClientSecret);
        Assert.False(target.RequireHttpsMetadata);
        Assert.Equal("code", target.ResponseType);
        Assert.True(target.SaveTokens);
    }

    [Theory]
    [InlineData("another-scheme")]
    [InlineData(null)]
    public void Configure_for_another_or_default_name_leaves_options_untouched(string? name)
    {
        var target = new OpenIdConnectOptions
        {
            Authority = "https://existing.example/",
            ClientId = "existing-client",
            ClientSecret = "existing-secret",
            RequireHttpsMetadata = false,
            ResponseType = "id_token",
            SaveTokens = false
        };
        var before = Snapshot(target);

        new ConfigureOidcOptions(Options.Create(new OidcAuthenticationOptions
        {
            AuthenticationScheme = Scheme,
            Authority = "https://issuer.example/",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RequireHttpsMetadata = true
        })).Configure(name, target);

        Assert.Equal(before, Snapshot(target));
    }

    [Fact]
    public void Configure_without_a_name_uses_the_default_name_and_leaves_a_named_scheme_untouched()
    {
        var target = new OpenIdConnectOptions { ClientId = "existing-client" };
        var configure = new ConfigureOidcOptions(Options.Create(new OidcAuthenticationOptions
        {
            AuthenticationScheme = Scheme,
            ClientId = "client-id"
        }));

        configure.Configure(target);

        Assert.Equal("existing-client", target.ClientId);
        Assert.Null(target.Authority);
    }

    private static (string? Authority, string? ClientId, string? ClientSecret, bool RequireHttpsMetadata, string ResponseType, bool SaveTokens) Snapshot(OpenIdConnectOptions options) =>
        (options.Authority, options.ClientId, options.ClientSecret, options.RequireHttpsMetadata, options.ResponseType, options.SaveTokens);
}
