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
    Description = "Opt-in EF Core persistence for Elsa 3 reusable-activity imports. The receipt, provenance bindings, and Activity and Workflow Design rows commit in one transaction, so the Activities and Workflows Design EF Core lanes must use the same database.",
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

    [ManifestSetting(
        DisplayName = "Schema",
        Description = "Optional database schema for this module's tables and its own migrations history table. Falls back to Elsa:Persistence:EntityFramework:Schema, then to the provider's own default. Ignored on Sqlite, which has no schemas, and refused on MySql, where a schema is a database: name it in the connection string there instead.",
        Category = "Persistence")]
    public string? Schema { get; set; }

    [ManifestSetting(
        DisplayName = "Pooled contexts",
        Description = "Reuse DbContext instances from a pool instead of constructing one per scope. Safe for every first-party module context, which carries nothing but its options.",
        Category = "Persistence")]
    public bool Pooling { get; set; }

    public virtual void ConfigureServices(IServiceCollection services) =>
        services.AddElsa3ImportEntityFrameworkCore(new Elsa3ImportEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName, Schema = Schema, Pooling = Pooling
        })
        .AddEfModuleMigrations<Elsa3ImportDbContext>(Provider);
}
