using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

public static class ExecutionCommandTransportEfModule
{
    public const string HistoryModuleName = "ElsaDistributedCommandTransport";
    public const string StreamHeadTableName = "elsa_distributed_command_stream_head";
    public const string TransportItemTableName = "elsa_distributed_command_transport";

    /// <summary>The persisted-schema version every stream-head and transport-item row is stamped with, and checked against when read.</summary>
    public const string SchemaVersion = "1.0.0";

    /// <summary>The schema family this module's rows belong to, checked against when a row is read.</summary>
    public const string SchemaFamily = "ExecutionCommandTransport";
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;
    public const int WorkflowExecutionIdOrderKeyWidth = DistributedRuntimeIdentityConstraints.MaximumLength * sizeof(char) + sizeof(ushort);
    public const int TransportItemIdMaximumLength = 10 + (DistributedRuntimeIdentityConstraints.MaximumLength * 3) + 1 + 19;
    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
