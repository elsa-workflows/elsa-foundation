using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Distributed.Contracts;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

public static class ExecutionPlacementEfModule
{
    public const string HistoryModuleName = "ElsaDistributedExecutionPlacement";
    public const string TableName = "elsa_distributed_execution_placement";

    /// <summary>The persisted-schema version every placement row is stamped with, and checked against when read.</summary>
    public const string SchemaVersion = "1.0.0";

    /// <summary>The schema family this module's rows belong to, checked against when a row is read.</summary>
    public const string SchemaFamily = "ExecutionPlacement";
    /// <summary>The family's one chain, from this assembly's declaration: what every reader of the family checks and
    /// upcasts through (spec 180, FR-010).</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(ExecutionPlacementEfModule).Assembly, SchemaFamily);
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;
    public const int WorkflowExecutionIdOrderKeyWidth =
        DistributedRuntimeIdentityConstraints.MaximumLength * sizeof(char) + sizeof(ushort);

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
