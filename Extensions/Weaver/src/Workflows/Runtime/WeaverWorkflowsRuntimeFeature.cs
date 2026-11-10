using CShells.Features;
using Elsa.Weaver.Workflows.Runtime.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Workflows.Runtime;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Weaver")]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(
    name: "WeaverWorkflowsRuntime",
    DisplayName = "Foundation Workflows Runtime Agent",
    Description = "Registers read-only Weaver context and tools for workflow Executables and exact workflow Runs, read through Workflows Runtime Core under the host's runtime read permission.",
    DependsOn = new object[] { typeof(WeaverFeature) }
)]
public class WeaverWorkflowsRuntimeFeature : WeaverFeature
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddWeaverWorkflowsRuntime();
    }
}
