using CShells.Features;
using Elsa.Workflows.Dashboard.Persistence.Groundwork.V2;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workbench;

/// <summary>
/// Preserves the v2 dashboard projections that the retired provider-specific presets installed as a side
/// effect. This is deliberately a shell feature: the Groundwork storage declarations and target resolution must
/// live in the same shell container as the lane features that own the dashboard's design and runtime units.
/// </summary>
[ShellFeature(
    name: "GroundworkWorkflowDashboard",
    DisplayName = "Groundwork Workflow Dashboard",
    Description = "Routes workflow run-health and portfolio dashboard queries to the Groundwork v2 projections.",
    DependsOn = new object[]
    {
        "GroundworkWorkflowRuntime",
        "WorkflowsDesignGroundworkPersistence",
        "WorkflowsPublishingGroundwork"
    })]
public sealed class WorkbenchGroundworkDashboardFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddGroundworkV2WorkflowRunHealth();
        services.AddGroundworkV2WorkflowPortfolio();
    }
}
