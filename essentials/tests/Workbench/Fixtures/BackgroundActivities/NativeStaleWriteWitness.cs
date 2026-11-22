using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Elsa.Workbench.Tests.BackgroundActivities.NativeHostingWitnessRegistration;

namespace Elsa.Workbench.Tests.BackgroundActivities;

/// <summary>Test-only stale-writer gates around the real native checkpoint and ownership contracts.</summary>
internal static class NativeStaleWriteWitness
{
    private static readonly ConcurrentDictionary<string, CapturedCommit> Captured = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, CapturedCommit> CapturedByExecution = new(StringComparer.Ordinal);

    public static bool IsSelected(NativeHostingObservationSettings settings, string executionId)
    {
        if (!settings.NativeStaleWriteObservation || !StringComparer.Ordinal.Equals(settings.Role, "owner"))
            return false;

        var selectorPath = Path.Combine(settings.JournalDirectory, "stale.execution-id");
        if (!File.Exists(selectorPath))
            return false;

        var selected = File.ReadAllText(selectorPath).Trim();
        if (selected.Length == 0)
            throw new InvalidOperationException("The native stale-write execution selector is empty.");
        return StringComparer.Ordinal.Equals(selected, executionId);
    }

    public static async ValueTask CapturePublicCommitAsync(
        IServiceProvider services,
        NativeHostingObservationSettings settings,
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken)
    {
        if (!IsSelected(settings, commit.WorkflowExecutionId) || !IsTarget(commit))
            return;

        ValidateCommit(commit, decision);
        var captured = new CapturedCommit(commit, cancellationToken,
            RuntimeCheckpointCommitFingerprint.Compute(commit), commit.ExpectedFence);
        var existing = CapturedByExecution.GetOrAdd(commit.WorkflowExecutionId, captured);
        if (!ReferenceEquals(existing, captured))
        {
            if (StringComparer.Ordinal.Equals(existing.Commit.CommitId, commit.CommitId))
                throw new InvalidOperationException($"The native stale-write witness observed duplicate public commit '{commit.CommitId}'.");
            return;
        }

        var key = Key(commit.WorkflowExecutionId, commit.CommitId);
        if (!Captured.TryAdd(key, captured))
        {
            CapturedByExecution.TryRemove(commit.WorkflowExecutionId, out _);
            throw new InvalidOperationException($"The native stale-write witness observed duplicate public commit '{commit.CommitId}'.");
        }

        await WriteEventAsync(settings,
            CommitEvent("stale-commit-public", settings, commit, decision, cancellationToken, captured, null, null),
            cancellationToken);
    }

    public static async ValueTask<RuntimeCheckpointCommitStoreResult> CommitInnerAsync(
        IServiceProvider services,
        NativeHostingObservationSettings settings,
        IRuntimeCheckpointCommitStore inner,
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken)
    {
        var key = Key(commit.WorkflowExecutionId, commit.CommitId);
        if (!Captured.TryGetValue(key, out var captured))
            return await inner.CommitAsync(commit, decision, cancellationToken);

        ValidateCommit(commit, decision);
        if (!ReferenceEquals(captured.Commit, commit) ||
            !StringComparer.Ordinal.Equals(captured.Fingerprint, RuntimeCheckpointCommitFingerprint.Compute(commit)) ||
            captured.CancellationToken != cancellationToken ||
            !Equals(captured.ExpectedFence, commit.ExpectedFence) ||
            cancellationToken.IsCancellationRequested)
            throw new InvalidOperationException("The checkpoint envelope or cancellation token changed between the public coalescing boundary and the EF inner store.");

        var before = await ReadSnapshotAsync(services, commit, cancellationToken);
        await WriteEventAsync(settings, CommitEvent("stale-commit-baseline", settings, commit, decision, cancellationToken, captured, before, null), cancellationToken);
        ValidateBefore(commit, captured, before);
        await WriteEventAsync(settings, CommitEvent("stale-commit-captured", settings, commit, decision, cancellationToken, captured, before, null, null), cancellationToken);

        try
        {
            await WaitForFileAsync(settings, "stale.commit.release", cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                throw new InvalidOperationException("The original checkpoint cancellation token was cancelled before EF invocation.");

            var preDelegation = await ReadSnapshotAsync(services, commit, cancellationToken);
            ValidatePreDelegation(commit, captured, before, preDelegation, cancellationToken);
            await WriteEventAsync(settings,
                CommitEvent("stale-commit-delegating", settings, commit, decision, cancellationToken, captured, preDelegation,
                    "live-successor-revalidated"), cancellationToken);

            var result = await inner.CommitAsync(commit, decision, cancellationToken);
            var after = await ReadSnapshotAsync(services, commit, CancellationToken.None);
            await WriteEventAsync(settings, CommitEvent("stale-commit-attempted", settings, commit, decision, cancellationToken, captured, after, "returned", result), CancellationToken.None);
            return result;
        }
        catch (Exception exception)
        {
            var after = await ReadSnapshotAsync(services, commit, CancellationToken.None);
            var accepted = exception is RuntimeStaleFencingTokenException stale &&
                           stale.Reason == RuntimeFencingRejectionReason.StaleToken &&
                           stale.PresentedFencingToken == commit.ExpectedFence!.FencingToken &&
                           stale.CurrentFencingToken > stale.PresentedFencingToken;
            await WriteEventAsync(settings,
                CommitEvent("stale-commit-attempted", settings, commit, decision, cancellationToken, captured, after,
                    accepted ? "stale-token-rejected" : exception is OperationCanceledException ? "cancelled" : "threw",
                    null, exception), CancellationToken.None);
            if (accepted)
                ValidateAfterStale(before, after, commit);
            throw;
        }
    }

    public static async ValueTask RecordCommitFailureAsync(
        NativeHostingObservationSettings settings,
        RuntimeCheckpointCommit commit,
        Exception exception)
    {
        if (!Captured.ContainsKey(Key(commit.WorkflowExecutionId, commit.CommitId)))
            return;
        await WriteEventAsync(settings, new
        {
            Stage = "stale-commit-failed",
            RecordedAt = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            Role = settings.Role,
            ExecutionId = commit.WorkflowExecutionId,
            CommitId = commit.CommitId,
            ExceptionType = exception.GetType().FullName,
            Exception = exception.ToString()
        }, CancellationToken.None);
    }

    public static async ValueTask GateSuccessorAcquireAsync(
        IServiceProvider services,
        NativeHostingObservationSettings settings,
        RuntimeExecutionLease lease,
        CancellationToken cancellationToken)
    {
        if (!settings.NativeStaleWriteObservation || !StringComparer.Ordinal.Equals(settings.Role, "survivor") ||
            !IsSelectorMatch(settings, lease.WorkflowExecutionId))
            return;

        var current = await ReadOwnershipAsync(services, lease.WorkflowExecutionId, cancellationToken);
        if (current is null || current.ExecutionLease is null ||
            current.ExecutionLease.LeaseId != lease.LeaseId || current.ExecutionLease.OwnerId != lease.OwnerId ||
            current.ExecutionLease.FencingToken != lease.FencingToken || current.ExecutionLease.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("The survivor ownership acquisition was not visible as the current, unexpired durable lease.");

        await WriteEventAsync(settings, new
        {
            Stage = "stale-successor-acquired",
            RecordedAt = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            Role = settings.Role,
            ExecutionId = lease.WorkflowExecutionId,
            LeaseId = lease.LeaseId,
            OwnerId = lease.OwnerId,
            FencingToken = lease.FencingToken,
            AcquiredAt = lease.AcquiredAt,
            LeaseExpiresAt = lease.ExpiresAt,
            CancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled,
            CancellationRequested = cancellationToken.IsCancellationRequested,
            PersistedOwnerId = current.ExecutionLease.OwnerId,
            PersistedLeaseId = current.ExecutionLease.LeaseId,
            PersistedFencingToken = current.ExecutionLease.FencingToken,
            PersistedLeaseExpiresAt = current.ExecutionLease.ExpiresAt,
            PersistedLeaseUnexpired = current.ExecutionLease.ExpiresAt > DateTimeOffset.UtcNow
        }, cancellationToken);

        await WaitForFileAsync(settings, "stale.successor.release", cancellationToken);
    }

    public static async ValueTask GateOwnerHeartbeatAsync(
        IServiceProvider services,
        NativeHostingObservationSettings settings,
        RuntimeExecutionLease lease,
        CancellationToken cancellationToken)
    {
        if (!settings.NativeStaleWriteObservation || !StringComparer.Ordinal.Equals(settings.Role, "owner") ||
            !IsSelectorMatch(settings, lease.WorkflowExecutionId) ||
            !CapturedByExecution.TryGetValue(lease.WorkflowExecutionId, out var captured) ||
            captured.Commit.ExpectedFence is not { } fence || fence.LeaseId != lease.LeaseId ||
            fence.OwnerId != lease.OwnerId || fence.FencingToken != lease.FencingToken ||
            File.Exists(ControlPath(settings, "stale.heartbeat.release")))
            return;

        await WriteEventAsync(settings, new
        {
            Stage = "stale-heartbeat-blocked",
            RecordedAt = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            Role = settings.Role,
            ExecutionId = lease.WorkflowExecutionId,
            LeaseId = lease.LeaseId,
            OwnerId = lease.OwnerId,
            FencingToken = lease.FencingToken,
            CancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled,
            CancellationRequested = cancellationToken.IsCancellationRequested
        }, cancellationToken);
        try
        {
            await WaitForFileAsync(settings, "stale.heartbeat.release", cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            await WriteEventAsync(settings, new
            {
                Stage = "stale-heartbeat-cancelled",
                RecordedAt = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
                Role = settings.Role,
                ExecutionId = lease.WorkflowExecutionId,
                LeaseId = lease.LeaseId,
                OwnerId = lease.OwnerId,
                FencingToken = lease.FencingToken,
                CancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled,
                CancellationRequested = cancellationToken.IsCancellationRequested,
                Delegated = false,
                ExceptionType = exception.GetType().FullName
            }, CancellationToken.None);
            ClearCapture(lease.WorkflowExecutionId);
            throw;
        }
    }

    public static async ValueTask RecordHeartbeatOutcomeAsync(
        NativeHostingObservationSettings settings,
        RuntimeExecutionLease lease,
        CancellationToken cancellationToken,
        RuntimeExecutionOwnershipTransitionResult? result,
        Exception? exception)
    {
        if (!settings.NativeStaleWriteObservation || !IsSelectorMatch(settings, lease.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(settings.Role, "owner") ||
            !CapturedByExecution.ContainsKey(lease.WorkflowExecutionId))
            return;

        await WriteEventAsync(settings, new
        {
            Stage = "stale-heartbeat-delegated",
            RecordedAt = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            Role = settings.Role,
            ExecutionId = lease.WorkflowExecutionId,
            LeaseId = lease.LeaseId,
            OwnerId = lease.OwnerId,
            FencingToken = lease.FencingToken,
            CancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled,
            CancellationRequested = cancellationToken.IsCancellationRequested,
            ResultStatus = result?.Status.ToString(),
            ExceptionType = exception?.GetType().FullName,
            ExceptionMessage = exception?.Message
        }, CancellationToken.None);
        ClearCapture(lease.WorkflowExecutionId);
    }

    private static bool IsSelectorMatch(NativeHostingObservationSettings settings, string executionId)
    {
        var path = Path.Combine(settings.JournalDirectory, "stale.execution-id");
        return File.Exists(path) && StringComparer.Ordinal.Equals(File.ReadAllText(path).Trim(), executionId);
    }

    private static bool IsTarget(RuntimeCheckpointCommit commit) =>
        StringComparer.Ordinal.Equals(commit.Checkpoint.Name, RuntimeCheckpointNames.ActivityCompleted) &&
        commit.Checkpoint.Metadata.TryGetValue(RuntimeMetadataKeys.CheckpointReason, out var reason) &&
        StringComparer.Ordinal.Equals(reason, "BackgroundActivityResult") &&
        commit.StateChanges.BackgroundActivityWork.Acceptances.Count == 1;

    private static void ValidateCommit(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision)
    {
        var activityId = Metadata(commit, RuntimeMetadataKeys.ActivityExecutionId);
        var workItemId = Metadata(commit, RuntimeMetadataKeys.SchedulerWorkItemId);
        var acceptances = commit.StateChanges.BackgroundActivityWork.Acceptances;
        var activityChanges = commit.StateChanges.ActivityExecutions;
        if (decision.Mode != RuntimeCheckpointPersistenceMode.Immediate || commit.ExpectedFence is null ||
            commit.Checkpoint.Metadata.ContainsKey(RuntimeCoalescingMetadataKeys.CoalescedFlush) ||
            activityChanges.Count != 1 || activityChanges.Single().StateId != activityId ||
            activityChanges.Single().State.Status != ActivityExecutionStatus.Completed ||
            acceptances.Count != 1 || acceptances.Single().WorkflowExecutionId != commit.WorkflowExecutionId ||
            acceptances.Single().WorkId.Length == 0 ||
            commit.StateChanges.ConsumedSchedulerWorkItems.Count != 0 ||
            !commit.Checkpoint.ActivityExecutionIds.Contains(activityId, StringComparer.Ordinal) ||
            string.IsNullOrWhiteSpace(workItemId))
            throw new InvalidOperationException("The selected background-result checkpoint does not match the native stale-write witness contract.");
    }

    private static void ValidateBefore(RuntimeCheckpointCommit commit, CapturedCommit captured, DurableSnapshot snapshot)
    {
        var acceptance = commit.StateChanges.BackgroundActivityWork.Acceptances.Single();
        var currentLease = snapshot.CurrentOwnership?.ExecutionLease;
        if (snapshot.Marker is not null || snapshot.BackgroundWork is null ||
            snapshot.BackgroundWork.Status != BackgroundActivityWorkStatus.ResultReady ||
            !StringComparer.Ordinal.Equals(snapshot.BackgroundWork.ReceiptDigest, acceptance.ExpectedReceiptDigest) ||
            !StringComparer.Ordinal.Equals(snapshot.BackgroundWork.Request.ActivityExecutionId, Metadata(commit, RuntimeMetadataKeys.ActivityExecutionId)) ||
            !StringComparer.Ordinal.Equals(snapshot.BackgroundWork.Request.CoordinatorWorkItemId, Metadata(commit, RuntimeMetadataKeys.SchedulerWorkItemId)) ||
            snapshot.ActivityRow is null || snapshot.ActivityRow.Status != ActivityExecutionStatus.Running.ToString() ||
            snapshot.SchedulerRow is null || snapshot.SchedulerRow.ClaimOwnerId is not null || snapshot.SchedulerRow.ClaimToken != 0 ||
            snapshot.SchedulerRow.ClaimedAtUtcTicks is not null || snapshot.SchedulerRow.VisibleAfterUtcTicks is not null ||
            captured.ExpectedFence is null || currentLease is null ||
            currentLease.LeaseId != captured.ExpectedFence.LeaseId || currentLease.OwnerId != captured.ExpectedFence.OwnerId ||
            currentLease.FencingToken != captured.ExpectedFence.FencingToken || currentLease.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("The original ActivityCompleted commit did not reach an unmarked, ready-receipt, running-activity, unclaimed-scheduler baseline.");
    }

    private static void ValidatePreDelegation(
        RuntimeCheckpointCommit commit,
        CapturedCommit captured,
        DurableSnapshot before,
        DurableSnapshot current,
        CancellationToken cancellationToken)
    {
        var expectedFence = captured.ExpectedFence;
        var successor = current.CurrentOwnership?.ExecutionLease;
        if (!ReferenceEquals(captured.Commit, commit) ||
            !StringComparer.Ordinal.Equals(captured.Fingerprint, RuntimeCheckpointCommitFingerprint.Compute(commit)) ||
            captured.CancellationToken != cancellationToken || cancellationToken.IsCancellationRequested ||
            !Equals(expectedFence, commit.ExpectedFence) || expectedFence is null ||
            current.Marker is not null || current.BackgroundWork is null || before.BackgroundWork is null ||
            current.BackgroundWork.Status != before.BackgroundWork.Status ||
            current.BackgroundWork.ReceiptDigest != before.BackgroundWork.ReceiptDigest ||
            current.BackgroundWork.Revision != before.BackgroundWork.Revision ||
            current.BackgroundWork.Request.ActivityExecutionId != before.BackgroundWork.Request.ActivityExecutionId ||
            current.BackgroundWork.Request.CoordinatorWorkItemId != before.BackgroundWork.Request.CoordinatorWorkItemId ||
            current.ActivityRow != before.ActivityRow || current.SchedulerRow != before.SchedulerRow ||
            successor is null || successor.LeaseId == expectedFence.LeaseId || successor.OwnerId == expectedFence.OwnerId ||
            successor.FencingToken <= expectedFence.FencingToken || successor.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("The original checkpoint changed or no live, different-owner successor fence is current immediately before EF delegation.");
    }

    private static void ValidateAfterStale(DurableSnapshot before, DurableSnapshot after, RuntimeCheckpointCommit commit)
    {
        if (after.Marker is not null || after.BackgroundWork is null || before.BackgroundWork is null ||
            after.BackgroundWork.Status != before.BackgroundWork.Status || after.BackgroundWork.ReceiptDigest != before.BackgroundWork.ReceiptDigest ||
            after.BackgroundWork.Revision != before.BackgroundWork.Revision || after.ActivityRow != before.ActivityRow ||
            after.SchedulerRow != before.SchedulerRow || after.CurrentOwnership?.ExecutionLease is null ||
            after.CurrentOwnership.ExecutionLease.FencingToken <= commit.ExpectedFence!.FencingToken ||
            after.CurrentOwnership.ExecutionLease.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("The stale commit changed a protected durable participant or lacks a live, greater successor fence.");
    }

    private static async Task<DurableSnapshot> ReadSnapshotAsync(IServiceProvider services, RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        var access = services.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
        var scope = access.RequireScope();
        var operationScopes = services.GetRequiredService<IPersistenceOperationScopeFactory>();
        await using var operation = await operationScopes.CreateAsync(scope, cancellationToken);
        var provider = operation.ServiceProvider;
        var db = provider.GetRequiredService<RuntimeDbContext>();
        var scopeKey = EfRelationalIdentity.Encode(scope.Value);
        var executionKey = EfRelationalIdentity.Encode(commit.WorkflowExecutionId);
        var commitKey = EfRelationalIdentity.Encode(commit.CommitId);
        var activityKey = EfRelationalIdentity.Encode(Metadata(commit, RuntimeMetadataKeys.ActivityExecutionId)!);
        var marker = await db.RuntimeCheckpointCommits.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == executionKey &&
                                         row.CommitId == commitKey, cancellationToken);
        var activity = await db.ActivityExecutionStates.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == executionKey &&
                                         row.ActivityExecutionId == activityKey, cancellationToken);
        var backgroundWork = await provider.GetRequiredService<IBackgroundActivityWorkStore>()
            .FindAsync(commit.WorkflowExecutionId, commit.StateChanges.BackgroundActivityWork.Acceptances.Single().WorkId, cancellationToken);
        var workItemId = backgroundWork?.Request.CoordinatorWorkItemId;
        var workItemKey = workItemId is null ? null : EfRelationalIdentity.Encode(workItemId);
        var scheduler = workItemKey is null ? null : await db.SchedulerWorkItems.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == executionKey &&
                                         row.WorkItemId == workItemKey, cancellationToken);
        var ownership = await provider.GetRequiredService<IExecutionLivenessStateStore>().FindAsync(
            commit.WorkflowExecutionId, RuntimeExecutionOwnershipStateId.For(commit.WorkflowExecutionId), cancellationToken);
        return new DurableSnapshot(
            marker is null ? null : new MarkerSnapshot(EfRelationalIdentity.Decode(marker.CommitId), marker.Fingerprint, marker.Revision),
            backgroundWork,
            activity is null ? null : new ActivityRowSnapshot(activity.Status, activity.Revision, activity.ContentJson),
            scheduler is null ? null : new SchedulerRowSnapshot(
                EfRelationalIdentity.Decode(scheduler.WorkItemId), scheduler.ClaimOwnerId, scheduler.ClaimToken, scheduler.ClaimedAtUtcTicks,
                scheduler.VisibleAfterUtcTicks, scheduler.Revision, Hash(scheduler.ContentJson)),
            ownership);
    }

    private static async Task<ExecutionLivenessState?> ReadOwnershipAsync(IServiceProvider services, string executionId, CancellationToken cancellationToken)
    {
        var access = services.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
        var scope = access.RequireScope();
        var operationScopes = services.GetRequiredService<IPersistenceOperationScopeFactory>();
        await using var operation = await operationScopes.CreateAsync(scope, cancellationToken);
        return await operation.ServiceProvider.GetRequiredService<IExecutionLivenessStateStore>()
            .FindAsync(executionId, RuntimeExecutionOwnershipStateId.For(executionId), cancellationToken);
    }

    private static object CommitEvent(
        string stage,
        NativeHostingObservationSettings settings,
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken,
        CapturedCommit captured,
        DurableSnapshot? snapshot,
        string? outcome,
        RuntimeCheckpointCommitStoreResult? result = null,
        Exception? exception = null) => new
    {
        Stage = stage,
        RecordedAt = DateTimeOffset.UtcNow,
        ProcessId = Environment.ProcessId,
        Role = settings.Role,
        ExecutionId = commit.WorkflowExecutionId,
        CommitId = commit.CommitId,
        CheckpointId = commit.Checkpoint.CheckpointId,
        WorkItemId = Metadata(commit, RuntimeMetadataKeys.SchedulerWorkItemId),
        CheckpointName = commit.Checkpoint.Name,
        CheckpointReason = Metadata(commit, RuntimeMetadataKeys.CheckpointReason),
        ActivityExecutionId = Metadata(commit, RuntimeMetadataKeys.ActivityExecutionId),
        ExecutableNodeId = Metadata(commit, RuntimeMetadataKeys.ExecutableNodeId),
        ArtifactId = Metadata(commit, RuntimeMetadataKeys.ExecutableArtifactId),
        ArtifactVersion = Metadata(commit, RuntimeMetadataKeys.ExecutableArtifactVersion),
        ArtifactHash = Metadata(commit, RuntimeMetadataKeys.ExecutableArtifactHash),
        DecisionMode = decision.Mode.ToString(),
        ExpectedFence = Fence(commit.ExpectedFence),
        CancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled,
        CancellationRequested = cancellationToken.IsCancellationRequested,
        CancellationTokenHash = cancellationToken.GetHashCode(),
        CommitObjectId = RuntimeHelpers.GetHashCode(commit),
        captured.Fingerprint,
        ActivityParticipantIds = commit.StateChanges.ActivityExecutions.Select(change => change.StateId).ToArray(),
        ReceiptAcceptances = commit.StateChanges.BackgroundActivityWork.Acceptances.Select(Receipt).ToArray(),
        ConsumedSchedulerWorkItemIds = commit.StateChanges.ConsumedSchedulerWorkItems.Select(item => item.WorkItemId).ToArray(),
        CoalescedFlush = commit.Checkpoint.Metadata.ContainsKey(RuntimeCoalescingMetadataKeys.CoalescedFlush),
        Outcome = outcome,
        ReturnedPendingPostCommitWorkIds = result?.PendingPostCommitWorkIds,
        ReturnedConsumedSchedulerWorkItemIds = result?.ConsumedSchedulerWorkItemIds,
        ExceptionType = exception?.GetType().FullName,
        ExceptionMessage = exception?.Message,
        PresentedFencingToken = (exception as RuntimeStaleFencingTokenException)?.PresentedFencingToken,
        ExceptionCurrentFencingToken = (exception as RuntimeStaleFencingTokenException)?.CurrentFencingToken,
        FencingRejectionReason = (exception as RuntimeStaleFencingTokenException)?.Reason.ToString(),
        Marker = snapshot?.Marker,
        BackgroundStatus = snapshot?.BackgroundWork?.Status.ToString(),
        ReceiptDigest = snapshot?.BackgroundWork?.ReceiptDigest,
        BackgroundRevision = snapshot?.BackgroundWork?.Revision,
        ActivityStatus = snapshot?.ActivityRow?.Status,
        ActivityRevision = snapshot?.ActivityRow?.Revision,
        ActivityContentHash = snapshot?.ActivityRow is null ? null : Hash(snapshot?.ActivityRow.ContentJson),
        SchedulerRow = snapshot?.SchedulerRow,
        CurrentOwnerId = snapshot?.CurrentOwnership?.ExecutionLease?.OwnerId,
        CurrentLeaseId = snapshot?.CurrentOwnership?.ExecutionLease?.LeaseId,
        PersistedFencingToken = snapshot?.CurrentOwnership?.ExecutionLease?.FencingToken,
        CurrentLeaseExpiresAt = snapshot?.CurrentOwnership?.ExecutionLease?.ExpiresAt
    };

    private static async Task WriteEventAsync(NativeHostingObservationSettings settings, object value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(settings.JournalDirectory);
        var path = Path.Combine(settings.JournalDirectory, $"native-stale.{settings.Role}.{Guid.NewGuid():N}.json");
        await BackgroundWitnessJournal.WriteJsonAtomicallyAsync(path, value, cancellationToken);
    }

    private static async Task WaitForFileAsync(NativeHostingObservationSettings settings, string fileName, CancellationToken cancellationToken)
    {
        var path = ControlPath(settings, fileName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            while (!File.Exists(path))
                await Task.Delay(TimeSpan.FromMilliseconds(50), linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for native stale-write control '{fileName}'.");
        }
    }

    private static string ControlPath(NativeHostingObservationSettings settings, string fileName) => Path.Combine(settings.JournalDirectory, fileName);
    private static string Key(string executionId, string commitId) => $"{executionId}\0{commitId}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static object? Fence(RuntimeExecutionFence? fence) => fence is null ? null : new { fence.LeaseId, fence.OwnerId, fence.FencingToken };
    private static object Receipt(BackgroundActivityWorkAcceptance acceptance) => new { acceptance.WorkId, acceptance.ExpectedReceiptDigest };
    private static string? Metadata(RuntimeCheckpointCommit commit, string key) => commit.Checkpoint.Metadata.TryGetValue(key, out var value) ? value : null;

    private sealed record CapturedCommit(
        RuntimeCheckpointCommit Commit,
        CancellationToken CancellationToken,
        string Fingerprint,
        RuntimeExecutionFence? ExpectedFence);

    private static void ClearCapture(string executionId)
    {
        if (CapturedByExecution.TryRemove(executionId, out var captured))
            Captured.TryRemove(Key(executionId, captured.Commit.CommitId), out _);
    }

    private sealed record DurableSnapshot(
        MarkerSnapshot? Marker,
        BackgroundActivityWorkItem? BackgroundWork,
        ActivityRowSnapshot? ActivityRow,
        SchedulerRowSnapshot? SchedulerRow,
        ExecutionLivenessState? CurrentOwnership);

    private sealed record MarkerSnapshot(string CommitId, string Fingerprint, long Revision);
    private sealed record ActivityRowSnapshot(string Status, long Revision, string ContentJson);
    private sealed record SchedulerRowSnapshot(
        string WorkItemId,
        string? ClaimOwnerId,
        long ClaimToken,
        long? ClaimedAtUtcTicks,
        long? VisibleAfterUtcTicks,
        long Revision,
        string ContentHash);
}

internal static class NativeStaleWriteWitnessRegistration
{
    public static void Register(IServiceCollection services)
    {
        var backend = RuntimeCheckpointCommitStoreBackend.Find(services)
                      ?? throw new InvalidOperationException("Native stale-write observation requires a selected runtime checkpoint backend.");
        if (backend.Name != RuntimeCheckpointCommitStoreBackend.EntityFramework)
            throw new InvalidOperationException("Native stale-write observation requires the EF runtime checkpoint backend.");
        RequireScoped<IRuntimeCheckpointCommitStore>(services);
        var holder = RequireScoped<CoalescingInner<IRuntimeCheckpointCommitStore>>(services);

        var originalHolder = holder;
        services.Remove(holder);
        services.AddScoped<EfCheckpointWitness>(provider =>
        {
            var ownedHolder = CreateInner<CoalescingInner<IRuntimeCheckpointCommitStore>>(originalHolder, provider);
            if (ownedHolder.Value.GetType() != typeof(EfRuntimeCheckpointCommitStore))
            {
                ownedHolder.Dispose();
                throw new InvalidOperationException("Native stale-write observation expected CoalescingInner to retain the real EF checkpoint store.");
            }

            return new EfCheckpointWitness(ownedHolder, provider,
                provider.GetRequiredService<IOptions<NativeHostingObservationSettings>>().Value);
        });
        services.AddScoped<CoalescingInner<IRuntimeCheckpointCommitStore>>(provider =>
            new CoalescingInner<IRuntimeCheckpointCommitStore>(provider.GetRequiredService<EfCheckpointWitness>()));

        var outer = RequireScoped<IRuntimeCheckpointCommitStore>(services);
        services.Remove(outer);
        services.AddScoped<IRuntimeCheckpointCommitStore>(provider =>
        {
            var actualOuter = CreateInner<IRuntimeCheckpointCommitStore>(outer, provider);
            if (actualOuter.GetType() != typeof(CoalescingRuntimeCheckpointCommitStore))
            {
                if (actualOuter is IDisposable disposable)
                    disposable.Dispose();
                else if (actualOuter is IAsyncDisposable asyncDisposable)
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw new InvalidOperationException("Native stale-write observation requires the actual API CoalescingRuntimeCheckpointCommitStore.");
            }

            return new OuterCheckpointWitness(
                actualOuter,
                provider,
                provider.GetRequiredService<IOptions<NativeHostingObservationSettings>>().Value,
                ownsInner: outer.ImplementationInstance is null);
        });
    }


}

internal sealed class OuterCheckpointWitness(
    IRuntimeCheckpointCommitStore inner,
    IServiceProvider services,
    NativeHostingObservationSettings settings,
    bool ownsInner)
    : NativeHostingOwnedService<IRuntimeCheckpointCommitStore>(inner, services, settings, ownsInner), IRuntimeCheckpointCommitStore
{
    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default)
    {
        await NativeStaleWriteWitness.CapturePublicCommitAsync(Services, Settings, commit, decision, cancellationToken);
        try
        {
            return await Inner.CommitAsync(commit, decision, cancellationToken);
        }
        catch (Exception exception)
        {
            await NativeStaleWriteWitness.RecordCommitFailureAsync(Settings, commit, exception);
            throw;
        }
    }
}

internal sealed class EfCheckpointWitness(
    CoalescingInner<IRuntimeCheckpointCommitStore> ownedHolder,
    IServiceProvider services,
    NativeHostingObservationSettings settings) : IRuntimeCheckpointCommitStore, IDisposable, IAsyncDisposable
{
    private int _disposed;

    public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default) =>
        NativeStaleWriteWitness.CommitInnerAsync(services, settings, ownedHolder.Value, commit, decision, cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            ownedHolder.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            await ownedHolder.DisposeAsync();
    }
}
