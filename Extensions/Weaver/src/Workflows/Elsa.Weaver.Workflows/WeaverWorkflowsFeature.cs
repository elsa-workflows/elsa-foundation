using CShells.Features;
using Elsa.Weaver.Workflows.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Workflows;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Weaver")]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(
    name: "WeaverWorkflows",
    DisplayName = "Foundation Workflows Weaver",
    Description = "Registers workflow explanation, troubleshooting, and reviewable workflow-change proposal seams for the Weaver foundation.",
    DependsOn = new object[] { typeof(WeaverFeature) }
)]
public class WeaverWorkflowsFeature : WeaverFeature
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddWeaverWorkflows();
    }
}
