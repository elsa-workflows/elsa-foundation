using CShells.Features;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Options;
using Elsa.Workflows.Design.Persistence.EFCore.Sqlite.Extensions;
using Elsa.Workflows.Design.Persistence.EFCore.Sqlite.Services;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Workflows.Design.Persistence.EFCore.Sqlite
{
    /// <summary>
    /// Configures the Sqlite persistence for workflow definitions.
    /// </summary>
    [ShellFeature(
        name: "WorkflowDefinitionsPersistenceEFCoreSqlite",
        DisplayName = "Sqlite Workflow Definition Persistence",
        Description = "Provides Sqlite persistence for workflow definitions")]
    [UsedImplicitly]
    public class SqliteWorkflowDefinitionPersistenceShellFeature : EFCoreWorkflowsPersistenceFeatureBase
    {
        /// <inheritdoc />
        protected override void ConfigureProvider(DbContextOptionsBuilder builder, Assembly migrationsAssembly, string connectionString, ElsaDbContextOptions? options)
        {
            builder.UseElsaSqlite(migrationsAssembly, connectionString, options);
        }

        /// <inheritdoc />
        protected override void OnConfiguring(IServiceCollection services)
        {
            services.AddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();
            base.OnConfiguring(services);
        }
    }
}
