namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Names and bounded projection sizes for the R14-R18 runtime operational state family.</summary>
public static class RuntimeOperationalStateEfModule
{
    public const string HistoryModuleName = "ElsaRuntimeOperationalState";
    public const string DefaultConnectionName = "ElsaRuntimeOperationalState";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-runtime-operational-state.db";
    public const string DurableValueTableName = "elsa_runtime_durable_value_state";
    public const string SchedulerTableName = "elsa_runtime_scheduler_state";
    public const string ExecutionLivenessTableName = "elsa_runtime_execution_liveness_state";
    public const string WorkflowHoldTableName = "elsa_runtime_workflow_hold_state";
    public const string IncidentTableName = "elsa_runtime_incident_state";
    public const string SchemaVersion = "1.0.0";
    public const int IdentityMaximumLength = 128;
    public const int IdentityProjectionMaximumLength = ((IdentityMaximumLength * sizeof(char) + 2) / 3) * 4;
    public const int CompositeIdentityMaximumLength = IdentityMaximumLength * 2 + 8;
    public const int ScopeProjectionMaximumLength = ((256 * sizeof(char) + 2) / 3) * 4;
    public const int OrderKeyMaximumLength = (IdentityMaximumLength + 1) * sizeof(char) * 2;
}
