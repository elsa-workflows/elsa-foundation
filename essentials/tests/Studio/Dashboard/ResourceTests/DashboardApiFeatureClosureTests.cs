using CShells;
using CShells.Lifecycle;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Api.Capabilities;
using Elsa.Events;
using Elsa.Foundation.Identity;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Studio.Dashboard.Api;
using Elsa.Studio.Dashboard.Management;
using Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Studio.Dashboard.ResourceTests;

public sealed class DashboardApiFeatureClosureTests
{
    [Fact]
    public async Task Selecting_native_api_composes_its_own_identity_substrate()
    {
        var directory = Directory.CreateTempSubdirectory("dashboard-api-closure-");
        try
        {
            await using var app = BuildHost(directory.FullName, withStore: true);
            app.MapShells();
            await app.StartAsync();
            var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync("dashboard-closure");
            await using var scope = shell.BeginScope();
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<NormalizedPrincipalValidator>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPermissionAuthorizationService>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAuthorizationService>());
        }
        finally { directory.Delete(recursive: true); }
    }
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Shell_activation_refuses_missing_or_ambiguous_stores(int stores)
    {
        var directory = Directory.CreateTempSubdirectory("dashboard-api-closure-invalid-");
        try
        {
            await using var app = BuildHost(directory.FullName, withStore: false, services => AddDeferredStores(services, stores));
            app.MapShells(); await app.StartAsync();
            var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
                app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync("dashboard-closure"));
            Assert.Contains(nameof(Elsa.Studio.Dashboard.Core.Resources.IDashboardResourceStore), failure.ToString(), StringComparison.Ordinal);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task Shell_validation_does_not_construct_request_scoped_store_replacements()
    {
        var directory = Directory.CreateTempSubdirectory("dashboard-api-closure-deferred-");
        try
        {
            await using var app = BuildHost(directory.FullName, withStore: false, services => AddDeferredStores(services, 1));
            app.MapShells(); await app.StartAsync();
            Assert.NotNull(await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync("dashboard-closure"));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void AddDeferredStores(IServiceCollection services, int count)
    {
        for (var index = 0; index < count; index++)
            services.AddScoped<Elsa.Studio.Dashboard.Core.Resources.IDashboardResourceStore>(_ =>
                throw new InvalidOperationException("A request-scoped store must not be constructed during activation."));
    }

    private static WebApplication BuildHost(string directory, bool withStore, Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = directory });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        configure?.Invoke(builder.Services);
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(typeof(DashboardResourcesApiFeature).Assembly, typeof(DashboardManagementFeature).Assembly,
                typeof(ApiCapabilitiesFeature).Assembly, typeof(FoundationIdentityAbstractionsFeature).Assembly,
                typeof(EventsFeature).Assembly, typeof(DashboardResourcesEntityFrameworkCoreFeature).Assembly)
            .AddShell("dashboard-closure", shell => {
                shell.WithFeature<DashboardResourcesApiFeature>();
                if (withStore) shell.WithFeature<DashboardResourcesEntityFrameworkCoreFeature>(feature =>
                    feature.ConnectionString = "Data Source=" + Path.Combine(directory, "definitions.db"));
            }));
        return builder.Build();
    }

}
