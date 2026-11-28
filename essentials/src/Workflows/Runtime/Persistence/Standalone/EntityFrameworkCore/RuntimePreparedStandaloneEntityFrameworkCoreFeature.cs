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
    name: "WorkflowsRuntimePreparedStandaloneEntityFrameworkCorePersistence",
    DisplayName = "Workflows Runtime EF Core Prepared Standalone Persistence",
    Description = "Qualifies protected standalone source admission and prepared Actor Foundation delivery.",
    DependsOn = new object[] { "WorkflowsRuntimePublicationEntityFrameworkCorePersistence", "Events" })]
[UsesEfModule("Workflows.Runtime")]
public sealed class RuntimePreparedStandaloneEntityFrameworkCoreFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        RuntimeStandaloneSourceEntityFrameworkRegistration.UsePreparedEntityFramework(services);
}
