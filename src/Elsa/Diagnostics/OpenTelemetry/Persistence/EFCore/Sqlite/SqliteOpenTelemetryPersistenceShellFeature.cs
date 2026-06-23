using System.Reflection;
using CShells.Features;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Options;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Persistence.EFCore.Sqlite.Constants;
using Elsa.Persistence.EFCore.Sqlite.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Sqlite;

/// <summary>
/// Provides Sqlite persistence for OpenTelemetry diagnostics signals, replacing the default in-memory store.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("Observability")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "DiagnosticsOpenTelemetryPersistenceEFCoreSqlite",
    DisplayName = "Sqlite OpenTelemetry Persistence",
    Description = "Provides Sqlite persistence for diagnostics OpenTelemetry traces, metrics, logs, and resources")]
[UsedImplicitly]
public class SqliteOpenTelemetryPersistenceShellFeature : EFCoreOpenTelemetryPersistenceFeatureBase
{
    public SqliteOpenTelemetryPersistenceShellFeature()
    {
        DbContextFactoryLifetime = ServiceLifetime.Singleton;
    }

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
