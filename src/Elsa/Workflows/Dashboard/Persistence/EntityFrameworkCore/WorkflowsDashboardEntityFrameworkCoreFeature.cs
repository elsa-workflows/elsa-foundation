using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore;

/// <summary>
/// Routes the dashboard's run-health and portfolio queries to the EF Core Runtime and Workflows Design projections:
/// the counterpart of the Groundwork workflow dashboard feature. Without it the dashboard falls back to its
/// unavailable sources even though the durable projections exist.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Dashboard")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsDashboardEntityFrameworkCore",
    DisplayName = "Workflows Dashboard EF Core",
    Description = "Routes workflow run-health and portfolio dashboard queries to the EF Core Runtime and Workflows Design projections.",
    DependsOn = new object[] { "WorkflowsRuntimeEntityFrameworkCore", "WorkflowsDesignEntityFrameworkCore" })]
public sealed class WorkflowsDashboardEntityFrameworkCoreFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddWorkflowRunHealthEntityFrameworkCore();
        services.AddWorkflowPortfolioEntityFrameworkCore();
    }
}
