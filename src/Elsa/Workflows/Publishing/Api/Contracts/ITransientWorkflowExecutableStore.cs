using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Contracts;

public interface ITransientWorkflowExecutableStore
{
    ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default);

    ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default);

    ValueTask<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
