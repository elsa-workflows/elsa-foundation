using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

public interface IWorkflowTestRunStore
{
    ValueTask SaveAsync(WorkflowTestRun testRun, CancellationToken cancellationToken = default);

    ValueTask<WorkflowTestRun?> FindAsync(string testRunId, CancellationToken cancellationToken = default);

    ValueTask SaveDraftSnapshotAsync(WorkflowTestRunDraftSnapshot snapshot, CancellationToken cancellationToken = default);

    ValueTask<WorkflowTestRunDraftSnapshot?> FindDraftSnapshotAsync(string definitionVersionId, CancellationToken cancellationToken = default);

    ValueTask<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
