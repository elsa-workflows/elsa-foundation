using Elsa.Persistence.EntityFramework;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore;

public static class SecretsEfModule
{
    public const string HistoryModuleName = "ElsaSecrets";
    public const string TableName = "elsa_secrets";
    public const string FilteredListIndex = "IX_elsa_secrets_tenantId_status_normalizedName";
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
