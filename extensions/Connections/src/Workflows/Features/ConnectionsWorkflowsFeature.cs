using CShells.Features;
using Elsa.Connections.Workflows.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Connections.Workflows.Features;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Connections")]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(
    name: "ConnectionsWorkflows",
    DisplayName = "Connections Workflow Resolution",
    Description = "Applies a bound http connection to SendHttpRequest at send time and masks revealed credentials.",
    DependsOn = new object[] { "Connections", "ActivitiesHttp", "SecretsWorkflows" }
)]
public class ConnectionsWorkflowsFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddConnectionsWorkflows();
}
