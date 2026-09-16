using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

public static class ExecutionCommandTransportEfModule
{
    public const string HistoryModuleName = "ElsaDistributedCommandTransport";
    public const string StreamHeadTableName = "elsa_distributed_command_stream_head";
    public const string TransportItemTableName = "elsa_distributed_command_transport";
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;
    public const int WorkflowExecutionIdOrderKeyWidth = DistributedRuntimeIdentityConstraints.MaximumLength * sizeof(char) + sizeof(ushort);
    public const int TransportItemIdMaximumLength = 10 + (DistributedRuntimeIdentityConstraints.MaximumLength * 3) + 1 + 19;
    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
