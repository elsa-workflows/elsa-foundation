using System.Reflection;
using CShells.Features;
using Elsa.Persistence.EFCore.Options;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Platform.PackageManifest.Generator.Hints;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

        ConnectionString = SqliteShellFeatureDefaults.ApplyDefaults(services, ConnectionString);
    }

    protected override void ConfigureProvider(DbContextOptionsBuilder builder, Assembly migrationsAssembly, string connectionString, ElsaDbContextOptions? options)
    {
        SqliteShellFeatureDefaults.ConfigureProvider(builder, migrationsAssembly, connectionString, options);
    }
}
