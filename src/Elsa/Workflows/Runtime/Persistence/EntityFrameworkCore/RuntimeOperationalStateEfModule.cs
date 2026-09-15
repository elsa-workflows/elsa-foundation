namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Names and bounded projection sizes for the EF runtime operational-state family.</summary>
public static class RuntimeOperationalStateEfModule
{
    public const string HistoryModuleName = "ElsaRuntimeOperationalState";
    public const string DefaultConnectionName = "ElsaRuntimeOperationalState";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-runtime-operational-state.db";
    public const string DurableValueTableName = "elsa_runtime_durable_value_state";
    public const string SchedulerTableName = "elsa_runtime_scheduler_state";
    public const string DurableTimerTableName = "elsa_runtime_durable_timer";
    public const string SchedulerWorkTableName = "elsa_runtime_scheduler_work_item";
    public const string ExecutionLivenessTableName = "elsa_runtime_execution_liveness_state";
    public const string WorkflowHoldTableName = "elsa_runtime_workflow_hold_state";
    public const string IncidentTableName = "elsa_runtime_incident_state";
    public const string WorkflowRunHealthTableName = "elsa_runtime_workflow_run_health_state";
    public const string CheckpointCommitTableName = "elsa_runtime_checkpoint_commit";
    public const string RecurringScheduleTableName = "elsa_runtime_recurring_trigger_schedule";
    public const string RecurringScheduleProjectionStateTableName = "elsa_runtime_recurring_schedule_projection_state";
    public const string SchemaVersion = "1.0.0";
    public const int IdentityMaximumLength = 128;
    public const int IdentityProjectionMaximumLength = ((IdentityMaximumLength * sizeof(char) + 2) / 3) * 4;
    public const int CompositeIdentityMaximumLength = IdentityMaximumLength * 2 + 8;
    public const int ScopeProjectionMaximumLength = ((256 * sizeof(char) + 2) / 3) * 4;
    public const int OrderKeyMaximumLength = (IdentityMaximumLength + 1) * sizeof(char) * 2;
    public const int DurableTimerStimulusTypeMaximumLength = 256;
    public const int DurableTimerStimulusHashMaximumLength = 450;
    public const int DurableTimerStimulusTypeProjectionMaximumLength = ((DurableTimerStimulusTypeMaximumLength * sizeof(char) + 2) / 3) * 4;
    public const int DurableTimerStimulusHashProjectionMaximumLength = ((DurableTimerStimulusHashMaximumLength * sizeof(char) + 2) / 3) * 4;
    public const int DurableTimerClaimOrderKeyMaximumLength = 84;
    public const int SchedulerWorkOrderKeyMaximumLength = 170;
    public const int RecurringScheduleStimulusTypeMaximumLength = 240;
    public const int RecurringScheduleExpressionMaximumLength = 2048;
}
