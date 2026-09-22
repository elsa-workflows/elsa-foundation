using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Weaver.Workflows;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("AI")]
[ManifestFeatureCategory("Workflows")]
[StudioModule("weaver-workflows", "Weaver workflows", "1.0.0", "ai-context-providers", "ai-prompt-actions", "ai-proposal-renderers", "ai-tools", "weaver-workflows", HasStyles = false)]
[ShellFeature(
    name: "WeaverWorkflowsStudio",
    DisplayName = "Weaver workflows Studio module",
    Description = "Contributes workflow-aware Weaver context, prompt actions, tools, and proposal review surfaces."
)]
public sealed class WeaverWorkflowsStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
