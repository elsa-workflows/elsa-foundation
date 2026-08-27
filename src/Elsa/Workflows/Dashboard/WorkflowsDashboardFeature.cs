using CShells.Features;
using CShells.AspNetCore.Features;
using Elsa.Api.AspNetCore;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Foundation.Identity.Abstractions.Authorization;
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
    DependsOn = new object[] { "WorkflowsRuntimeApi", "WorkflowsDesignApi", "WorkflowDesignValidations" })]
public sealed class WorkflowsDashboardFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaEndpoints();
        services.TryAddScoped<IWorkflowRunHealthDataSource>(provider =>
        {
            var executions = provider.GetRequiredService<IWorkflowExecutionStateStore>();
            var incidents = provider.GetRequiredService<IIncidentStateStore>();
            return executions is InMemoryWorkflowExecutionStateStore inMemoryExecutions &&
                   incidents is InMemoryIncidentStateStore inMemoryIncidents
                ? new InMemoryWorkflowRunHealthDataSource(inMemoryExecutions, inMemoryIncidents)
                : new UnavailableWorkflowRunHealthDataSource();
        });
        services.TryAddScoped<IWorkflowRunHealthService, WorkflowRunHealthService>();
        services.TryAddScoped<IWorkflowPortfolioDataSource, UnavailableWorkflowPortfolioDataSource>();
        services.TryAddScoped<IWorkflowPortfolioService>(provider => new WorkflowPortfolioService(
            provider.GetRequiredService<IWorkflowPortfolioDataSource>(),
            provider.GetRequiredService<Elsa.Events.Core.Contracts.IInlineEventPublisher>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkflowPortfolioOptions>>().Value));
        services.AddOptions<WorkflowPortfolioOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionContributor, WorkflowsDashboardPermissionContributor>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        WorkflowsDashboardApi.MapWorkflowsDashboardApi(endpoints);
}
