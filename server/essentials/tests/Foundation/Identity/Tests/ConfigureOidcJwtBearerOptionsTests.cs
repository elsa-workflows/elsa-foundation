using Elsa.Foundation.Identity.Oidc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests;

public sealed class ConfigureOidcJwtBearerOptionsTests
{
    private const string Scheme = "test-jwt";

    [Fact]
    public void Configure_for_the_registered_scheme_uses_the_explicit_audience()
    {
        var settings = new OidcAuthenticationOptions
        {
            JwtBearerScheme = Scheme,
            Authority = "https://issuer.example/",
            ClientId = "legacy-client-audience",
            Audience = "worker-api",
            RequireHttpsMetadata = false
        };
        var target = new JwtBearerOptions();

        new ConfigureOidcJwtBearerOptions(Options.Create(settings)).Configure(Scheme, target);

        Assert.Equal(settings.Authority, target.Authority);
        Assert.Equal("worker-api", target.Audience);
        Assert.False(target.RequireHttpsMetadata);
    }

    [Fact]
    public void Configure_for_the_registered_scheme_uses_client_id_only_when_audience_is_absent()
    {
        var target = new JwtBearerOptions();

        new ConfigureOidcJwtBearerOptions(Options.Create(new OidcAuthenticationOptions
        {
            JwtBearerScheme = Scheme,
            ClientId = "legacy-client-audience"
        })).Configure(Scheme, target);

        Assert.Equal("legacy-client-audience", target.Audience);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Configure_preserves_an_explicit_blank_audience_instead_of_falling_back(string audience)
    {
        var target = new JwtBearerOptions();

        new ConfigureOidcJwtBearerOptions(Options.Create(new OidcAuthenticationOptions
        {
            JwtBearerScheme = Scheme,
            ClientId = "legacy-client-audience",
            Audience = audience
        })).Configure(Scheme, target);

        Assert.Equal(audience, target.Audience);
    }

    [Theory]
    [InlineData("another-scheme")]
    [InlineData(null)]
    public void Configure_for_another_or_default_name_leaves_options_untouched(string? name)
    {
        var target = new JwtBearerOptions
        {
            Authority = "https://existing.example/",
            Audience = "existing-audience",
            RequireHttpsMetadata = false
        };

        new ConfigureOidcJwtBearerOptions(Options.Create(new OidcAuthenticationOptions
        {
            JwtBearerScheme = Scheme,
            Authority = "https://issuer.example/",
            Audience = "worker-api",
            RequireHttpsMetadata = true
        })).Configure(name, target);

        Assert.Equal("https://existing.example/", target.Authority);
        Assert.Equal("existing-audience", target.Audience);
        Assert.False(target.RequireHttpsMetadata);
    }

    [Fact]
    public void Configure_without_a_name_uses_the_default_name_and_leaves_a_named_scheme_untouched()
    {
        var target = new JwtBearerOptions { Audience = "existing-audience" };
        var configure = new ConfigureOidcJwtBearerOptions(Options.Create(new OidcAuthenticationOptions
        {
            JwtBearerScheme = Scheme,
            Audience = "worker-api"
        }));

        configure.Configure(target);

        Assert.Equal("existing-audience", target.Audience);
    }
}
