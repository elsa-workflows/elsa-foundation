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
    name: "WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence",
    DisplayName = "Workflows Runtime EF Core Bookmark Persistence",
    Description = "Opt-in EF Core persistence for runtime bookmark state and stimulus lookup. Groundwork remains the default.",
    DependsOn = new object[] { "WorkflowsRuntime" })]
public sealed class RuntimeBookmarksEntityFrameworkCoreFeature : IShellFeature
{
    public string? Provider { get; set; }
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }

    public void ConfigureServices(IServiceCollection services) => services.AddRuntimeBookmarksEntityFrameworkCore(
        new RuntimeBookmarksEntityFrameworkCoreOptions
        {
            Provider = Provider ?? "Sqlite",
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName
        });
}
