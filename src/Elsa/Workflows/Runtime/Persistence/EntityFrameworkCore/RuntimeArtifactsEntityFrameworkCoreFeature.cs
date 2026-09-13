using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence",
    DisplayName = "Workflows Runtime EF Core Executable Artifact Persistence",
    Description = "Opt-in EF Core persistence for runtime executable artifacts, templates and source references. Groundwork remains the default.",
    DependsOn = new object[] { "WorkflowsRuntime" })]
public sealed class RuntimeArtifactsEntityFrameworkCoreFeature : IShellFeature
{
    public string? Provider { get; set; }
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }

    public void ConfigureServices(IServiceCollection services) => services.AddRuntimeArtifactsEntityFrameworkCore(new RuntimeArtifactsEntityFrameworkCoreOptions
    {
        Provider = Provider ?? "Sqlite",
        ConnectionString = ConnectionString,
        ConnectionName = ConnectionName
    });
}
