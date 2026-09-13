using Elsa.Persistence.EntityFramework;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>Schema ownership for opt-in EF persistence of tenant-local Identity IAM records.</summary>
public static class IdentityIamEfModule
{
    public const string HistoryModuleName = "ElsaIdentityIam";
    public const string ApplicationTableName = "identity_applications";
    public const string CredentialTableName = "identity_credentials";
    public const string DefaultConnectionName = "ElsaIdentity";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-identity.db";

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
