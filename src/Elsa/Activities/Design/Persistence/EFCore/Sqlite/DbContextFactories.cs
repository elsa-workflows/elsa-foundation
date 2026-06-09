using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.EFCore.Sqlite;
using JetBrains.Annotations;

namespace Elsa.Activities.Design.Persistence.EFCore.Sqlite
{
    [UsedImplicitly]
    public sealed class ActivitiesDesignDbContextFactory : SqliteDesignTimeDbContextFactory<ActivitiesDesignDbContext>;
}
