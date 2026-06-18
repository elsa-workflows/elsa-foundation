using System.Reflection;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Options;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Persistence.EFCore.Sqlite.Constants;
using Elsa.Persistence.EFCore.Sqlite.Extensions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Sqlite;

/// <summary>
/// Provides Sqlite persistence for captured structured log entries, replacing the default in-memory store.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("Observability")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "DiagnosticsStructuredLogsPersistenceEFCoreSqlite",
    DisplayName = "Sqlite Structured Logs Persistence",
    Description = "Provides Sqlite persistence for diagnostics structured log entries")]
[UsedImplicitly]
public class SqliteStructuredLogsPersistenceShellFeature : EFCoreStructuredLogsPersistenceFeatureBase
{
    public SqliteStructuredLogsPersistenceShellFeature()
    {
        // The EF store is a singleton resolving IDbContextFactory<StructuredLogsDbContext>, so the factory
        // must be a singleton too (the base defaults it to Scoped, which would be a captive dependency).
        // IDbContextFactory<T> is documented as safe to consume as a singleton.
        DbContextFactoryLifetime = ServiceLifetime.Singleton;
    }

    /// <inheritdoc />
    protected override void OnAfterConfigured(IServiceCollection services)
    {
        base.OnAfterConfigured(services);

        if (string.IsNullOrWhiteSpace(ConnectionString))
            ConnectionString = SqliteConstants.DefaultConnectionString;

        services.TryAddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();
    }

    protected override void ConfigureProvider(DbContextOptionsBuilder builder, Assembly migrationsAssembly, string connectionString, ElsaDbContextOptions? options)
    {
        builder.UseElsaSqlite(migrationsAssembly, connectionString, options);
    }
}
