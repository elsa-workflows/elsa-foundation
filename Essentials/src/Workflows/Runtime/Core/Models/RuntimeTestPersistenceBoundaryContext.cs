using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>The exact physical provider tuple selected for one admitted test entry.</summary>
public sealed record RuntimeTestPersistenceBoundaryContext(
    WorkflowTestEntry Entry,
    IWorkflowExecutionStateStore Executions,
    IWorkflowSchedulerWorkQueue Scheduler,
    IRuntimeCheckpointCommitStore Checkpoints,
    IRuntimePostCommitOutboxStore Outbox,
    IBackgroundActivityWorkStore Background,
    IDurableTimerStore Timers);
