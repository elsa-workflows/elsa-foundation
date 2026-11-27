using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.Capabilities;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Workflows.Dashboard;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Dashboard")]
[ShellFeature(
    name: FeatureName,
    DisplayName = "Workflow Dashboard for retained workflows",
    Description = "Serves the workflow portfolio without the legacy runner. Actor run health reports unavailable until exact statistics can be read.",
    DependsOn = new object[] { "WorkflowsDesignApi", "WorkflowDesignValidations", typeof(ApiCapabilitiesFeature) })]
public sealed class WorkflowsDashboardRetainedFeature : IWebShellFeature, IPostConfigureShellServices
{
    public const string FeatureName = "WorkflowsDashboardRetained";

    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddScoped<IWorkflowRunHealthDataSource, UnavailableWorkflowRunHealthDataSource>();
        WorkflowsDashboardFeature.ConfigureDashboardServices(services, FeatureName);
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        if (services.Any(service => !service.IsKeyedService && service.ServiceType == typeof(LegacyWorkflowDashboardSelected)))
            throw new InvalidOperationException($"Select either {FeatureName} or WorkflowsDashboard because they serve the same routes.");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        WorkflowsDashboardApi.MapWorkflowsDashboardApi(endpoints);
}
