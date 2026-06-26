using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IWorkflowExecutableStore
{
    ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default);

    ValueTask<bool> SoftDeleteAsync(string artifactId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default);

    ValueTask<bool> RestoreAsync(string artifactId, CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default);

    ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default, bool includeDeleted = false);

    ValueTask<IReadOnlyCollection<WorkflowExecutable>> ListAsync(
        bool includeTransient = false,
        CancellationToken cancellationToken = default,
        bool includeDeleted = false);
}
