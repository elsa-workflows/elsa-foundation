using System.Collections.Concurrent;
using Elsa.Tasks.Schedules;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Elsa.Workflows.Runtime.Services.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Recurring background pump that fires due <see cref="DurableTimer"/>s. Each tick runs one bounded sweep:
/// it loads at most <see cref="DurableTimerPumpOptions.MaxTimersPerTick"/> due timers and dispatches each
/// as a bookmark resume through the single-writer <see cref="IBookmarkResumeDispatcher"/> (the same mailbox
/// path every other resume uses). Whole-sweep failure backoff comes from <see cref="BackoffSweepPumpTask"/>;
/// on top of it the pump parks individually failing timers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per-timer backoff.</b> A timer whose dispatch faults or is rejected/deferred is parked for a
/// geometrically growing window and skipped on subsequent sweeps until eligible again, so a single poisoned
/// timer cannot occupy a dispatch slot on every tick.
/// </para>
/// <para>
/// <b>Idempotency.</b> A dispatched timer is deleted on <c>Dispatched</c>/<c>Duplicate</c> — the resume is
/// durably enqueued into the scheduler work queue before the dispatcher returns, so deletion cannot lose
/// the resume. <c>NotFound</c> past the grace window (bookmark already consumed by an earlier fire, or the
/// workflow is gone) also deletes the timer; within the grace window it is retried to cover a very short
/// delay racing its own bookmark commit. The bookmark is consumed at most once, so an at-least-once
/// duplicate fire cannot double-resume.
/// </para>
/// <para>
/// <b>Claims (#2195).</b> A claim-capable store hands the pump a batch of claims under one visibility timeout, and the pump
/// fires them one at a time, so the claims at the end of a long batch can lapse and be re-claimed by a peer. Each timer is
/// therefore fired under a <see cref="FencedClaimLease{TClaim}"/>: its claim is renewed immediately before the fire and
/// kept renewed while the fire runs. A timer whose claim was lost, before, during or at its completion or release, is
/// logged and skipped; it never ends the sweep for the timers behind it.
/// </para>
/// </remarks>
public sealed class DurableTimerPumpTask : BackoffSweepPumpTask
{
    private static readonly PersistenceScope DirectConstructionScope = new(PersistenceScope.DefaultValue);

    private readonly IPersistenceScopeRunner? _scopeRunner;
    private readonly IDurableTimerStore? _timerStore;
    private readonly IBookmarkResumeDispatcher? _dispatcher;
    private readonly IOptions<DurableTimerPumpOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<TimerKey, TimerBackoff> _timerBackoff = new();
    private readonly string _claimOwnerId = $"durable-timer-pump:{Guid.NewGuid():N}";

    [ActivatorUtilitiesConstructor]
    public DurableTimerPumpTask(
        IPersistenceScopeRunner scopeRunner,
        IOptions<DurableTimerPumpOptions> options,
        TimeProvider timeProvider,
        ILogger<DurableTimerPumpTask> logger)
        : this(options, timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(scopeRunner);
        _scopeRunner = scopeRunner;
    }

    /// <summary>Direct-construction seam retained for focused pump tests and custom hosts.</summary>
    public DurableTimerPumpTask(
        IDurableTimerStore timerStore,
        IBookmarkResumeDispatcher dispatcher,
        IOptions<DurableTimerPumpOptions> options,
        TimeProvider timeProvider,
        ILogger<DurableTimerPumpTask> logger)
        : this(options, timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(timerStore);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _timerStore = timerStore;
        _dispatcher = dispatcher;
    }

    private DurableTimerPumpTask(
        IOptions<DurableTimerPumpOptions> options,
        TimeProvider timeProvider,
        ILogger<DurableTimerPumpTask> logger)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = options;
        _timeProvider = timeProvider;
        if (_options.Value.ClaimVisibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Durable timer claim visibility timeout must be greater than zero.");
    }

    protected override TimeSpan SweepInterval => _options.Value.SweepInterval;

    protected override TimeSpan MaxBackoffInterval => _options.Value.MaxBackoffInterval;

    protected override async Task SweepAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var now = _timeProvider.GetUtcNow();

        PruneBackoff(options, now);

        if (_scopeRunner is null)
        {
            await SweepAsync(DirectConstructionScope, _timerStore!, _dispatcher!, options, now, cancellationToken);
        }
        else
        {
            await _scopeRunner.RunAsync(async (persistenceScope, operationScope, operationCancellationToken) =>
            {
                await SweepAsync(
                    persistenceScope,
                    operationScope.ServiceProvider.GetRequiredService<IDurableTimerStore>(),
                    operationScope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>(),
                    options,
                    now,
                    operationCancellationToken);
            }, cancellationToken);
        }
    }

    protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval) =>
        Logger.LogError(
            exception,
            "Durable timer sweep failed ({ConsecutiveFailures} consecutive); backing off to {Interval}",
            consecutiveFailures,
            backoffInterval);

    private async Task SweepAsync(
        PersistenceScope persistenceScope,
        IDurableTimerStore timerStore,
        IBookmarkResumeDispatcher dispatcher,
        DurableTimerPumpOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (timerStore.SupportsClaimTransitions)
        {
            var claims = await timerStore.ClaimDueAsync(
                new RuntimeDurableTimerClaimRequest(
                    _claimOwnerId,
                    now,
                    options.ClaimVisibilityTimeout,
                    options.MaxTimersPerTick),
                cancellationToken);
            var lease = new FencedClaimLease<RuntimeDurableTimerClaim>(
                RenewalOf(timerStore),
                options.ClaimVisibilityTimeout,
                _timeProvider);
            var claimedDispatches = 0;
            foreach (var claim in claims)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await FireClaimedAsync(
                        persistenceScope,
                        timerStore,
                        dispatcher,
                        lease,
                        claim,
                        options,
                        now,
                        cancellationToken))
                {
                    claimedDispatches++;
                }
            }

            if (claimedDispatches > 0 && Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(
                    "Durable timer sweep fired {Dispatched}/{DueCount} claimed timer(s)",
                    claimedDispatches,
                    claims.Count);
            }

            return;
        }

        var due = await timerStore.ListDueAsync(now, options.MaxTimersPerTick, cancellationToken);
        var dispatched = 0;

        foreach (var timer in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsBackingOff(persistenceScope, timer, now))
                continue;

            if (await FireAsync(persistenceScope, timerStore, dispatcher, timer, options, now, cancellationToken))
                dispatched++;
        }

        if (dispatched > 0 && Logger.IsEnabled(LogLevel.Debug))
            Logger.LogDebug("Durable timer sweep fired {Dispatched}/{DueCount} due timer(s)", dispatched, due.Count);
    }

    private async Task<bool> FireClaimedAsync(
        PersistenceScope persistenceScope,
        IDurableTimerStore timerStore,
        IBookmarkResumeDispatcher dispatcher,
        FencedClaimLease<RuntimeDurableTimerClaim> lease,
        RuntimeDurableTimerClaim claim,
        DurableTimerPumpOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var run = await lease.RunRenewingAsync(
            claim,
            dispatchCancellationToken => dispatcher.DispatchAsync(
                CreateRequest(claim.Timer),
                cancellationToken: dispatchCancellationToken),
            cancellationToken);

        switch (run.Status)
        {
            case FencedClaimRunStatus.LostBeforeSideEffect:
                LogClaimLost(claim, "renew before firing", status: null, run.Exception);
                return false;

            case FencedClaimRunStatus.LostDuringSideEffect:
                LogClaimLost(claim, "renew while firing", status: null, run.Exception);
                return false;

            case FencedClaimRunStatus.Faulted:
                Logger.LogError(run.Exception, "Durable timer '{TimerId}' dispatch threw; releasing with backoff", claim.Timer.TimerId);
                await ReleaseAfterFailureAsync(persistenceScope, timerStore, run.Claim, options, now, cancellationToken);
                return false;
        }

        var result = run.Result!;
        switch (result.Status)
        {
            case BookmarkResumeDispatchStatus.Dispatched:
            case BookmarkResumeDispatchStatus.Duplicate:
            case BookmarkResumeDispatchStatus.WorkflowExecutionMissing:
            case BookmarkResumeDispatchStatus.ExecutableMissing:
                await CompleteClaimAsync(persistenceScope, timerStore, run.Claim, cancellationToken);
                return result.Status is BookmarkResumeDispatchStatus.Dispatched or BookmarkResumeDispatchStatus.Duplicate;

            case BookmarkResumeDispatchStatus.NotFound:
                if (now - claim.Timer.DueTime > options.NotFoundGrace)
                    await CompleteClaimAsync(persistenceScope, timerStore, run.Claim, cancellationToken);
                else
                    await ReleaseAfterFailureAsync(persistenceScope, timerStore, run.Claim, options, now, cancellationToken);
                return false;

            default:
                await ReleaseAfterFailureAsync(persistenceScope, timerStore, run.Claim, options, now, cancellationToken);
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.LogDebug(
                        "Durable timer '{TimerId}' dispatch returned {Status}; releasing with backoff",
                        claim.Timer.TimerId,
                        result.Status);
                }
                return false;
        }
    }

    // Returns true when the timer was handed to the dispatcher (a real fire), false when it was skipped,
    // kept for retry, or deleted without a dispatch. A per-timer failure never escapes the sweep.
    private async Task<bool> FireAsync(
        PersistenceScope persistenceScope,
        IDurableTimerStore timerStore,
        IBookmarkResumeDispatcher dispatcher,
        DurableTimer timer,
        DurableTimerPumpOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        BookmarkResumeDispatchResult result;
        try
        {
            result = await dispatcher.DispatchAsync(CreateRequest(timer), cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            BackOff(persistenceScope, timer, options, now);
            Logger.LogError(exception, "Durable timer '{TimerId}' dispatch threw; backing off", timer.TimerId);
            return false;
        }

        switch (result.Status)
        {
            case BookmarkResumeDispatchStatus.Dispatched:
            case BookmarkResumeDispatchStatus.Duplicate:
            // The workflow execution or its executable is gone: the timer is orphaned, nothing to resume.
            case BookmarkResumeDispatchStatus.WorkflowExecutionMissing:
            case BookmarkResumeDispatchStatus.ExecutableMissing:
                await DeleteAsync(persistenceScope, timerStore, timer, cancellationToken);
                return result.Status is BookmarkResumeDispatchStatus.Dispatched or BookmarkResumeDispatchStatus.Duplicate;

            case BookmarkResumeDispatchStatus.NotFound:
                // Past grace: the bookmark was consumed by an earlier fire (or never will match) — delete.
                // Within grace: a very short delay may still be committing its bookmark — keep and retry.
                if (now - timer.DueTime > options.NotFoundGrace)
                    await DeleteAsync(persistenceScope, timerStore, timer, cancellationToken);
                else
                    BackOff(persistenceScope, timer, options, now);
                return false;

            default:
                // Rejected / Deferred / Ambiguous / ResumeResolutionFailed: transient or fault — keep the
                // timer and back off so a poisoned timer cannot occupy a dispatch slot every tick.
                BackOff(persistenceScope, timer, options, now);
                if (Logger.IsEnabled(LogLevel.Debug))
                    Logger.LogDebug("Durable timer '{TimerId}' dispatch returned {Status}; backing off", timer.TimerId, result.Status);
                return false;
        }
    }

    private async Task DeleteAsync(
        PersistenceScope persistenceScope,
        IDurableTimerStore timerStore,
        DurableTimer timer,
        CancellationToken cancellationToken)
    {
        await timerStore.DeleteAsync(timer.WorkflowExecutionId, timer.TimerId, cancellationToken);
        _timerBackoff.TryRemove(new TimerKey(persistenceScope, timer.WorkflowExecutionId, timer.TimerId), out _);
    }

    private async Task CompleteClaimAsync(
        PersistenceScope persistenceScope,
        IDurableTimerStore timerStore,
        RuntimeDurableTimerClaim claim,
        CancellationToken cancellationToken)
    {
        var completion = await timerStore.CompleteClaimAsync(claim, cancellationToken);
        if (!completion.Succeeded)
        {
            LogClaimLost(claim, "complete", completion.Status, exception: null);
            return;
        }

        _timerBackoff.TryRemove(
            new TimerKey(persistenceScope, claim.Timer.WorkflowExecutionId, claim.Timer.TimerId),
            out _);
    }

    private async Task ReleaseAfterFailureAsync(
        PersistenceScope persistenceScope,
        IDurableTimerStore timerStore,
        RuntimeDurableTimerClaim claim,
        DurableTimerPumpOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var failures = checked(claim.FailureCount + 1);
        var delay = ComputeBackoff(options.SweepInterval, options.MaxBackoffInterval, failures);
        var release = await timerStore.ReleaseClaimAsync(claim, now.Add(delay), cancellationToken);
        if (!release.Succeeded)
        {
            LogClaimLost(claim, "release", release.Status, exception: null);
            return;
        }

        _timerBackoff.TryRemove(
            new TimerKey(persistenceScope, claim.Timer.WorkflowExecutionId, claim.Timer.TimerId),
            out _);
    }

    private static BookmarkResumeDispatchRequest CreateRequest(DurableTimer timer) =>
        new(
            timer.WorkflowExecutionId,
            timer.StimulusType,
            timer.StimulusHash,
            timer.Input,
            idempotencyKey: $"timer:{timer.TimerId}",
            requestedBy: DurableTimerConstants.PumpRequestedBy,
            payloadType: timer.PayloadType,
            providerId: timer.ProviderId);

    private static FencedClaimRenewal<RuntimeDurableTimerClaim> RenewalOf(IDurableTimerStore timerStore) =>
        async (claim, now, visibilityTimeout, cancellationToken) =>
            await timerStore.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken) is
                { Status: RuntimeDurableTimerClaimTransitionStatus.Succeeded, Claim: { } renewed }
                ? renewed
                : null;

    // Warning, not Error: another claimant holds the timer and fires it, so nothing is lost. It is still worth seeing,
    // because it means a sweep outran the claim visibility timeout.
    private void LogClaimLost(
        RuntimeDurableTimerClaim claim,
        string transition,
        RuntimeDurableTimerClaimTransitionStatus? status,
        Exception? exception) =>
        Logger.LogWarning(
            exception,
            "Durable timer '{TimerId}' in workflow execution '{WorkflowExecutionId}' lost its claim during '{Transition}' (status {Status}); skipping it and leaving successor-owned state untouched",
            claim.Timer.TimerId,
            claim.Timer.WorkflowExecutionId,
            transition,
            status?.ToString() ?? "none");

    private bool IsBackingOff(PersistenceScope persistenceScope, DurableTimer timer, DateTimeOffset now) =>
        _timerBackoff.TryGetValue(new TimerKey(persistenceScope, timer.WorkflowExecutionId, timer.TimerId), out var backoff) &&
        backoff.NextEligibleAt > now;

    private void BackOff(PersistenceScope persistenceScope, DurableTimer timer, DurableTimerPumpOptions options, DateTimeOffset now)
    {
        var key = new TimerKey(persistenceScope, timer.WorkflowExecutionId, timer.TimerId);
        var failures = _timerBackoff.TryGetValue(key, out var current) ? current.Failures + 1 : 1;
        var delay = ComputeBackoff(options.SweepInterval, options.MaxBackoffInterval, failures);
        _timerBackoff[key] = new TimerBackoff(now + delay, failures);
    }

    private void PruneBackoff(DurableTimerPumpOptions options, DateTimeOffset now)
    {
        var pruneBefore = now - options.MaxBackoffInterval;

        foreach (var pair in _timerBackoff)
        {
            // Eligible for a full max-backoff window without reappearing: the timer is gone, drop it so the
            // map stays bounded.
            if (pair.Value.NextEligibleAt <= pruneBefore)
                _timerBackoff.TryRemove(pair.Key, out _);
        }
    }

    private readonly record struct TimerKey(PersistenceScope PersistenceScope, string WorkflowExecutionId, string TimerId);

    private readonly record struct TimerBackoff(DateTimeOffset NextEligibleAt, int Failures);
}
