using System.Security.Cryptography;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Elsa.Foundation.Identity.Oidc;
using Elsa.Foundation.Identity.Oidc.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace Elsa.Foundation.Identity.Tests.OpenIddict;

/// <summary>
/// B2: scheme registration and composition — the validation handler and selector scheme are registered, the
/// selector becomes the default authenticate/challenge scheme deterministically regardless of feature
/// registration order relative to the OIDC module, and per-request scheme selection routes by request shape
/// (local bearer / external bearer / cookie / anonymous).
/// </summary>
public sealed class OpenIddictSchemeCompositionTests : IAsyncDisposable
{
    private readonly OpenIddictIdentityFixture _fixture = new();

    [Fact]
    public async Task Registers_The_Validation_Handler_And_Selector_Schemes()
    {
        var schemes = await _fixture.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();

        Assert.Contains(schemes, x => x.Name == OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        Assert.Contains(schemes, x => x.Name == OpenIddictIdentityDefaults.SelectorScheme);
    }

    [Fact]
    public void Feature_Registers_Cleanly_Without_Shells_Or_Server_Wiring()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new OpenIddictIdentityFeature { IsDevelopmentOrDemo = true }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ITokenService>());
        Assert.IsType<OpenIddictTokenService>(scope.ServiceProvider.GetRequiredService<ITokenService>());
    }

    [Fact]
    public void EF_oracle_composes_behavior_with_the_entity_framework_store()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationIdentityOpenIddict(
            options => options.IsDevelopmentOrDemo = true,
            configureDbContext: builder =>
                OpenIddictIdentityFixture.ConfigureInMemoryStore(builder, $"openiddict-{Guid.NewGuid():n}"));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>());
        Assert.IsType<OpenIddictTokenService>(scope.ServiceProvider.GetRequiredService<ITokenService>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Selector_Becomes_Default_Scheme_Regardless_Of_Oidc_Registration_Order(bool oidcFirst)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (oidcFirst)
        {
            services.AddFoundationIdentityOidc();
            AddOpenIddict(services);
        }
        else
        {
            AddOpenIddict(services);
            services.AddFoundationIdentityOidc();
        }

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal(OpenIddictIdentityDefaults.SelectorScheme, options.DefaultAuthenticateScheme);
        Assert.Equal(OpenIddictIdentityDefaults.SelectorScheme, options.DefaultChallengeScheme);

        static void AddOpenIddict(IServiceCollection services) =>
            services.AddFoundationIdentityOpenIddict(
                options => options.IsDevelopmentOrDemo = true,
                configureDbContext: builder => builder.UseInMemoryDatabase($"openiddict-{Guid.NewGuid():n}"));
    }

    [Fact]
    public async Task Selector_Routes_Local_Bearer_Tokens_To_The_Validation_Handler()
    {
        var issued = await _fixture.IssueAsync();
        var context = CreateContext(_fixture.Services, authorization: $"Bearer {issued.AccessToken}");

        Assert.Equal(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, OpenIddictSchemeSelector.SelectScheme(context));
    }

    [Fact]
    public void Selector_Routes_Opaque_Bearer_Tokens_To_The_Validation_Handler()
    {
        var context = CreateContext(_fixture.Services, authorization: "Bearer not-a-jwt");

        Assert.Equal(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, OpenIddictSchemeSelector.SelectScheme(context));
    }

    [Fact]
    public void Selector_Routes_Foreign_Bearer_Tokens_To_The_External_Scheme_When_Registered()
    {
        // A container where the external JwtBearer scheme (OIDC module default name) exists.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationIdentityOidc();
        services.AddFoundationIdentityOpenIddict(
            options => options.IsDevelopmentOrDemo = true,
            configureDbContext: builder => builder.UseInMemoryDatabase($"openiddict-{Guid.NewGuid():n}"));
        using var provider = services.BuildServiceProvider();

        var context = CreateContext(provider, authorization: $"Bearer {CreateForeignJwt()}");

        Assert.Equal(new OidcAuthenticationOptions().JwtBearerScheme, OpenIddictSchemeSelector.SelectScheme(context));
    }

    [Fact]
    public void Selector_Routes_Foreign_Bearer_Tokens_To_The_Validation_Handler_When_No_External_Scheme_Exists()
    {
        var context = CreateContext(_fixture.Services, authorization: $"Bearer {CreateForeignJwt()}");

        Assert.Equal(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, OpenIddictSchemeSelector.SelectScheme(context));
    }

    [Fact]
    public void Selector_Routes_Cookie_Requests_To_The_Interactive_Scheme_When_Registered()
    {
        // A container where the identity cookie scheme (ASP.NET Core Identity module name) exists.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication().AddCookie("Elsa.Identity.Cookie");
        services.AddFoundationIdentityOpenIddict(
            options => options.IsDevelopmentOrDemo = true,
            configureDbContext: builder => builder.UseInMemoryDatabase($"openiddict-{Guid.NewGuid():n}"));
        using var provider = services.BuildServiceProvider();

        var context = CreateContext(provider, cookie: "Elsa.Identity.Cookie=session-value");

        Assert.Equal("Elsa.Identity.Cookie", OpenIddictSchemeSelector.SelectScheme(context));
    }

    [Fact]
    public void Selector_Routes_Anonymous_Requests_To_The_Validation_Handler_For_A_Clean_401()
    {
        var context = CreateContext(_fixture.Services);

        Assert.Equal(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, OpenIddictSchemeSelector.SelectScheme(context));
    }

    private static DefaultHttpContext CreateContext(IServiceProvider services, string? authorization = null, string? cookie = null)
    {
        var context = new DefaultHttpContext { RequestServices = services };

        if (authorization is not null)
            context.Request.Headers.Authorization = authorization;

        if (cookie is not null)
            context.Request.Headers.Cookie = cookie;

        return context;
    }

    private static string CreateForeignJwt() =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://some-external-idp.example.com/",
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)), SecurityAlgorithms.HmacSha256)
        });

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
