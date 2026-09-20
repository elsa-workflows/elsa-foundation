using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872).
[assembly: EfModule(
    "Workflows.Publishing",
    typeof(PublishingSnapshotReviewDbContext),
    HistoryModule = PublishingSnapshotReviewEfModule.HistoryModuleName,
    Sqlite = typeof(PublishingSnapshotReviewSqliteDbContext),
    SqlServer = typeof(PublishingSnapshotReviewSqlServerDbContext),
    PostgreSql = typeof(PublishingSnapshotReviewPostgreSqlDbContext),
    MySql = typeof(PublishingSnapshotReviewMySqlDbContext),
    DisplayName = "Publishing")]
