using Elsa.Persistence.EntityFramework;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>Names and provider-neutral settings for the Identity provider-configuration EF slice.</summary>
public static class IdentityProviderConfigurationEfModule
{
    public const string HistoryModuleName = "ElsaIdentityProviderConfiguration";
    public const string TenantTableName = "identity_provider_configurations";
    public const string GlobalTableName = "identity_global_provider_configurations";

    /// <summary>The persisted-schema version every provider-configuration row is stamped with, and checked against when read.</summary>
    public const string SchemaVersion = "1.0.0";

    /// <summary>The schema family this module's rows belong to, checked against when a row is read.</summary>
    public const string SchemaFamily = "IdentityProviderConfiguration";
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
