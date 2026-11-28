using CShells.Features;
using Elsa.Connections.Workflows.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Connections.Workflows.Features;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Connections")]
[ManifestFeatureCategory("Workflows")]
[ShellFeature(
    name: "ActorHttpConnections",
    DisplayName = "Actor HTTP Connections",
    Description = "Applies tenant-owned HTTP Connections with non-refreshable authentication to protected Actor workflow jobs.",
    DependsOn = new object[] { "Connections", "WorkflowsRuntimeKernelHosting" })]
public sealed class ActorHttpConnectionsFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.TryAddScoped<IWorkflowHttpConnectionApplier, ActorHttpConnectionApplicator>();
}
