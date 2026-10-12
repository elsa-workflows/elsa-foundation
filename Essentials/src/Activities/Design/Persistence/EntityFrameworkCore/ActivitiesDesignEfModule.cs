using Elsa.Persistence.EntityFramework;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore;

public static class ActivitiesDesignEfModule
{
    public const string HistoryModuleName = "ElsaActivitiesDesign";
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;

    /// <summary>The persisted-schema version every activity-design row is stamped with, and checked against when read.</summary>
    public const string SchemaVersion = "1.0.0";

    /// <summary>
    /// The schema family this module's rows belong to. The module maps domain types directly, so its stamp is a shadow
    /// property that <see cref="EfSchemaVersionMaterializationInterceptor"/> writes and checks.
    /// </summary>
    public const string SchemaFamily = "ActivitiesDesign";
    /// <summary>The family's one chain, from this assembly's declaration: what every reader of the family checks and
    /// upcasts through (spec 180, FR-010).</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(ActivitiesDesignEfModule).Assembly, SchemaFamily);

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
