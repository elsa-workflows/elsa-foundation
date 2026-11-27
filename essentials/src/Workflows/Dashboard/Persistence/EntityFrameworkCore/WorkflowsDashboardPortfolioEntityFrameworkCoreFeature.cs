using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Dashboard")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: FeatureName,
    DisplayName: "Workflow Portfolio EF Core",
    Description = "Queries workflow definitions, drafts, and publication references for the workflow portfolio.",
    DependsOn = new object[] { "WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence", "WorkflowsDesignEntityFrameworkCore" })]
[UsesEfModule("Workflows.Design")]
[UsesEfModule("Workflows.Runtime")]
public sealed class WorkflowsDashboardPortfolioEntityFrameworkCoreFeature : IShellFeature
{
    public const string FeatureName = "WorkflowsDashboardPortfolioEntityFrameworkCore";

    public void ConfigureServices(IServiceCollection services) => services.AddWorkflowPortfolioEntityFrameworkCore();
}
