using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elsa.EntityFrameworkCore.Tooling;

/// <summary>
/// Creates a provider-derived module context for <c>dotnet ef</c>. The provider comes from the
/// context's name suffix, and migrations compile into the assembly that declares the context.
/// Generating migrations never opens the connection, so a placeholder connection string is enough;
/// out-of-process apply passes the real one through <c>ELSA_EF_CONNECTION</c>. The history table is the
/// module's own, so an out-of-process apply records exactly what a runtime validate reads.
/// </summary>
public abstract class ModuleDesignTimeFactory<TContext>(string historyTable) : IDesignTimeDbContextFactory<TContext>
    where TContext : DbContext
{
    private static readonly (string Suffix, string Provider, string Connection)[] Providers =
    [
        ("SqliteDbContext", "Sqlite", "Data Source=elsa-design-time.db"),
        ("SqlServerDbContext", "SqlServer", "Server=localhost;Database=elsa_design_time;Trusted_Connection=True;TrustServerCertificate=True"),
        ("PostgreSqlDbContext", "PostgreSql", "Host=localhost;Database=elsa_design_time"),
        ("MySqlDbContext", "MySql", "Server=localhost;Database=elsa_design_time")
    ];

    public static string Provider => Resolve().Provider;

    public TContext CreateDbContext(string[] args)
    {
        var (_, provider, placeholder) = Resolve();
        var connection = Environment.GetEnvironmentVariable("ELSA_EF_CONNECTION") is { Length: > 0 } configured ? configured : placeholder;
        var builder = new DbContextOptionsBuilder<TContext>();
        EfRelationalProviderBinding.Use(builder, provider, connection, historyTable, typeof(TContext).Assembly.GetName().Name);
        return (TContext)Activator.CreateInstance(typeof(TContext), builder.Options)!;
    }

    private static (string Suffix, string Provider, string Connection) Resolve() =>
        Providers.SingleOrDefault(candidate => typeof(TContext).Name.EndsWith(candidate.Suffix, StringComparison.Ordinal)) is { Provider: not null } match
            ? match
            : throw new InvalidOperationException($"{typeof(TContext).Name} does not end with a known provider suffix.");
}
