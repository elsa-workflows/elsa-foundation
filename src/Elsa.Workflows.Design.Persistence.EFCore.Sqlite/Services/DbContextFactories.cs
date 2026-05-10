using Elsa.Persistence.EFCore;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.Sqlite.Extensions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Sqlite.Services
{
    [UsedImplicitly]
    public sealed class WorkflowDefinitionDbContextFactory : SqliteDesignTimeDbContextFactory<WorkflowDefinitionDbContext>;


    public class SqliteDesignTimeDbContextFactory<TDbContext> : DesignTimeDbContextFactoryBase<TDbContext> 
        where TDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        protected override void ConfigureBuilder(DbContextOptionsBuilder<TDbContext> builder, string connectionString)
        {
            builder.UseElsaSqlite(GetType().Assembly, connectionString);
        }
    }
}
