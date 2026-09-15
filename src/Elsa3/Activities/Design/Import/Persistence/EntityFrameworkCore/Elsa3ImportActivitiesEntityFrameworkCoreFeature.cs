using Elsa.Persistence.EntityFramework;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Elsa3")]
[ManifestFeatureCategory("Import")]
[ShellFeature(
    name: "Elsa3ImportActivitiesEntityFrameworkCore",
    DisplayName = "Elsa 3 Activity Import EF Core Persistence",
    Description = "Opt-in EF Core persistence for Elsa 3 reusable-activity imports. The receipt, provenance bindings, and Activity and Workflow Design rows commit in one transaction, so the Activities and Workflows Design EF Core lanes must use the same database. Groundwork remains the default.",
    // The command enlists the Activities and Workflows Design EF contexts in its transaction, and this
    // feature registers neither; naming both lanes is what makes selecting it alone compose correctly.
    DependsOn = new object[] { "ActivitiesDesignEntityFrameworkCore", "WorkflowsDesignEntityFrameworkCore" })]
public class Elsa3ImportActivitiesEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Sqlite, SqlServer, PostgreSql, or MySql. Must match the Design EF Core lanes.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit connection string. Must equal the Design EF Core lanes' connection string.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Optional named connection under ConnectionStrings.", Category = "Persistence")]
    public string? ConnectionName { get; set; }

    public virtual void ConfigureServices(IServiceCollection services) =>
        services.AddElsa3ImportEntityFrameworkCore(new Elsa3ImportEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName
        })
        .AddEfModuleMigrations<Elsa3ImportDbContext>(Provider);
}
