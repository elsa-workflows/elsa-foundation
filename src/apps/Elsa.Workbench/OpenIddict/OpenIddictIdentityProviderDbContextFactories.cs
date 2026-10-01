using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elsa.Workbench.OpenIddict;

// Design-time factories for the engines other than SQLite, so `dotnet ef migrations add --context <engine context>` can scaffold
// that engine's migrations without the full application host. Each takes an optional `--connectionString <value>`.

public sealed class OpenIddictIdentitySqlServerDbContextFactory : IDesignTimeDbContextFactory<OpenIddictIdentitySqlServerDbContext>
{
    public OpenIddictIdentitySqlServerDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<OpenIddictIdentitySqlServerDbContext>();
        builder.UseSqlServer(
            DesignTime.ConnectionString(args, "Server=localhost;Database=elsa;User Id=sa;Password=design-time;TrustServerCertificate=true"),
            sql => sql
                .MigrationsAssembly(typeof(OpenIddictIdentitySqlServerDbContextFactory).Assembly)
                .MigrationsHistoryTable(OpenIddictEntityFrameworkCoreDefaults.MigrationsHistoryTable));
        return new OpenIddictIdentitySqlServerDbContext(builder.Options);
    }
}

public sealed class OpenIddictIdentityPostgreSqlDbContextFactory : IDesignTimeDbContextFactory<OpenIddictIdentityPostgreSqlDbContext>
{
    public OpenIddictIdentityPostgreSqlDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<OpenIddictIdentityPostgreSqlDbContext>();
        builder.UseNpgsql(
            DesignTime.ConnectionString(args, "Host=localhost;Database=elsa;Username=postgres;Password=design-time"),
            npgsql => npgsql
                .MigrationsAssembly(typeof(OpenIddictIdentityPostgreSqlDbContextFactory).Assembly)
                .MigrationsHistoryTable(OpenIddictEntityFrameworkCoreDefaults.MigrationsHistoryTable));
        return new OpenIddictIdentityPostgreSqlDbContext(builder.Options);
    }
}

file static class DesignTime
{
    public static string ConnectionString(string[] args, string fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--connectionString", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return fallback;
    }
}
