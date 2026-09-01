using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elsa.Workbench.OpenIddict.Sqlite;

/// <summary>
/// Design-time factory for <see cref="OpenIddictIdentityDbContext"/> so <c>dotnet ef migrations</c> can build
/// the model without the full application host. Accepts an optional <c>--connectionString &lt;value&gt;</c>
/// argument, defaulting to the shared identity database file. Mirrors the identity module's factory.
/// </summary>
public sealed class OpenIddictIdentityDbContextFactory : IDesignTimeDbContextFactory<OpenIddictIdentityDbContext>
{
    public OpenIddictIdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = ParseConnectionString(args) ?? OpenIddictEntityFrameworkCoreDefaults.DefaultConnectionString;

        var builder = new DbContextOptionsBuilder<OpenIddictIdentityDbContext>();
        builder.UseSqlite(connectionString, sqlite => sqlite
            .MigrationsAssembly(typeof(OpenIddictIdentityDbContextFactory).Assembly.GetName().Name)
            .MigrationsHistoryTable(OpenIddictEntityFrameworkCoreDefaults.MigrationsHistoryTable, OpenIddictIdentityDbContext.Schema));

        return new OpenIddictIdentityDbContext(builder.Options);
    }

    private static string? ParseConnectionString(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--connectionString", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
