using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// not the hand-written EfModuleBinding each registration class still builds for itself (slice 2, #1872).
[assembly: EfModule(
    "Workflows.Design",
    typeof(WorkflowsDesignDbContext),
    HistoryModule = WorkflowsDesignEfModule.HistoryModuleName,
    Sqlite = typeof(WorkflowsDesignSqliteDbContext),
    SqlServer = typeof(WorkflowsDesignSqlServerDbContext),
    PostgreSql = typeof(WorkflowsDesignPostgreSqlDbContext),
    MySql = typeof(WorkflowsDesignMySqlDbContext))]
