using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores split continuation state for the single-writer runtime scheduler snapshot.
/// </summary>
public interface ISchedulerStateStore
{
    /// <summary>
    /// Inserts or replaces scheduler state for the workflow execution.
    /// </summary>
    ValueTask<SchedulerState> SaveAsync(SchedulerState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns scheduler state for the given workflow execution ID, or <see langword="null"/> if not found.
    /// </summary>
    ValueTask<SchedulerState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all scheduler states.
    /// </summary>
    ValueTask<IReadOnlyCollection<SchedulerState>> ListAsync(CancellationToken cancellationToken = default);
}
