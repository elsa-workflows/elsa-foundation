using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

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

        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (!_testRuns.TryGetValue(testRunId, out var testRun))
                return ValueTask.FromResult<WorkflowTestRun?>(null);

            // Lazy expiry: a test run past its ExpiresAt is dropped on read so a stale record is never
            // observed even if the periodic sweep has not run yet.
            if (IsExpired(testRun, now))
            {
                _testRuns.Remove(testRunId);
                return ValueTask.FromResult<WorkflowTestRun?>(null);
            }

            return ValueTask.FromResult<WorkflowTestRun?>(testRun);
        }
    }

    public ValueTask<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var expiredTestRunIds = _testRuns
                .Where(item => IsExpired(item.Value, now))
                .Select(item => item.Key)
                .ToArray();

            foreach (var testRunId in expiredTestRunIds)
                _testRuns.Remove(testRunId);

            return ValueTask.FromResult(expiredTestRunIds.Length);
        }
    }

    // A null ExpiresAt means the test run never expires.
    private static bool IsExpired(WorkflowTestRun testRun, DateTimeOffset now)
        => testRun.ExpiresAt is { } expiresAt && expiresAt <= now;
}
