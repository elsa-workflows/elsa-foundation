using Elsa.Persistence.EntityFramework;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal static class JobsEfModule
{
    internal const string Name = "JobsRuntime";
    internal const string Version = "1.0.0";
    internal static string HistoryTableName => EfMigrationsHistory.TableName(Name);
}
