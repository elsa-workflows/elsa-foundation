using System.Reflection;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Options;
using Elsa.Persistence.EFCore.Sqlite.Constants;
using Elsa.Persistence.EFCore.Sqlite.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.EFCore.Sqlite;

/// <summary>
/// Shared configuration for the per-domain SQLite persistence shell features. Each feature derives from a
/// different EF Core persistence base, so a common base class is unavailable; these helpers collapse the
/// identical connection-string defaulting, model-creating handler registration and provider wiring that
/// every SQLite feature otherwise repeats.
/// </summary>
public static class SqliteShellFeatureDefaults
{
    /// <summary>
    /// Registers the SQLite model-creating handler and returns the connection string to use, falling back
    /// to <see cref="SqliteConstants.DefaultConnectionString"/> when <paramref name="connectionString"/> is
    /// blank. Callers assign the result back to their <c>ConnectionString</c> setting.
    /// </summary>
    public static string ApplyDefaults(IServiceCollection services, string? connectionString)
    {
        services.TryAddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();
        return string.IsNullOrWhiteSpace(connectionString) ? SqliteConstants.DefaultConnectionString : connectionString;
    }

    /// <summary>Configures the EF Core context builder to use Elsa's SQLite provider.</summary>
    public static void ConfigureProvider(DbContextOptionsBuilder builder, Assembly migrationsAssembly, string connectionString, ElsaDbContextOptions? options) =>
        builder.UseElsaSqlite(migrationsAssembly, connectionString, options);
}
