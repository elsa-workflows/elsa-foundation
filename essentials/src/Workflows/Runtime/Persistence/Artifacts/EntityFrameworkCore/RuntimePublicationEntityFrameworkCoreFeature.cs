using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsRuntimePublicationEntityFrameworkCorePersistence",
    DisplayName = "Workflows Runtime EF Core Publication Persistence",
    Description = "Persists activation slots, workflow trigger and recurring schedule projections, and Kernel Event and schedule state in the Runtime artifact database.",
    DependsOn = new object[] { "WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence" })]
[UsesEfModule("Workflows.Runtime")]
public sealed class RuntimePublicationEntityFrameworkCoreFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        RuntimePublicationEntityFrameworkCoreRegistration.UseEntityFramework(services);
}
