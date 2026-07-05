using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Sqlite;

/// <summary>
/// Design-time factory for <see cref="ApplicationIdentityDbContext"/> so <c>dotnet ef migrations</c> can
/// build the model without the full application host. Accepts an optional <c>--connectionString &lt;value&gt;</c>
/// argument, defaulting to a local Sqlite file. The identity context derives from ASP.NET Core Identity's
/// <c>IdentityDbContext</c> rather than Elsa's <c>ElsaDbContextBase</c>, so this is self-contained rather
/// than reusing the repo's EF-Core design-time base.
/// </summary>
public sealed class ApplicationIdentityDbContextFactory : IDesignTimeDbContextFactory<ApplicationIdentityDbContext>
{
    public ApplicationIdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = ParseConnectionString(args) ?? "Data Source=identity.db";

        var builder = new DbContextOptionsBuilder<ApplicationIdentityDbContext>();
        builder.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(ApplicationIdentityDbContextFactory).Assembly.GetName().Name));

        return new ApplicationIdentityDbContext(builder.Options);
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
