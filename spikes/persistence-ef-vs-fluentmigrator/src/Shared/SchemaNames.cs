namespace Elsa.Persistence.Spike;

/// <summary>Per-module history/version table names so multiple modules can share one database.</summary>
public static class SchemaNames
{
    public const string Table = "ElsaSecrets";
    public const string UniqueIndex = "IX_ElsaSecrets_TenantId_Name";

    /// <summary>EF Core migrations history table. Default is <c>__EFMigrationsHistory</c>; that collides across modules.</summary>
    public const string EfHistoryTable = "__EFMigrationsHistory_ElsaSecrets";

    /// <summary>FluentMigrator version table. Default is <c>VersionInfo</c>; that also collides across modules.</summary>
    public const string FluentMigratorVersionTable = "VersionInfo_ElsaSecrets";
}
