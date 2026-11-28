using System.Net;
using System.Reflection;
using System.Security.Claims;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using CShells.Nuplane;
using Elsa.Foundation.Hosting;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.EntityFramework.Extensions;
using Elsa.Persistence.Schema;
using Elsa.Workbench;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class EmbeddedFoundationShellHostingTests
{
    [Fact]
    public async Task Workbench_registration_order_preserves_EF_preparation_and_guards_a_restart_only_shell()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["CShells:Shells:restart-guard:Name"] = "restart-guard";
        var efDiscovery = new ReplacedAssemblyProbe();
        builder.AddNuplaneHostIntegration();
        builder.Services.AddSingleton<ISupersededAssemblySource>(efDiscovery);
        builder.Services.AddEfPersistenceResources(builder.Configuration, typeof(WorkbenchEfToolingShellDefaults).Assembly);
        builder.AddFoundationShellHosting(shells => shells
            .WithAssemblyContaining<RestartRequiredProbeFeature>()
            .ConfigureAllShells(shell => shell.WithFeature<RestartRequiredProbeFeature>()));

        await using var app = builder.Build();
        Assert.Single(app.Services.GetServices<IShellSettingsPreparer>());
        var registry = app.Services.GetRequiredService<IShellRegistry>();
        var first = await registry.GetOrActivateAsync("restart-guard");
        try
        {
            Assert.Equal(1, efDiscovery.ReplacedReads);
            var replacement = await registry.ReloadAsync("restart-guard");
            Assert.Contains("requires a host restart", replacement.Error?.ToString(), StringComparison.Ordinal);
            Assert.Same(first, registry.GetActive("restart-guard"));
            Assert.Equal(1, efDiscovery.ReplacedReads);
        }
        finally
        {
            await registry.DrainAsync(first);
        }
    }

    [Fact]
    public async Task Existing_app_keeps_its_routes_and_authentication_when_opting_into_shell_startup()
    {
        var contentRoot = Directory.CreateTempSubdirectory("elsa-embedded-host-");
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = contentRoot.FullName });
            builder.WebHost.UseTestServer();
            builder.Configuration["CShells:Shells:embedded:Name"] = "embedded";
            builder.Configuration["Nuplane:Setup:StateFilePath"] = Path.Join(contentRoot.FullName, "state.json");
            builder.Configuration["Nuplane:Loading:Enabled"] = "true";
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddAuthentication("existing-app").AddCookie("existing-app", options =>
            {
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
            });
            builder.Services.AddAuthorization();

            builder.AddFoundationNuplane();
            builder.AddFoundationShellHosting();

            await using var app = builder.Build();
            app.MapGet("/existing", (ClaimsPrincipal user) => user.Identity!.Name).RequireAuthorization();
            app.MapPost("/test-sign-in", (HttpContext context) => context.SignInAsync("existing-app",
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "existing user")], "existing-app"))));
            await app.StartAsync();

            var registry = Assert.IsAssignableFrom<ISettledShellRegistry>(app.Services.GetRequiredService<IShellRegistry>());
            Assert.NotNull(registry.GetSettledActive("embedded"));
            var profile = app.Services.GetRequiredService<IOptionsMonitor<NuplaneIntegrationOptions>>().CurrentValue;
            Assert.True(profile.Enabled);
            Assert.True(profile.AutoReload);
            Assert.Equal(NuplaneRefreshTrigger.EveryEligibleCompletion, profile.RefreshTrigger);

            using var client = app.GetTestClient();
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/health/ready")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/_module-management/reconcile", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/existing")).StatusCode);
            using var signIn = await client.PostAsync("/test-sign-in", null);
            signIn.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Add("Cookie", Assert.Single(signIn.Headers.GetValues("Set-Cookie")).Split(';')[0]);
            Assert.Equal("existing user", await client.GetStringAsync("/existing"));
        }
        finally
        {
            contentRoot.Delete(recursive: true);
        }
    }

    [ShellFeature("test-restart-required-probe")]
    public sealed class RestartRequiredProbeFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) =>
            services.AddSingleton(new ShellRestartRequirement("test-restart-required-probe"));
    }

    private sealed class ReplacedAssemblyProbe : ISupersededAssemblySource
    {
        public int ReplacedReads { get; private set; }

        public ValueTask<IReadOnlySet<Assembly>> GetReplacedAsync(CancellationToken cancellationToken = default)
        {
            ReplacedReads++;
            return GetRetiredAsync(cancellationToken);
        }

        public ValueTask<IReadOnlySet<Assembly>> GetRetiredAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlySet<Assembly>>(new HashSet<Assembly>());
    }
}
