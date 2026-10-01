using Elsa.Persistence.EntityFramework;
using Elsa.Workbench.OpenIddict;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elsa.Workbench.OpenIddictEngines;

// Design-time factories for the engines other than SQLite, so `dotnet ef migrations add --context <engine context>` can scaffold
// that engine's migrations without the full application host. Each takes an optional `--connectionString <value>`, and binds the
// engine as the host does, through EfRelationalProviderBinding.

public sealed class OpenIddictIdentitySqlServerDbContextFactory : IDesignTimeDbContextFactory<OpenIddictIdentitySqlServerDbContext>
{
    public OpenIddictIdentitySqlServerDbContext CreateDbContext(string[] args) =>
        DesignTime.Create<OpenIddictIdentitySqlServerDbContext>(
            args,
            "SqlServer",
            "Server=localhost;Database=elsa;User Id=sa;Password=design-time;TrustServerCertificate=true",
            options => new OpenIddictIdentitySqlServerDbContext(options));
}

public sealed class OpenIddictIdentityPostgreSqlDbContextFactory : IDesignTimeDbContextFactory<OpenIddictIdentityPostgreSqlDbContext>
{
    public OpenIddictIdentityPostgreSqlDbContext CreateDbContext(string[] args) =>
        DesignTime.Create<OpenIddictIdentityPostgreSqlDbContext>(
            args,
            "PostgreSql",
            "Host=localhost;Database=elsa;Username=postgres;Password=design-time",
            options => new OpenIddictIdentityPostgreSqlDbContext(options));
}

file static class DesignTime
{
    public static TContext Create<TContext>(string[] args, string provider, string fallbackConnectionString, Func<DbContextOptions<TContext>, TContext> create)
        where TContext : OpenIddictIdentityDbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        EfRelationalProviderBinding.UseMigrationsFrom(
            builder,
            provider,
            ConnectionString(args) ?? fallbackConnectionString,
            EfMigrationsHistory.TableName(WorkbenchOpenIddictStoreProvider.Module),
            typeof(TContext).Assembly,
            OpenIddictIdentityDbContext.Schema);
        return create(builder.Options);
    }

    private static string? ConnectionString(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--connectionString", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
