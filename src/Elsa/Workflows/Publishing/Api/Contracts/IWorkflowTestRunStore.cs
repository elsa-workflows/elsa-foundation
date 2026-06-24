using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Contracts;

public interface IWorkflowTestRunStore
{
    ValueTask SaveAsync(WorkflowTestRun testRun, CancellationToken cancellationToken = default);

    ValueTask<WorkflowTestRun?> FindAsync(string testRunId, CancellationToken cancellationToken = default);
}
