using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Management.Resources;
using Elsa.Studio.Dashboard.Management;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Dashboard.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("API")]
[ShellFeature(name: "StudioDashboardApi", DisplayName = "Studio Dashboard API", Description = "Provides authorized native saved questions, dashboards and module queries.", DependsOn = ["StudioDashboardManagement", typeof(ApiCapabilitiesFeature)])]
public class DashboardResourcesApiFeature : DashboardManagementFeature, IWebShellFeature
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddElsaEndpoints();
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.studio-dashboard", 1,
            [new("datasets", "_elsa/studio/dashboard-datasets"), new("query-preview", "_elsa/studio/dashboard-queries/preview"),
             new("query-validate", "_elsa/studio/dashboard-queries/validate"), new("query-drill", "_elsa/studio/dashboard-queries/drill"),
             new("questions", "_elsa/studio/questions"), new("dashboards", "_elsa/studio/dashboards"),
             new("audience-candidates", "_elsa/studio/dashboard-audience-candidates")], "StudioDashboardApi"));
        services.AddHttpContextAccessor();
        if (services.Count(descriptor => descriptor.ServiceType == typeof(IDashboardActorContext)) > 1)
            throw new InvalidOperationException("Exactly one Dashboard actor context must be registered.");
        services.TryAddScoped<IDashboardActorContext, HttpDashboardActorContext>();
        services.AddDashboardResources();
        services.TryAddSingleton<DashboardAudienceCursors>();
        services.TryAddScoped<IDashboardAudienceDirectory, DashboardAudienceDirectory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DashboardResourceCompositionGuard>());
        services.AddPermissionContributor<DashboardResourcePermissionContributor>();
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        DashboardResourcesApi.MapDashboardResourcesApi(endpoints);
        DashboardQueriesApi.Map(endpoints);
    }
}

internal sealed class DashboardResourceCompositionGuard(IServiceProvider provider) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = provider.CreateScope();
        RequireOne(scope.ServiceProvider.GetServices<IDashboardActorContext>());
        RequireOne(scope.ServiceProvider.GetServices<IDashboardQueryService>());
        RequireOne(scope.ServiceProvider.GetServices<IDashboardResourceService>());
        RequireOne(scope.ServiceProvider.GetServices<IDashboardResourceStore>());
        RequireOne(scope.ServiceProvider.GetServices<IDashboardDefinitionValidator>());
        RequireOne(scope.ServiceProvider.GetServices<IDashboardAudienceDirectory>());
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    private static void RequireOne<T>(IEnumerable<T> services)
    {
        if (services.Take(2).Count() != 1) throw new InvalidOperationException($"Exactly one {typeof(T).Name} must be configured for Studio Dashboards.");
    }
}
