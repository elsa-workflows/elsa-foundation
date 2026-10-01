using CShells.Features;
using Elsa.Locking.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Medallion.Threading.MySql;
using Medallion.Threading.Postgres;
using Medallion.Threading.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Locking.Database;

/// <summary>
/// Distributed locks held in a shared PostgreSQL, SQL Server or MySQL database (#2192): what a cluster composes, so a lock
/// one node holds excludes every other node, not only the other processes on its machine.
/// </summary>
/// <remarks>
/// <para>
/// Each lock is the engine's own session lock (a PostgreSQL advisory lock, SQL Server's <c>sp_getapplock</c>, MySQL's
/// <c>GET_LOCK</c>) through Medallion's providers, so it needs no table and no migration, and it is released when its
/// connection closes, including when the process dies. A handle reports a lost connection through
/// <c>HandleLostToken</c>.
/// </para>
/// <para>
/// SQLite has no such provider, and is refused here: a SQLite composition keeps <c>FileSystemDistributedLocking</c> and
/// is single-node by definition. See the Locking domain's <c>EXTENSION_POINTS.md</c>.
/// </para>
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Locking")]
[ManifestFeatureCategory("Infrastructure")]
[ShellFeature(
    name: "DatabaseDistributedLocking",
    DisplayName = "Database Distributed Locking",
    Description = "Provides distributed locks held in a shared PostgreSQL, SQL Server or MySQL database, so every node of a cluster excludes every other."
)]
public class DatabaseLockingFeature : IShellFeature
{
    /// <summary>The <c>ConnectionStrings</c> entry used when neither a connection string nor a connection name is set.</summary>
    public const string DefaultConnectionName = "Elsa";

    private const string FeatureName = "DatabaseDistributedLocking";

    [ManifestSetting(DisplayName = "Provider", Description = "The database engine that holds the locks: PostgreSql, SqlServer or MySql. SQLite has no database lock; compose FileSystemDistributedLocking there instead.", Category = "Locking", Required = true)]
    public string? Provider { get; set; }

    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit connection string. When omitted, ConnectionName or the shared ConnectionStrings:Elsa is used. Every node must reach the same primary.", Category = "Locking", Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Connection name", Description = "Named connection under ConnectionStrings when ConnectionString is omitted.", Category = "Locking")]
    public string? ConnectionName { get; set; }

    [ManifestSetting(DisplayName = "Lock acquisition timeout", Description = "Maximum time in minutes to wait when acquiring a distributed lock.", Category = "Locking", DefaultValue = "10")]
    public double LockAcquisitionTimeoutMinutes { get; set; } = 10;

    public void ConfigureServices(IServiceCollection services)
    {
        // Refused here, at shell build, rather than when the first lock is taken.
        var engine = ParseProvider(Provider);
        var defaultTimeout = TimeSpan.FromMinutes(LockAcquisitionTimeoutMinutes);
        var connectionString = ConnectionString;
        var connectionName = ConnectionName;

        services.AddSingleton<IDistributedLockProvider>(serviceProvider =>
        {
            var resolved = ResolveConnectionString(serviceProvider.GetService<IConfiguration>(), connectionString, connectionName);
            Medallion.Threading.IDistributedLockProvider medallion = engine switch
            {
                DatabaseLockEngine.PostgreSql => new PostgresDistributedSynchronizationProvider(resolved),
                DatabaseLockEngine.SqlServer => new SqlDistributedSynchronizationProvider(resolved),
                _ => new MySqlDistributedSynchronizationProvider(resolved)
            };
            return new MedallionDistributedLockProvider(medallion, defaultTimeout);
        });
    }

    private static DatabaseLockEngine ParseProvider(string? provider) =>
        provider?.Trim().ToLowerInvariant() switch
        {
            "postgresql" or "postgres" or "npgsql" => DatabaseLockEngine.PostgreSql,
            "sqlserver" or "sql server" or "mssql" => DatabaseLockEngine.SqlServer,
            "mysql" or "my sql" => DatabaseLockEngine.MySql,
            "sqlite" => throw new InvalidOperationException(
                $"{FeatureName}:{nameof(Provider)} is Sqlite, which has no database lock. A SQLite composition is single-node by " +
                "definition: compose FileSystemDistributedLocking instead, with a LocksFolderPath every process of that node shares."),
            _ => throw new InvalidOperationException(
                $"{FeatureName}:{nameof(Provider)} is '{provider}'. Set it to PostgreSql, SqlServer or MySql, the engine whose " +
                "database every node of the cluster shares.")
        };

    /// <summary>
    /// An explicit connection string wins, then a named <c>ConnectionStrings</c> entry, then <c>ConnectionStrings:Elsa</c>,
    /// as for the EF modules. A named entry that is missing or blank is refused rather than replaced by the default, because
    /// the host asked for that connection by name.
    /// </summary>
    private static string ResolveConnectionString(IConfiguration? configuration, string? connectionString, string? connectionName)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        if (!string.IsNullOrWhiteSpace(connectionName))
            return configuration?.GetConnectionString(connectionName) is { } named && !string.IsNullOrWhiteSpace(named)
                ? named
                : throw new InvalidOperationException($"{FeatureName} connection '{connectionName}' was not found or was empty in ConnectionStrings.");

        return configuration?.GetConnectionString(DefaultConnectionName) is { } fallback && !string.IsNullOrWhiteSpace(fallback)
            ? fallback
            : throw new InvalidOperationException(
                $"{FeatureName} requires {nameof(ConnectionString)} or {nameof(ConnectionName)}, or ConnectionStrings:{DefaultConnectionName}.");
    }

    private enum DatabaseLockEngine
    {
        PostgreSql,
        SqlServer,
        MySql
    }
}
