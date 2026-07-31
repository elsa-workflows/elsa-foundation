using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing;

/// <summary>
/// The endpoint-free publishing ENGINE feature. It owns the auth-free workflow-publish + compile
/// logic (the executable compiler and its collaborators, the publication activator/stores, and the
/// workflow-publish orchestration handlers) so a runtime node can compose the publish capability
/// without mounting any HTTP endpoints. Authorization is a transport concern and lives in the
/// <c>WorkflowsPublishingApi</c> feature, not here.
/// </summary>
/// <remarks>
/// The Api feature obtains the engine by <c>DependsOn</c> composition (framework §2.11), not
/// inheritance: it keeps its own <c>FastEndpointsFeatureBase</c> base and declares
/// <c>DependsOn WorkflowsPublishing</c>.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Publishing")]
[ShellFeature(
    name: "WorkflowsPublishing",
    DisplayName = "Workflows Publishing",
    Description = "Endpoint-free engine that compiles designed workflow definitions into canonical executables.",
    DependsOn = new object[] { "WorkflowsRuntimeTriggers", "Events" }
)]
public class WorkflowsPublishingFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        // Phase 2 (spec 145) relocates the auth-free workflow-publish + compile engine here from
        // WorkflowsPublishingApiFeature. Intentionally empty in the scaffold checkpoint.
    }
}
