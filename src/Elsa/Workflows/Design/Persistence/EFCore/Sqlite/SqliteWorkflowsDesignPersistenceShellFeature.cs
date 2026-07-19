using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Persistence.EFCore.Options;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Workflows.Dashboard.Providers.EFCore;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Workflows.Design.Persistence.EFCore.Sqlite;

/// <summary>
/// Configures the Sqlite persistence for workflow definitions.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsDesignPersistenceEFCoreSqlite",
    DisplayName = "Sqlite Workflows Design Persistence",
    Description = "Provides Sqlite persistence for the workflows design domain")]
[UsedImplicitly]
public class SqliteWorkflowsDesignPersistenceShellFeature : EFCoreWorkflowsPersistenceFeatureBase
{
    /// <inheritdoc />
    protected override void OnBeforeConfiguring(IServiceCollection services)
    {
        ConnectionString = SqliteShellFeatureDefaults.ApplyDefaults(services, ConnectionString);
        services.AddEFCoreWorkflowPortfolio();
    }

    protected override void ConfigureProvider(DbContextOptionsBuilder builder, Assembly migrationsAssembly, string connectionString, ElsaDbContextOptions? options)
    {
        SqliteShellFeatureDefaults.ConfigureProvider(builder, migrationsAssembly, connectionString, options);
    }
}
