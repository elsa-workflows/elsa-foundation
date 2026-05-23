using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using JetBrains.Annotations;

namespace Elsa.Workflows.Design.Persistence.EFCore.Sqlite
{
    [UsedImplicitly]
    public sealed class WorkflowsDesignDbContextFactory : SqliteDesignTimeDbContextFactory<WorkflowsDesignDbContext>;
}
