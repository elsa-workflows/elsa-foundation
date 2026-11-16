namespace Elsa.Persistence.EntityFramework;

/// <summary>Well-known <c>Database.ProviderName</c> values for the default relational packs.</summary>
public static class EfProviderNames
{
    public const string Sqlite = "Microsoft.EntityFrameworkCore.Sqlite";
    public const string SqlServer = "Microsoft.EntityFrameworkCore.SqlServer";
    public const string PostgreSql = "Npgsql.EntityFrameworkCore.PostgreSQL";
    public const string MySql = "MySql.EntityFrameworkCore";
}
