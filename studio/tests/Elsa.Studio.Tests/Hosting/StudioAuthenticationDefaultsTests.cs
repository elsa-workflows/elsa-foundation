using System.Net;
using System.Security.Claims;
using Elsa.Studio.Api.Authorization;
using Elsa.Studio.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Tests.Hosting;

// One case changes ASP.NET Core's process-wide compatibility switch; it must run apart from other tests.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AuthenticationDefaultsCollection
{
    public const string Name = "Studio authentication defaults";
}

[Collection(AuthenticationDefaultsCollection.Name)]
public sealed class StudioAuthenticationDefaultsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_single_application_cookie_remains_the_implicit_default_before_or_after_Studio(bool applicationFirst)
    {
        await using var app = await StartAsync(services => AddCookie(services, "app"), applicationFirst);
        using var client = app.GetTestClient();
        using var anonymous = await client.GetAsync("/app/private");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("app", anonymous.Headers.GetValues("X-Application-Challenge").Single());
        using var login = await client.GetAsync("/login");
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Cookie", login.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        using var authenticated = await client.GetAsync("/app/private");
        authenticated.EnsureSuccessStatusCode();
        Assert.Equal("application-user", await authenticated.Content.ReadAsStringAsync());
        // The application's cookie cannot replace the independently authenticated backend bearer.
        using var studio = await client.GetAsync("/studio/private");
        Assert.Equal(HttpStatusCode.Unauthorized, studio.StatusCode);
        Assert.False(studio.Headers.Contains("X-Application-Challenge"));
    }

    [Fact]
    public async Task Explicit_application_defaults_remain_unchanged()
    {
        await using var app = await StartAsync(services =>
        {
            AddCookie(services, "app");
            AddCookie(services, "other");
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = "other";
                options.DefaultAuthenticateScheme = "app";
                options.DefaultChallengeScheme = "other";
                options.DefaultForbidScheme = "app";
                options.DefaultSignInScheme = "app";
                options.DefaultSignOutScheme = "other";
            });
        });
        var schemes = app.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.Equal("app", (await schemes.GetDefaultAuthenticateSchemeAsync())!.Name);
        Assert.Equal("other", (await schemes.GetDefaultChallengeSchemeAsync())!.Name);
        Assert.Equal("app", (await schemes.GetDefaultForbidSchemeAsync())!.Name);
        Assert.Equal("app", (await schemes.GetDefaultSignInSchemeAsync())!.Name);
        Assert.Equal("other", (await schemes.GetDefaultSignOutSchemeAsync())!.Name);
    }

    [Fact]
    public async Task Multiple_application_schemes_still_require_an_explicit_default()
    {
        await using var app = await StartAsync(services =>
        {
            AddCookie(services, "app");
            AddCookie(services, "other");
        });
        var schemes = app.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.Null(await schemes.GetDefaultAuthenticateSchemeAsync());
        Assert.Null(await schemes.GetDefaultChallengeSchemeAsync());
    }

    [Fact]
    public async Task Suppressing_automatic_defaults_remains_effective()
    {
        const string switchName = "Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme";
        AppContext.TryGetSwitch(switchName, out var previous);
        AppContext.SetSwitch(switchName, true);
        try
        {
            await using var app = await StartAsync(services => AddCookie(services, "app"));
            var schemes = app.Services.GetRequiredService<IAuthenticationSchemeProvider>();
            Assert.Null(await schemes.GetDefaultAuthenticateSchemeAsync());
            Assert.Null(await schemes.GetDefaultChallengeSchemeAsync());
        }
        finally
        {
            AppContext.SetSwitch(switchName, previous);
        }
    }

    private static void AddCookie(IServiceCollection services, string name) => services.AddAuthentication().AddCookie(name, options =>
    {
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers["X-Application-Challenge"] = name;
                return Task.CompletedTask;
            }
        };
    });

    private static async Task<WebApplication> StartAsync(Action<IServiceCollection> configureAuthentication, bool applicationFirst = true)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CShells:Shells:Default:Features:StudioApi"] = "true",
            ["Studio:Auth:Enabled"] = "true"
        });
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        if (applicationFirst) configureAuthentication(builder.Services);
        builder.AddElsaStudio(options => options.ShellsFile = null);
        if (!applicationFirst) configureAuthentication(builder.Services);
        var app = builder.Build();
        app.UseElsaStudio();
        app.MapElsaStudio();
        app.MapGet("/login", async (HttpContext context) => await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "application-user")], "app"))));
        app.MapGet("/app/private", (HttpContext context) => context.User.Identity!.Name!).RequireAuthorization();
        app.MapGet("/studio/private", () => Results.Ok()).RequireAuthorization(StudioBridgeAuthorization.PolicyName);
        await app.StartAsync();
        return app;
    }
}
