using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Names and provider-neutral limits for R10 workflow execution persistence.</summary>
public static class RuntimeWorkflowExecutionEfModule
{
    public const string HistoryModuleName = "ElsaRuntimeWorkflowExecutions";
    public const string DefaultConnectionName = "ElsaRuntimeWorkflowExecutions";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-runtime-workflow-executions.db";
    public const string TableName = "elsa_runtime_workflow_execution_state";
    public const string SchemaVersion = "1.0.0";
    public const int IdentityMaximumLength = 128;
    public const int IdentityProjectionMaximumLength = 450;
    public const int OrderKeyMaximumLength = 655;
    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
