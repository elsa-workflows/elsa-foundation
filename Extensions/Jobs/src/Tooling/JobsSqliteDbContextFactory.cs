using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Jobs.Persistence.EntityFrameworkCore;

namespace Elsa.Jobs.Tooling;

internal sealed class JobsSqliteDbContextFactory() : ModuleDesignTimeFactory<JobsSqliteDbContext>(JobsEfModule.HistoryTableName);
