using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872).
[assembly: EfModule(
    "Activities.Design",
    typeof(ActivitiesDesignDbContext),
    HistoryModule = ActivitiesDesignEfModule.HistoryModuleName,
    Sqlite = typeof(ActivitiesDesignSqliteDbContext),
    SqlServer = typeof(ActivitiesDesignSqlServerDbContext),
    PostgreSql = typeof(ActivitiesDesignPostgreSqlDbContext),
    MySql = typeof(ActivitiesDesignMySqlDbContext),
    DisplayName = "Activities Design")]
