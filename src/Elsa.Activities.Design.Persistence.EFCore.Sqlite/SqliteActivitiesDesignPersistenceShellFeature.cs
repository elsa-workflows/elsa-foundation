using CShells.Features;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Options;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Persistence.EFCore.Sqlite.Extensions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace Elsa.Activities.Design.Persistence.EFCore.Sqlite
{
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
        protected override void ConfigureProvider(DbContextOptionsBuilder builder, Assembly migrationsAssembly, string connectionString, ElsaDbContextOptions? options)
        {
            builder.UseElsaSqlite(migrationsAssembly, connectionString, options);
        }

        /// <inheritdoc />
        protected override void OnConfiguring(IServiceCollection services)
        {
            services.TryAddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();
            base.OnConfiguring(services);
        }
    }
}
