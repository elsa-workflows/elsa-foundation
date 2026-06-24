using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

public sealed class InMemoryWorkflowTestRunStore : IWorkflowTestRunStore
{
    private readonly Dictionary<string, WorkflowTestRun> _testRuns = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public ValueTask SaveAsync(WorkflowTestRun testRun, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(testRun);

        lock (_gate)
            _testRuns[testRun.TestRunId] = testRun;

        return ValueTask.CompletedTask;
    }

    public ValueTask<WorkflowTestRun?> FindAsync(string testRunId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);

        lock (_gate)
            return ValueTask.FromResult(_testRuns.GetValueOrDefault(testRunId));
    }
}
