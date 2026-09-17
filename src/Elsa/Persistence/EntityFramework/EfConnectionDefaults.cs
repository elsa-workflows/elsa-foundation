using Microsoft.Extensions.Configuration;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Where a first-party EF module connects when a host names neither a connection string nor a connection
/// name. Every module shares one connection by default, so a host configures <c>ConnectionStrings:Elsa</c>
/// once and overrides a module only when it deliberately splits storage.
/// </summary>
public static class EfConnectionDefaults
{
    public const string ConnectionName = "Elsa";
    public const string SqliteConnectionString = "Data Source=elsa.db";

    /// <summary>
    /// An explicit connection string wins, then a named <c>ConnectionStrings</c> entry, then the module's default entry,
    /// then the SQLite default file. A named entry that is missing or blank is refused rather than replaced by a
    /// default, because the host asked for that connection by name.
    /// </summary>
    public static string ResolveConnectionString(
        IServiceProvider services,
        string owner,
        string provider,
        string? connectionString,
        string? connectionName,
        string defaultConnectionName = ConnectionName,
        string defaultSqliteConnectionString = SqliteConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (!string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        var configuration = (IConfiguration?)services.GetService(typeof(IConfiguration));
        if (!string.IsNullOrWhiteSpace(connectionName))
        {
            var named = configuration?.GetConnectionString(connectionName);
            return string.IsNullOrWhiteSpace(named)
                ? throw new InvalidOperationException($"{owner} EF connection '{connectionName}' was not found or was empty in ConnectionStrings.")
                : named;
        }

        var fallback = configuration?.GetConnectionString(defaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;
        if (EfRelationalProviderBinding.Normalize(provider) == "sqlite")
            return defaultSqliteConnectionString;
        throw new InvalidOperationException(
            $"{owner} EF requires ConnectionString or ConnectionName, or ConnectionStrings:{defaultConnectionName}, for a non-Sqlite provider.");
    }
}
