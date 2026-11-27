using System.Collections.Frozen;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Process-local, bounded projections for prepared test-run targets.</summary>
internal sealed class MemoryTestRunTargetStore(TimeProvider timeProvider) : ITestRunTargetStore
{
    public const int MaximumTargets = 2048;

    private const int MaximumReplyBytes = 4 * 1024 * 1024;
    private readonly Dictionary<string, TestRunTarget> _targets = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly int _replyLimit = MaximumReplyBytes;
    private long _replyBytes;

    internal MemoryTestRunTargetStore(TimeProvider timeProvider, int replyLimit) : this(timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(replyLimit);
        _replyLimit = replyLimit;
    }

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

            if (target.DispatchResult is { Dispatch.TestRunHttpReply: not null } result)
                target = target.WithDispatchResult(RetainReply(target, result, timeProvider.GetUtcNow()));
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
            if (target is not null && target.ExpiresAt <= timeProvider.GetUtcNow()) target = ReleaseReply(target);
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
                return ValueTask.FromResult(new TestRunTargetAdmission(TestRunTargetDisposition.Expired, ReleaseReply(target)));
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
                target = target.WithDispatchResult(RetainReply(target, result, timeProvider.GetUtcNow()));
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
            target = ReleaseReply(target);
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
            if (target is not null && HeldReply(target) is { } reply) _replyBytes -= SizeOf(reply);
            return ValueTask.FromResult(target);
        }
    }

    private TestRunTargetDispatchResult RetainReply(TestRunTarget target, TestRunTargetDispatchResult result, DateTimeOffset now)
    {
        if (target.SourceReferenceId != target.Scope.ScopeId || result.Dispatch.TestRunHttpReply is not { } reply) return result;
        var snapshot = reply with { Headers = reply.Headers.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase) };
        var size = SizeOf(snapshot);
        if (target.ExpiresAt <= now || size > _replyLimit) return WithReply(result, null);
        if (_replyBytes + size > _replyLimit)
        {
            foreach (var expired in _targets.Values.Where(item => item.ExpiresAt <= now && HeldReply(item) is not null).ToArray())
                ReleaseReply(expired);
            if (_replyBytes + size > _replyLimit) return WithReply(result, null);
        }
        _replyBytes += size;
        return WithReply(result, snapshot);
    }

    private TestRunTarget ReleaseReply(TestRunTarget target)
    {
        if (HeldReply(target) is not { } reply) return target;
        _replyBytes -= SizeOf(reply);
        var metadataOnly = target.WithDispatchResult(WithReply(target.DispatchResult!, null));
        _targets[target.TestRunId] = metadataOnly;
        return metadataOnly;
    }

    private static WorkflowTestRunHttpReply? HeldReply(TestRunTarget target) =>
        target.SourceReferenceId == target.Scope.ScopeId ? target.DispatchResult?.Dispatch.TestRunHttpReply : null;

    private static long SizeOf(WorkflowTestRunHttpReply reply) => sizeof(char) *
        ((long)(reply.Body?.Length ?? 0) + reply.ContentType.Length + reply.Headers.Sum(header => (long)header.Key.Length + header.Value.Length));

    private static TestRunTargetDispatchResult WithReply(TestRunTargetDispatchResult result, WorkflowTestRunHttpReply? reply)
    {
        var dispatch = result.Dispatch;
        var projection = new WorkflowExecutionStartDispatchResult(dispatch.WorkflowExecutionId, dispatch.PinnedExecutable,
            dispatch.CommandDispatch, dispatch.Agent, dispatch.PinnedSource)
            { TestRunHttpReply = reply, TestRunCompletedWithoutRecord = dispatch.TestRunCompletedWithoutRecord };
        return result with { Dispatch = projection };
    }
}
