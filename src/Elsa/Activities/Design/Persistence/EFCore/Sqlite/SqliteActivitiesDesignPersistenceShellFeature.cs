using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Persistence.EFCore.Options;
using Elsa.Persistence.EFCore.Sqlite;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Activities.Design.Persistence.EFCore.Sqlite;

/// <summary>
/// Configures the Sqlite persistence for workflow definitions.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "ActivitiesDesignPersistenceEFCoreSqlite",
    DisplayName = "Sqlite Activities Design Persistence",
    Description = "Provides Sqlite persistence for the activities design domain")]
[UsedImplicitly]
public class SqliteActivitiesDesignPersistenceShellFeature : EFCoreActivitiesPersistenceFeatureBase
{
    /// <inheritdoc />
    protected override void OnAfterConfigured(IServiceCollection services)
    {
        ConnectionString = SqliteShellFeatureDefaults.ApplyDefaults(services, ConnectionString);
    }

    protected override void ConfigureProvider(DbContextOptionsBuilder builder, Assembly migrationsAssembly, string connectionString, ElsaDbContextOptions? options)
    {
        SqliteShellFeatureDefaults.ConfigureProvider(builder, migrationsAssembly, connectionString, options);
    }
}