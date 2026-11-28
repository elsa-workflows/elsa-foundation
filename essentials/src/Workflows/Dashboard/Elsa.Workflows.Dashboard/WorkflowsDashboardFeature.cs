using Elsa.Api.Capabilities;
using CShells.Features;
using CShells.AspNetCore.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Workflows.Dashboard;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Dashboard")]
[ShellFeature(
    name: "WorkflowsDashboard",
    DisplayName = "Workflow Dashboard",
    Description = "Provides exact workflow run-health dashboard data.",
    DependsOn = new object[] { "WorkflowsRuntimeApi", "WorkflowsDesignApi", "WorkflowDesignValidations", typeof(ApiCapabilitiesFeature) })]
public sealed class WorkflowsDashboardFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton(new LegacyWorkflowDashboardSelected());
        services.TryAddScoped<IWorkflowRunHealthDataSource>(provider =>
        {
            var executions = provider.GetRequiredService<IWorkflowExecutionStateStore>();
            var incidents = provider.GetRequiredService<IIncidentStateStore>();
            return executions is InMemoryWorkflowExecutionStateStore inMemoryExecutions &&
                   incidents is InMemoryIncidentStateStore inMemoryIncidents
                ? new InMemoryWorkflowRunHealthDataSource(inMemoryExecutions, inMemoryIncidents)
                : new UnavailableWorkflowRunHealthDataSource();
        });
        WorkflowsDashboardServices.Configure(services, "WorkflowsDashboard");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        WorkflowsDashboardApi.MapWorkflowsDashboardApi(endpoints);
}
