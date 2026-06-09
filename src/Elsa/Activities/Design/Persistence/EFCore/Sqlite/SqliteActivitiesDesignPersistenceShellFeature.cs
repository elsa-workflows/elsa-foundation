using CShells.Features;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Options;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Persistence.EFCore.Sqlite.Constants;
using Elsa.Persistence.EFCore.Sqlite.Extensions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace Elsa.Activities.Design.Persistence.EFCore.Sqlite;

/// <summary>
/// Configures the Sqlite persistence for workflow definitions.
/// </summary>
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
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            ConnectionString = SqliteConstants.DefaultConnectionString;
        }

        services.TryAddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();
    }

    protected override void ConfigureProvider(DbContextOptionsBuilder builder, Assembly migrationsAssembly, string connectionString, ElsaDbContextOptions? options)
    {
        builder.UseElsaSqlite(migrationsAssembly, connectionString, options);
    }
}