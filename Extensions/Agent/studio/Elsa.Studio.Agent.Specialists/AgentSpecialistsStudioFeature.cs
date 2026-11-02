using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Agent.Specialists;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Agent")]
[StudioModule("agent-specialists", "Agents", "routes", "http")]
[ShellFeature(
    name: "AgentSpecialistsStudio",
    DisplayName = "Agents Studio module",
    Description = "Contributes the human review page for exact content awaiting a decision in a workflow."
)]
public class AgentSpecialistsStudioFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
    }
}
