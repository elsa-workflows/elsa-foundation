using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;

[assembly: EfModule("Studio.Dashboard", typeof(DashboardResourceDbContext), HistoryModule = DashboardResourcesEfModule.HistoryModuleName,
    Sqlite = typeof(DashboardResourcesSqliteDbContext), SqlServer = typeof(DashboardResourcesSqlServerDbContext),
    PostgreSql = typeof(DashboardResourcesPostgreSqlDbContext), MySql = typeof(DashboardResourcesMySqlDbContext), DisplayName = "Studio Dashboards")]
[assembly: ManifestExtension("efModules", "Studio.Dashboard")]
[assembly: EfSchemaFamily(DashboardResourcesEfModule.SchemaFamily, "Studio.Dashboard", DashboardResourcesEfModule.SchemaVersion)]
[assembly: EfSchemaContent(DashboardResourcesEfModule.SchemaFamily, typeof(DashboardResourceRow), nameof(DashboardResourceRow.DocumentJson))]

namespace Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;

public static class DashboardResourcesEfModule
{
    public const string HistoryModuleName = "ElsaStudioDashboard";
    public const string SchemaVersion = "1.0.0";
    public const string SchemaFamily = "StudioDashboard";
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(DashboardResourcesEfModule).Assembly, SchemaFamily);
}
