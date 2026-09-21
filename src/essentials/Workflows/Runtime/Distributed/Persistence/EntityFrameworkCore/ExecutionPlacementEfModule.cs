using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Distributed.Contracts;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

public static class ExecutionPlacementEfModule
{
    public const string HistoryModuleName = "ElsaDistributedExecutionPlacement";
    public const string TableName = "elsa_distributed_execution_placement";
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;
    public const int WorkflowExecutionIdOrderKeyWidth =
        DistributedRuntimeIdentityConstraints.MaximumLength * sizeof(char) + sizeof(ushort);

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
