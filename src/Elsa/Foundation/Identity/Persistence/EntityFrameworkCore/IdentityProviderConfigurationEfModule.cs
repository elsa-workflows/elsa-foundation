using Elsa.Persistence.EntityFramework;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>Names and provider-neutral settings for the Identity provider-configuration EF slice.</summary>
public static class IdentityProviderConfigurationEfModule
{
    public const string HistoryModuleName = "ElsaIdentityProviderConfiguration";
    public const string TenantTableName = "identity_provider_configurations";
    public const string GlobalTableName = "identity_global_provider_configurations";
    public const string DefaultConnectionName = "ElsaIdentity";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-identity.db";

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
