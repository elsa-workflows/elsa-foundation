using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Publishing")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsPublishingEntityFrameworkCore",
    DisplayName = "Workflows Publishing EF Core Persistence",
    Description = "Opt-in EF Core persistence for publication snapshot reviews. Groundwork remains the default.",
    DependsOn = new object[] { "WorkflowsPublishing" })]
public class PublishingEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider: Sqlite, SqlServer, PostgreSql, or MySql.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit connection string.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Optional configuration connection-string name.", Category = "Persistence")]
    public string? ConnectionName { get; set; }

    public virtual void ConfigureServices(IServiceCollection services) =>
        services.AddPublishingEntityFrameworkCore(new PublishingEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName
        });
}
