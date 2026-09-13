using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

public static class ExecutionPlacementEfModule
{
    public const string HistoryModuleName = "ElsaDistributedExecutionPlacement";
    public const string TableName = "elsa_distributed_execution_placement";
    public const string DefaultConnectionName = "ElsaDistributedExecutionPlacement";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-distributed-execution-placement.db";

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
