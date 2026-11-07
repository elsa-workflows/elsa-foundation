using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Jobs.Persistence.EntityFrameworkCore;

namespace Elsa.Jobs.Tooling;

internal sealed class JobsPostgreSqlDbContextFactory() : ModuleDesignTimeFactory<JobsPostgreSqlDbContext>(JobsEfModule.HistoryTableName);
