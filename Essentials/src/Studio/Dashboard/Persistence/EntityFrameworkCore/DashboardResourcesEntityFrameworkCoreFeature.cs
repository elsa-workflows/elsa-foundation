using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(name: "StudioDashboardEntityFrameworkCore", DisplayName = "Studio Dashboard Entity Framework Core",
    Description = "Persists named dashboard/question definitions, audiences, revisions and keyed operations.",
    DependsOn = ["StudioDashboardManagement"])]
[UsesEfModule("Studio.Dashboard")]
public class DashboardResourcesEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Category = "Persistence", Description = "Sqlite; relational provider migrations follow in the next slice.")]
    public string Provider { get; set; } = "Sqlite";
    [ManifestSetting(DisplayName = "Connection string", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }
    [ManifestSetting(DisplayName = "Connection name", Category = "Persistence")]
    public string? ConnectionName { get; set; }
    [ManifestSetting(DisplayName = "Schema", Category = "Persistence")]
    public string? Schema { get; set; }
    public virtual void ConfigureServices(IServiceCollection services) =>
        services.AddDashboardResourcesEntityFrameworkCore(new() { Provider = Provider, ConnectionString = ConnectionString,
            ConnectionName = ConnectionName, Schema = Schema }).AddEfModuleMigrations<DashboardResourceDbContext>(Provider);
}
