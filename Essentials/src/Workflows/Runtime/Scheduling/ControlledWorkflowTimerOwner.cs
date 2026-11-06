using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Claims;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Drives scope-owned due timers through ordinary typed bookmark resumes. Providers retain pending scans and
/// not-found delivery responsibility; scenario age never retires a timer whose bookmark may still be committing.
/// This service is not advertised or registered without a qualified policy source and atomic time/scope provider.
/// </summary>
internal sealed class ControlledWorkflowTimerOwner(
    WorkflowTestTimePolicyResolver policies,
    IControlledWorkflowTimeStore timeStore,
    IWorkflowExecutionStateStore workflows,
    IActivityExecutionStateStore activities,
    IWorkflowTestScopeStore scopes,
    IBookmarkStimulusLookup lookup,
    IBookmarkResumeDispatcher dispatcher,
    TimeProvider wall)
{
    private static readonly TimeSpan Visibility = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly string _ownerId = $"controlled-timer:{Guid.NewGuid():N}";

    internal async ValueTask<ControlledWorkflowClockState> InitializeAsync(WorkflowTestEntry entry,
        CancellationToken cancellationToken = default)
    {
        var policy = await RequireControlledAsync(entry, cancellationToken);
        return await timeStore.CreateClockAsync(policy, wall.GetUtcNow(), cancellationToken);
    }

    internal async ValueTask<ControlledWorkflowClockState> AdvanceAsync(WorkflowTestEntry entry,
        string operationId, long expectedRevision, DateTimeOffset advanceTo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var policy = await RequireControlledAsync(entry, cancellationToken);
        return await timeStore.AdvanceAsync(policy, operationId, expectedRevision, advanceTo,
            wall.GetUtcNow(), cancellationToken);
    }

    internal async ValueTask<ControlledWorkflowTimerDrainResult> SweepAsync(int limit = 100,
        CancellationToken cancellationToken = default)
    {
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        var claimed = 0;
        var dispatched = 0;
        var pending = 0;
        var retired = 0;
        var remaining = limit;
        foreach (var policy in await timeStore.ListPendingAsync(wall.GetUtcNow(), limit, cancellationToken))
        {
            var result = await DrainAsync(policy, remaining, cancellationToken);
            claimed += result.Claimed;
            dispatched += result.Dispatched;
            pending += result.Pending;
            retired += result.Retired;
            remaining -= Math.Max(result.Claimed, result.Retired);
            if (remaining <= 0) break;
        }
        return new(claimed, dispatched, pending, retired);
    }

    internal async ValueTask<ControlledWorkflowTimerDrainResult> DrainAsync(WorkflowTestTimePolicy policy,
        int limit = 100, CancellationToken cancellationToken = default)
    {
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        var scope = await scopes.FindAsync(policy.Entry.Scope.ScopeId, cancellationToken);
        if (scope is not null && WorkflowTestScope.ContextEquals(scope.Scope, policy.Entry.Scope) &&
            (scope.State != WorkflowTestScopeState.Open || scope.Scope.IsExpired(wall.GetUtcNow())))
        {
            var cleanup = await timeStore.CloseAsync(policy, wall.GetUtcNow(), limit, cancellationToken);
            return new(0, 0, cleanup.Remaining, cleanup.Removed);
        }
        var admitted = await RequireControlledAsync(policy.Entry, cancellationToken);
        if (!policy.Matches(admitted))
            throw WorkflowTestTimePolicyResolver.Refuse("test-time-policy-changed", "The stored clock differs from its immutable admitted policy.");
        var claims = await timeStore.ClaimDueAsync(policy, _ownerId, wall.GetUtcNow(), Visibility, limit, cancellationToken);
        var dispatched = 0;
        var pending = 0;
        var retired = 0;
        foreach (var claim in claims)
        {
            var outcome = await FireAsync(claim, cancellationToken);
            if (outcome == TimerOutcome.Dispatched) dispatched++;
            else if (outcome == TimerOutcome.Retired) retired++;
            else pending++;
        }
        return new(claims.Count, dispatched, pending, retired);
    }

    private async ValueTask<WorkflowTestTimePolicy> RequireControlledAsync(WorkflowTestEntry entry, CancellationToken cancellationToken)
    {
        var policy = await policies.ResolveEntryAsync(entry, cancellationToken);
        if (policy.Mode != WorkflowTestTimeMode.Controlled)
            throw WorkflowTestTimePolicyResolver.Refuse("test-clock-not-controlled", "The admitted policy uses ordinary wall time.");
        return policy;
    }

    private async ValueTask<TimerOutcome> FireAsync(ControlledWorkflowTimerClaim original, CancellationToken cancellationToken)
    {
        var claim = original;
        try
        {
            var timer = claim.Claim.Timer;
            var workflow = await workflows.FindAsync(timer.WorkflowExecutionId, cancellationToken);
            if (workflow is null)
                return await ReleaseAsync(claim, cancellationToken);
            if (!claim.Policy.Entry.Matches(workflow.TestEntry) || workflow.RunKind != WorkflowRunKind.TestRun ||
                !WorkflowTestScope.ContextEquals(workflow.TestScope, claim.Policy.Entry.Scope))
                throw WorkflowTestTimePolicyResolver.Refuse("test-timer-context-mismatch", "The controlled timer no longer matches its admitted runtime context.");
            if (workflow.Status.IsTerminal())
                return await RetireAsync(claim, cancellationToken);
            if (claim.Delivery is { } recorded)
            {
                var existing = recorded.MatchesExisting(workflow);
                if (existing == true) return await AcknowledgeAsync(claim, cancellationToken);
                if (existing == false)
                    throw WorkflowTestTimePolicyResolver.Refuse("test-timer-receipt-conflict", "The controlled timer delivery receipt conflicts.");
            }
            var activity = timer.ActivityExecutionId is { } invocation
                ? await activities.FindAsync(timer.WorkflowExecutionId, invocation, cancellationToken) : null;
            if (activity is null) return await ReleaseAsync(claim, cancellationToken);
            if (activity.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Cancelled or
                ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Superseded)
                return await RetireAsync(claim, cancellationToken);
            var matched = await lookup.FindAsync(new(timer.WorkflowExecutionId, timer.StimulusType,
                timer.StimulusHash, wall.GetUtcNow(), claim.Delivery?.WaitId), cancellationToken);
            if (matched.Status != BookmarkStimulusLookupStatus.Found)
                return await ReleaseAsync(claim, cancellationToken);
            var bookmark = matched.Bookmark!;
            if (bookmark.ActivityExecutionId != timer.ActivityExecutionId)
                throw WorkflowTestTimePolicyResolver.Refuse("test-timer-wait-mismatch", "The controlled timer matched another activity invocation.");
            if (claim.Delivery is null)
            {
                var binding = await timeStore.BindDeliveryAsync(claim, Delivery(claim, bookmark), cancellationToken);
                if (binding.Status != RuntimeDurableTimerClaimTransitionStatus.Succeeded || binding.Claim is null)
                    return TimerOutcome.Pending;
                claim = binding.Claim;
            }
            var receipt = claim.Delivery!;
            var lease = new FencedClaimLease<ControlledWorkflowTimerClaim>(async (current, at, visibility, ct) =>
            {
                var renewed = await timeStore.RenewClaimAsync(current, at, visibility, ct);
                return renewed.Status == RuntimeDurableTimerClaimTransitionStatus.Succeeded ? renewed.Claim : null;
            }, Visibility, wall);
            // Renewal precedes enqueue. The normal persisted test receipt deduplicates a competing handoff;
            // the store must not share a provider unit of work with an inline runtime drain.
            var run = await lease.RunAsync(claim, ct => dispatcher.DispatchAsync(new(
                timer.WorkflowExecutionId, timer.StimulusType, timer.StimulusHash, timer.Input,
                receipt.DeliveryId, "runtime.controlled-timer", null, timer.PayloadType, timer.ProviderId,
                bookmark.BookmarkId, receipt), cancellationToken: ct), cancellationToken);
            claim = run.Claim;
            if (run.Status == FencedClaimRunStatus.LostBeforeSideEffect) return TimerOutcome.Pending;
            if (run.Status == FencedClaimRunStatus.Completed && run.Result?.Status is
                BookmarkResumeDispatchStatus.Dispatched or BookmarkResumeDispatchStatus.Duplicate)
                return await AcknowledgeAsync(claim, cancellationToken);
            return await ReleaseAsync(claim, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Release retains the exact pending responsibility/receipt. No failure may turn into real time,
            // a duplicate fresh delivery, or a scenario-age orphan deletion.
            await timeStore.ReleaseClaimAsync(claim, wall.GetUtcNow().Add(RetryDelay), cancellationToken);
            throw;
        }
    }

    private async ValueTask<TimerOutcome> ReleaseAsync(ControlledWorkflowTimerClaim claim, CancellationToken cancellationToken)
    {
        await timeStore.ReleaseClaimAsync(claim, wall.GetUtcNow().Add(RetryDelay), cancellationToken);
        return TimerOutcome.Pending;
    }

    private async ValueTask<TimerOutcome> AcknowledgeAsync(ControlledWorkflowTimerClaim claim, CancellationToken cancellationToken) =>
        (await timeStore.CompleteClaimAsync(claim, cancellationToken)).Succeeded ? TimerOutcome.Dispatched : TimerOutcome.Pending;

    private async ValueTask<TimerOutcome> RetireAsync(ControlledWorkflowTimerClaim claim, CancellationToken cancellationToken) =>
        (await timeStore.CompleteClaimAsync(claim, cancellationToken)).Succeeded ? TimerOutcome.Retired : TimerOutcome.Pending;

    private static TestRunDeliveryReceipt Delivery(ControlledWorkflowTimerClaim claim, BookmarkState bookmark)
    {
        var digest = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            claim.Policy.Entry.ContextId, claim.Policy.Entry.ContextFingerprint, claim.Policy.ClockId,
            claim.Claim.Timer.WorkflowExecutionId, claim.Claim.Timer.TimerId, bookmark.BookmarkId,
            claim.Claim.Timer.DueTime, claim.Claim.Timer.BusinessTime
        }));
        return new(claim.Policy.Entry.Scope.ScopeId, new Guid(digest.AsSpan(0, 16)).ToString("D"),
            bookmark.BookmarkId, Convert.ToHexStringLower(digest), TestRunDeliveryReceipt.Digest(claim.Claim.Timer.Input));
    }

    private enum TimerOutcome { Pending, Dispatched, Retired }
}
