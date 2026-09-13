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
    [ManifestSetting(
        DisplayName = "Provider",
        Description = "Relational provider for the Runtime bookmark DbContext: Sqlite, SqlServer, PostgreSql, or MySql. The host must reference that provider package; this feature does not provision schema.",
        Category = "Persistence")]
    public string? Provider { get; set; }

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "Optional explicit connection string. When omitted, ConnectionName or ConnectionStrings:ElsaRuntimeBookmarks is used. Sqlite defaults to Data Source=elsa-runtime-bookmarks.db.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Connection name",
        Description = "Optional configuration connection-string name. When omitted, ElsaRuntimeBookmarks is used.",
        Category = "Persistence")]
    public string? ConnectionName { get; set; }

    public void ConfigureServices(IServiceCollection services) => services.AddRuntimeBookmarksEntityFrameworkCore(
        new RuntimeBookmarksEntityFrameworkCoreOptions
        {
            Provider = Provider ?? "Sqlite",
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName
        });
}
