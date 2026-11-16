using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Schedules generated events from in-workflow generator activities as runtime scheduler work.
/// </summary>
public interface IRuntimeGeneratorEmissionScheduler
{
    /// <summary>
    /// Enqueues the generated event as deterministic scheduler work for its workflow execution.
    /// </summary>
    ValueTask<RuntimeGeneratorEmissionScheduleResult> ScheduleAsync(
        RuntimeGeneratorEmissionScheduleRequest request,
        CancellationToken cancellationToken = default);
}
