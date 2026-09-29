using Elsa.Cluster.Fixtures.FeedModule;
using Elsa.Persistence.EntityFramework;

[assembly: EfModule(
    FeedModule.Name,
    typeof(FeedModuleDbContext),
    HistoryModule = FeedModule.HistoryModuleName,
    Sqlite = typeof(FeedModuleSqliteDbContext),
    DisplayName = "Feed module fixture")]

// The release before the fixture's: its family is at version 1 and reads nothing newer.
[assembly: EfSchemaFamily(FeedModule.Family, FeedModule.Name, FeedModule.PreviousVersion)]
