using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Dashboard;

internal static class WorkflowsDashboardServices
{
    internal static void Configure(IServiceCollection services, string featureName)
    {
        services.AddElsaEndpoints();
        services.AddApiCapability(new ApiCapabilityDeclaration(
            "elsa.api.workflow-dashboard", 1,
            [new("workflow-portfolio", "_elsa/workflows/dashboard/definitions"), new("workflow-run-health", "_elsa/workflows/dashboard/runs")],
            featureName));
        services.TryAddScoped<IWorkflowRunHealthService, WorkflowRunHealthService>();
        services.TryAddScoped<IWorkflowPortfolioDataSource, UnavailableWorkflowPortfolioDataSource>();
        services.TryAddScoped<IWorkflowPortfolioService>(provider => new WorkflowPortfolioService(
            provider.GetRequiredService<IWorkflowPortfolioDataSource>(),
            provider.GetRequiredService<Elsa.Events.Core.Contracts.IInlineEventPublisher>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkflowPortfolioOptions>>().Value));
        services.AddOptions<WorkflowPortfolioOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionContributor, WorkflowsDashboardPermissionContributor>());
    }
}

internal sealed class LegacyWorkflowDashboardSelected;
