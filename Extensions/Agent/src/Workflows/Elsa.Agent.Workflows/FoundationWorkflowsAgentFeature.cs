using CShells.Features;
using Elsa.Agent.Workflows.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Workflows;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(
    name: "FoundationWorkflowsAgent",
    DisplayName = "Foundation Workflows Agent",
    Description = "Registers workflow explanation, troubleshooting, and reviewable workflow-change proposal seams for the agent foundation.",
    DependsOn = new object[] { typeof(FoundationAgentAbstractionsFeature) }
)]
public class FoundationWorkflowsAgentFeature : FoundationAgentAbstractionsFeature
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddFoundationWorkflowsAgent();
    }
}
