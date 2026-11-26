using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Collects terminal dispatch records only after both linked executions are no longer retained.</summary>
public interface IWorkflowDispatchRetentionCollector
{
    ValueTask<WorkflowDispatchRetentionSweepResult> SweepAsync(CancellationToken cancellationToken = default);
}

public sealed record WorkflowDispatchRetentionSweepResult(
    int ExaminedCount,
    int DeletedCount,
    int RetainedCount,
    int UncertainCount);
