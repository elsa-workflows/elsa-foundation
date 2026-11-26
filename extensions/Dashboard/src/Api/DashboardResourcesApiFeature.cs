using CShells.AspNetCore.Features;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity;
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
[ShellFeature(name: "StudioDashboardApi", DisplayName = "Studio Dashboard API", Description = "Provides authorized native saved questions, dashboards and module queries.", DependsOn = ["StudioDashboardManagement", typeof(ApiCapabilitiesFeature), typeof(FoundationIdentityAbstractionsFeature)])]
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
        var composition = new DashboardResourceCompositionGuard(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService>(composition));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IShellInitializer>(composition));
        services.AddPermissionContributor<DashboardResourcePermissionContributor>();
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        DashboardResourcesApi.MapDashboardResourcesApi(endpoints);
        DashboardQueriesApi.Map(endpoints);
    }
}

internal sealed class DashboardResourceCompositionGuard(IServiceCollection services) : IHostedService, IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken) => Check(cancellationToken);
    public Task StartAsync(CancellationToken cancellationToken) => Check(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private Task Check(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var contract in new[] { typeof(IDashboardActorContext), typeof(IDashboardQueryService),
                     typeof(IDashboardResourceService), typeof(IDashboardResourceStore),
                     typeof(IDashboardDefinitionValidator), typeof(IDashboardAudienceDirectory) })
            if (services.Count(descriptor => descriptor.ServiceType == contract) != 1)
                throw new InvalidOperationException($"Exactly one {contract.Name} must be configured for Studio Dashboards.");
        return Task.CompletedTask;
    }
}
