using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

public sealed class InMemoryWorkflowTestRunStore : IWorkflowTestRunStore
{
    private readonly Dictionary<string, WorkflowTestRun> _testRuns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkflowTestRunDraftSnapshot> _draftSnapshots = new(StringComparer.Ordinal);
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

    public ValueTask SaveDraftSnapshotAsync(WorkflowTestRunDraftSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.DefinitionVersionId);

        lock (_gate)
            _draftSnapshots[snapshot.DefinitionVersionId] = snapshot;

        return ValueTask.CompletedTask;
    }

    public ValueTask<WorkflowTestRunDraftSnapshot?> FindDraftSnapshotAsync(string definitionVersionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionVersionId);

        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (!_draftSnapshots.TryGetValue(definitionVersionId, out var snapshot))
                return ValueTask.FromResult<WorkflowTestRunDraftSnapshot?>(null);

            if (IsExpired(snapshot, now))
            {
                _draftSnapshots.Remove(definitionVersionId);
                return ValueTask.FromResult<WorkflowTestRunDraftSnapshot?>(null);
            }

            return ValueTask.FromResult<WorkflowTestRunDraftSnapshot?>(snapshot);
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

            var expiredDraftSnapshotIds = _draftSnapshots
                .Where(item => IsExpired(item.Value, now))
                .Select(item => item.Key)
                .ToArray();

            foreach (var definitionVersionId in expiredDraftSnapshotIds)
                _draftSnapshots.Remove(definitionVersionId);

            return ValueTask.FromResult(expiredTestRunIds.Length + expiredDraftSnapshotIds.Length);
        }
    }

    // A null ExpiresAt means the test run never expires.
    private static bool IsExpired(WorkflowTestRun testRun, DateTimeOffset now)
        => testRun.ExpiresAt is { } expiresAt && expiresAt <= now;

    private static bool IsExpired(WorkflowTestRunDraftSnapshot snapshot, DateTimeOffset now)
        => snapshot.ExpiresAt is { } expiresAt && expiresAt <= now;
}
