using CShells.Features;
using Elsa.Agent.Workflows.Runtime.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Workflows.Runtime;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(
    name: "FoundationWorkflowsRuntimeAgent",
    DisplayName = "Foundation Workflows Runtime Agent",
    Description = "Registers read-only Weaver context and tools for workflow Executables and exact workflow Runs, read through Workflows Runtime Core under the host's runtime read permission.",
    DependsOn = new object[] { typeof(FoundationAgentAbstractionsFeature) }
)]
public class FoundationWorkflowsRuntimeAgentFeature : FoundationAgentAbstractionsFeature
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddFoundationWorkflowsRuntimeAgent();
    }
}
