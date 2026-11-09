using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Weaver.Workflows;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("AI")]
[ManifestFeatureCategory("Workflows")]
// The module contributes prompt actions and result renderers only: the workflow context provider and the working-state tool
// contracts belong to Elsa.Studio.Workflows (spec 1193 FR-030), so this manifest declares no context or tool capability.
[StudioModule("weaver-workflows", "Weaver workflows", "ai-prompt-actions", "ai-proposal-renderers", "weaver-workflows", HasStyles = false)]
[ShellFeature(
    name: "WeaverWorkflowsStudio",
    DisplayName = "Weaver workflows Studio module",
    Description = "Contributes workflow-aware Weaver prompt actions and the review card for workflow changes."
)]
public sealed class WeaverWorkflowsStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
