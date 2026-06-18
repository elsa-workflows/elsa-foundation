using CShells.Features;
using Elsa.Foundation.Workflows.Agent.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Workflows.Agent;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(
    name: "FoundationWorkflowsAgent",
    DisplayName = "Foundation Workflows Agent",
    Description = "Registers workflow explanation, troubleshooting, and reviewable workflow-change proposal seams for the agent foundation."
)]
public sealed class FoundationWorkflowsAgentFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationWorkflowsAgent();
    }
}
