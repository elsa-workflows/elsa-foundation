using Elsa.Persistence.EntityFramework;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore;

public static class SecretsEfModule
{
    public const string HistoryModuleName = "ElsaSecrets";
    public const string TableName = "elsa_secrets";
    public const string FilteredListIndex = "IX_elsa_secrets_tenantId_status_normalizedName";

    /// <summary>The persisted-schema version every secret row is stamped with, and checked against when read.</summary>
    public const string SchemaVersion = "1.0.0";

    /// <summary>The schema family this module's rows belong to, checked against when a row is read.</summary>
    public const string SchemaFamily = "Secrets";
    /// <summary>The family's one chain, from this assembly's declaration: what every reader of the family checks and
    /// upcasts through (spec 180, FR-010).</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(SecretsEfModule).Assembly, SchemaFamily);
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
