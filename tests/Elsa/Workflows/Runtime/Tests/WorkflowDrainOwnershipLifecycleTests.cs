using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowDrainOwnershipLifecycleTests
{
    private const string WorkflowExecutionId = "wfexec-ownership-lifecycle";

    [Fact(Timeout = 5_000)]
    public async Task LongRunningDrain_RenewsOwnershipUntilCompletion_ThenReleasesOnce()
    {
        var drainer = new ControlledSchedulerDrainer();
        var ownership = new RecordingOwnershipService(TimeSpan.FromMilliseconds(60), heartbeatTarget: 4);
        var orchestrator = NewOrchestrator(drainer, ownership);

        var drain = orchestrator.DrainAsync(NewEnvelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask();
        await ownership.HeartbeatTargetReached;
        drainer.Complete();

        var result = await drain;

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.True(ownership.HeartbeatCount >= 4);
        Assert.Equal(1, ownership.ReleaseCount);
        Assert.False(ownership.ReleaseObservedCancellation);
    }

    [Theory(Timeout = 5_000)]
    [InlineData(RuntimeExecutionOwnershipTransitionStatus.Stale)]
    [InlineData(RuntimeExecutionOwnershipTransitionStatus.Expired)]
    [InlineData(RuntimeExecutionOwnershipTransitionStatus.NotFound)]
    public async Task HeartbeatOwnershipLoss_CancelsDrain_AndSurfacesTypedFailure(
        RuntimeExecutionOwnershipTransitionStatus status)
    {
        var drainer = new ControlledSchedulerDrainer();
        var ownership = new RecordingOwnershipService(TimeSpan.FromMilliseconds(30), heartbeatResult: status);
        var orchestrator = NewOrchestrator(drainer, ownership);

        var exception = await Assert.ThrowsAsync<RuntimeExecutionOwnershipLostException>(() =>
            orchestrator.DrainAsync(NewEnvelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask());

        Assert.Equal("heartbeat", exception.Operation);
        Assert.Equal(status, exception.TransitionStatus);
        Assert.True(drainer.ObservedCancellation);
        Assert.Equal(1, ownership.ReleaseCount);
        Assert.False(ownership.ReleaseObservedCancellation);
    }

    [Fact(Timeout = 5_000)]
    public async Task CallerCancellation_ReleasesWithNonCanceledCleanupToken_AndPreservesCancellation()
    {
        var drainer = new ControlledSchedulerDrainer();
        var ownership = new RecordingOwnershipService(TimeSpan.FromMinutes(1));
        var orchestrator = NewOrchestrator(drainer, ownership);
        using var cancellation = new CancellationTokenSource();
        var drain = orchestrator.DrainAsync(
            NewEnvelope(),
            new RuntimeSchedulerDrainRequest(WorkflowExecutionId),
            cancellation.Token).AsTask();
        await drainer.Started;

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drain);
        Assert.Equal(1, ownership.ReleaseCount);
        Assert.False(ownership.ReleaseObservedCancellation);
    }

    [Fact(Timeout = 5_000)]
    public async Task FastDrain_StopsRenewal_AndReleasesExactlyOnce()
    {
        var ownership = new RecordingOwnershipService(TimeSpan.FromMinutes(1));
        var orchestrator = NewOrchestrator(new ImmediateSchedulerDrainer(), ownership);

        await orchestrator.DrainAsync(NewEnvelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(0, ownership.HeartbeatCount);
        Assert.Equal(1, ownership.ReleaseCount);
    }

    private static WorkflowDrainOrchestrator NewOrchestrator(
        IWorkflowSchedulerDrainer drainer,
        IRuntimeExecutionOwnershipService ownership) =>
        new(
            drainer,
            EmptyPostCommitOutboxProcessor.Instance,
            [],
            ownership,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            timeProvider: TimeProvider.System);

    private static WorkflowExecutionCommandEnvelope NewEnvelope()
    {
        var now = DateTimeOffset.UtcNow;
        var command = new WorkflowExecutionCommand(
            CommandId: "command-1",
            WorkflowExecutionId: WorkflowExecutionId,
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: now,
            Payload: null,
            Metadata: new Dictionary<string, string>());
        return new WorkflowExecutionCommandEnvelope(
            envelopeId: "envelope-1",
            workflowExecutionId: WorkflowExecutionId,
            command,
            idempotencyKey: "command-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: now);
    }

    private sealed class RecordingOwnershipService : IRuntimeExecutionOwnershipService
    {
        private readonly TimeSpan _leaseDuration;
        private readonly RuntimeExecutionOwnershipTransitionStatus _heartbeatResult;
        private readonly int _heartbeatTarget;
        private readonly TaskCompletionSource _heartbeatTargetReached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingOwnershipService(
            TimeSpan leaseDuration,
            RuntimeExecutionOwnershipTransitionStatus heartbeatResult = RuntimeExecutionOwnershipTransitionStatus.Applied,
            int heartbeatTarget = 1)
        {
            _leaseDuration = leaseDuration;
            _heartbeatResult = heartbeatResult;
            _heartbeatTarget = heartbeatTarget;
        }

        public int HeartbeatCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public bool ReleaseObservedCancellation { get; private set; }
        public Task HeartbeatTargetReached => _heartbeatTargetReached.Task;

        public ValueTask<RuntimeExecutionLease> AcquireAsync(
            string workflowExecutionId,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            return ValueTask.FromResult(new RuntimeExecutionLease(
                "lease-1",
                workflowExecutionId,
                "owner-1",
                now,
                now.Add(_leaseDuration),
                1));
        }

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(
            RuntimeExecutionLease lease,
            CancellationToken cancellationToken = default)
        {
            HeartbeatCount++;
            if (HeartbeatCount >= _heartbeatTarget)
                _heartbeatTargetReached.TrySetResult();
            return ValueTask.FromResult(new RuntimeExecutionOwnershipTransitionResult(_heartbeatResult, lease.FencingToken));
        }

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> ReleaseAsync(
            RuntimeExecutionLease lease,
            CancellationToken cancellationToken = default)
        {
            ReleaseCount++;
            ReleaseObservedCancellation = cancellationToken.IsCancellationRequested;
            return ValueTask.FromResult(new RuntimeExecutionOwnershipTransitionResult(
                RuntimeExecutionOwnershipTransitionStatus.Applied,
                lease.FencingToken));
        }

        public ValueTask EnsureCurrentAsync(
            string workflowExecutionId,
            long fencingToken,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class ControlledSchedulerDrainer : IWorkflowSchedulerDrainer
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public bool ObservedCancellation { get; private set; }

        public void Complete() => _completion.TrySetResult();

        public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(
            RuntimeSchedulerDrainRequest request,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            try
            {
                await _completion.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }

            return Result(request.WorkflowExecutionId);
        }
    }

    private sealed class ImmediateSchedulerDrainer : IWorkflowSchedulerDrainer
    {
        public ValueTask<RuntimeSchedulerDrainResult> DrainAsync(
            RuntimeSchedulerDrainRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Result(request.WorkflowExecutionId));
    }

    private sealed class EmptyPostCommitOutboxProcessor : IRuntimePostCommitOutboxProcessor
    {
        public static readonly EmptyPostCommitOutboxProcessor Instance = new();

        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimePostCommitOutboxProcessResult([]));
    }

    private static RuntimeSchedulerDrainResult Result(string workflowExecutionId)
    {
        var now = DateTimeOffset.UtcNow;
        return new RuntimeSchedulerDrainResult(workflowExecutionId, now, now, []);
    }
}
