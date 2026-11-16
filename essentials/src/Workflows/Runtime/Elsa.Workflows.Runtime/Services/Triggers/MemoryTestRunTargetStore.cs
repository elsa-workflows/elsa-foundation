using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Process-local, bounded projections for prepared test-run targets.</summary>
internal sealed class MemoryTestRunTargetStore(TimeProvider timeProvider) : ITestRunTargetStore
{
    public const int MaximumTargets = 2048;

    private readonly Dictionary<string, TestRunTarget> _targets = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public ValueTask<TestRunTarget> SaveAsync(
        TestRunTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (target.ExpiresAt <= timeProvider.GetUtcNow())
                throw new InvalidOperationException("An expired workflow test scope cannot create a prepared target.");
            if (_targets.TryGetValue(target.TestRunId, out var existing))
            {
                if (WorkflowTestScope.ContextEquals(existing.Scope, target.Scope) &&
                    StringComparer.Ordinal.Equals(existing.DriverId, target.DriverId) &&
                    StringComparer.Ordinal.Equals(existing.ModeId, target.ModeId) &&
                    StringComparer.Ordinal.Equals(existing.Binding.TriggerBindingId, target.Binding.TriggerBindingId))
                    return ValueTask.FromResult(existing);

                throw new InvalidOperationException("A workflow test run already has a different prepared target.");
            }

            if (_targets.Count >= MaximumTargets)
                throw new InvalidOperationException("The bounded workflow test target store is full.");

            _targets.Add(target.TestRunId, target);
            return ValueTask.FromResult(target);
        }
    }

    public ValueTask<TestRunTarget?> FindAsync(
        string testRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _targets.TryGetValue(testRunId, out var target);
            return ValueTask.FromResult(target);
        }
    }

    public ValueTask<TestRunTargetAdmission> BeginDispatchAsync(
        string testRunId,
        string stimulusDigest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusDigest);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_targets.TryGetValue(testRunId, out var target))
                return ValueTask.FromResult(new TestRunTargetAdmission(TestRunTargetDisposition.NotFound, null));
            if (target.ExpiresAt <= timeProvider.GetUtcNow())
                return ValueTask.FromResult(new TestRunTargetAdmission(TestRunTargetDisposition.Expired, target));
            if (target.State == TestRunTargetState.Stopped)
                return ValueTask.FromResult(new TestRunTargetAdmission(TestRunTargetDisposition.Stopped, target));
            if (target.StimulusDigest is { } priorDigest && !StringComparer.Ordinal.Equals(priorDigest, stimulusDigest))
                return ValueTask.FromResult(new TestRunTargetAdmission(TestRunTargetDisposition.PayloadConflict, target));
            if (target.DispatchResult is not null)
                return ValueTask.FromResult(new TestRunTargetAdmission(TestRunTargetDisposition.AlreadyDispatched, target));

            if (target.State == TestRunTargetState.Prepared)
            {
                target = target.WithDispatching(stimulusDigest);
                _targets[testRunId] = target;
            }

            // Dispatch uses a preallocated workflow id and idempotency key. A concurrent/retried call can safely
            // re-enter the same start request; the runtime command store admits at most one root execution.
            return ValueTask.FromResult(new TestRunTargetAdmission(TestRunTargetDisposition.Ready, target));
        }
    }

    public ValueTask<TestRunTarget?> CompleteDispatchAsync(
        string testRunId,
        TestRunTargetDispatchResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_targets.TryGetValue(testRunId, out var target))
                return ValueTask.FromResult<TestRunTarget?>(null);
            if (target.DispatchResult is null && target.State != TestRunTargetState.Stopped)
            {
                target = target.WithDispatchResult(result);
                _targets[testRunId] = target;
            }
            return ValueTask.FromResult<TestRunTarget?>(target);
        }
    }

    public ValueTask<TestRunTarget?> MarkStoppedAsync(
        string testRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_targets.TryGetValue(testRunId, out var target))
                return ValueTask.FromResult<TestRunTarget?>(null);
            if (target.State is not TestRunTargetState.Stopped and not TestRunTargetState.Dispatched)
            {
                target = target.WithStopped();
                _targets[testRunId] = target;
            }
            return ValueTask.FromResult<TestRunTarget?>(target);
        }
    }

    public ValueTask<TestRunTarget?> RemoveAsync(
        string testRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _targets.Remove(testRunId, out var target);
            return ValueTask.FromResult(target);
        }
    }

}
