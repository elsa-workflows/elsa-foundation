using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Agent;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Agent")]
[StudioModule("agent", "Agents", "navigation", "routes", "http", "activity-editors")]
[ShellFeature(
    name: "AgentStudio",
    DisplayName = "Agents Studio module",
    Description = "Contributes the human review page for exact content awaiting a decision in a workflow."
)]
public class AgentStudioFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
    }
}
