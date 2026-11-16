using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Evaluates whether queued scheduler work may advance through a named runtime pause boundary.
/// </summary>
public interface IWorkflowSchedulerPauseGate
{
    /// <summary>
    /// Returns a pause decision for pause-gated scheduler work, or <see langword="null"/> for work with no boundary in this slice.
    /// </summary>
    ValueTask<SchedulerPauseDecision?> EvaluateAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default);
}
