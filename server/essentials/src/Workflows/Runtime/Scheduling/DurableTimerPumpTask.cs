using System.Collections.Concurrent;
using Elsa.Tasks.Core;
using Elsa.Tasks.Schedules;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Elsa.Workflows.Runtime.Services.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Recurring background pump that fires due <see cref="DurableTimer"/>s. Each tick runs one bounded sweep:
/// it attempts at most <see cref="DurableTimerPumpOptions.MaxTimersPerTick"/> eligible timers and dispatches each
/// as a bookmark resume through the single-writer <see cref="IBookmarkResumeDispatcher"/> (the same mailbox
/// path every other resume uses). Whole-sweep failure backoff comes from <see cref="BackoffSweepWorkerTask"/>;
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
/// fires them with bounded concurrency, so claims waiting behind long drains can lapse and be re-claimed by a peer. Each timer is
/// therefore fired under a <see cref="FencedClaimLease{TClaim}"/>: its claim is renewed immediately before the fire and
/// kept renewed while the fire runs. A timer whose claim was lost, before, during or at its completion or release, is
/// logged and skipped; it never ends the sweep for the timers behind it.
/// </para>
/// </remarks>
public sealed class DurableTimerPumpTask : BackoffSweepWorkerTask
{
    internal bool CanCaptureHistory(IPersistenceScopeRunner scopes, TimeProvider clock) =>
        _scopeRunner is not null && ReferenceEquals(_scopeRunner, scopes) && ReferenceEquals(_timeProvider, clock);
    private sealed class HistoryServices(IServiceProvider services, PersistenceScope scope)
    {
        private readonly Lazy<(IRuntimeHistoryObservationWriter? Writer, IRuntimeHistoryStore? Reader)> _value = new(() => Resolve(services, scope));
        internal IRuntimeHistoryObservationWriter? Writer => _value.Value.Writer;
        internal IRuntimeHistoryStore? Reader => _value.Value.Reader;

        private static (IRuntimeHistoryObservationWriter?, IRuntimeHistoryStore?) Resolve(IServiceProvider services, PersistenceScope scope)
        {
            var access = services.GetService<IPersistenceAccessContextAccessor>(); var captured = access?.Current;
            void Check()
            {
                if (access is not null && (access.Current != captured || captured!.RequireScope() != scope))
                    throw new InvalidOperationException("Timer history service resolution changed persistence scope.");
            }
            Check();
            try
            {
                var result = (services.GetService<IRuntimeHistoryObservationWriter>(), services.GetService<IRuntimeHistoryStore>());
                Check(); return result;
            }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                Check();
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Optional timer history services are unavailable.", error);
            }
        }
    }

    private sealed record HistoryDrain(bool Available, IReadOnlySet<TimerKey> Failed);

    private readonly ConcurrentDictionary<PersistenceScope, string> _historyCursors = new();
    private sealed record DueTraversal(DateTimeOffset Cutoff, int Limit, string Token);
    private readonly ConcurrentDictionary<PersistenceScope, DueTraversal> _dueCursors = new();

    private static readonly PersistenceScope DirectConstructionScope = new(PersistenceScope.DefaultValue);

    private readonly IPersistenceScopeRunner? _scopeRunner;
    private readonly IDurableTimerStore? _timerStore;
    private readonly IBookmarkResumeDispatcher? _dispatcher;
    private readonly IOptions<DurableTimerPumpOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<TimerKey, TimerBackoff> _timerBackoff = new();
    private readonly string _claimOwnerId = $"durable-timer-pump:{Guid.NewGuid():N}";
    private readonly DurableTimerWakeup _wakeup = new();
    private volatile bool _healthy = true;

    internal DurableTimerPumpTask(IPersistenceScopeRunner scopeRunner, IOptions<DurableTimerPumpOptions> options,
        TimeProvider timeProvider, ILogger<DurableTimerPumpTask> logger, DurableTimerWakeup wakeup)
        : this(scopeRunner, options, timeProvider, logger) => _wakeup = wakeup;

    internal DurableTimerPumpTask(IDurableTimerStore timerStore, IBookmarkResumeDispatcher dispatcher,
        IOptions<DurableTimerPumpOptions> options, TimeProvider timeProvider,
        ILogger<DurableTimerPumpTask> logger, DurableTimerWakeup wakeup)
        : this(timerStore, dispatcher, options, timeProvider, logger) => _wakeup = wakeup;

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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.Value.MaxConcurrentDispatches);
        if (_options.Value.ClaimVisibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Durable timer claim visibility timeout must be greater than zero.");
    }

    protected override TimeSpan SweepInterval => _options.Value.SweepInterval;

    protected override TimeSpan MaxBackoffInterval => _options.Value.MaxBackoffInterval;

    public override ITaskSchedule GetSchedule() => new DurableTimerDeadlineSchedule(
        _wakeup, () => CurrentSweepInterval, () => _healthy, _timeProvider, Logger);

    protected override async Task SweepAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var now = _timeProvider.GetUtcNow();
        var version = _wakeup.Snapshot.Version;
        DateTimeOffset? nextDeadline = null;

        PruneBackoff(options, now);

        if (_scopeRunner is null)
        {
            await SweepAsync(DirectConstructionScope, _timerStore!, _dispatcher!, options, now, cancellationToken);
            nextDeadline = await NextDeadlineAsync(_timerStore!, cancellationToken);
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
                    operationCancellationToken,
                    operationScope.ServiceProvider.GetService<IPersistenceOperationScopeFactory>(),
                    operationScope.ServiceProvider.GetService<IRuntimeAdmissionLoadSignal>(),
                    new HistoryServices(operationScope.ServiceProvider, persistenceScope));
                var next = await NextDeadlineAsync(operationScope.ServiceProvider.GetRequiredService<IDurableTimerStore>(),
                    operationCancellationToken);
                if (next is not null && (nextDeadline is null || next < nextDeadline))
                    nextDeadline = next;
            }, cancellationToken);
        }
        _wakeup.Reconcile(version, nextDeadline);
        _healthy = true;
    }

    // Legacy providers keep retry eligibility in this pump's private backoff map, so their raw deadline
    // query cannot safely accelerate sweeps. Only provider-owned visibility can authorize that shortcut.
    private static ValueTask<DateTimeOffset?> NextDeadlineAsync(IDurableTimerStore store, CancellationToken cancellationToken) =>
        store.SupportsClaimTransitions ? store.GetNextDueTimeAsync(cancellationToken) : ValueTask.FromResult<DateTimeOffset?>(null);

    protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval)
    {
        _healthy = false;
        Logger.LogError(
            exception,
            "Durable timer sweep failed ({ConsecutiveFailures} consecutive); backing off to {Interval}",
            consecutiveFailures,
            backoffInterval);
    }

    private async Task SweepAsync(
        PersistenceScope persistenceScope,
        IDurableTimerStore timerStore,
        IBookmarkResumeDispatcher dispatcher,
        DurableTimerPumpOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        IPersistenceOperationScopeFactory? scopeFactory = null,
        IRuntimeAdmissionLoadSignal? loadSignal = null,
        HistoryServices? history = null)
    {
        var historyDrain = await DrainHistoryAsync(persistenceScope, timerStore, history, options.MaxTimersPerTick, cancellationToken);
        if (timerStore.SupportsClaimTransitions)
        {
            _dueCursors.TryRemove(persistenceScope, out _);
            var claims = await timerStore.ClaimDueAsync(
                new RuntimeDurableTimerClaimRequest(
                    _claimOwnerId,
                    now,
                    options.ClaimVisibilityTimeout,
                    options.MaxTimersPerTick),
                cancellationToken);
            // Renewal has its own context: a long inline drain must never race its renewal on a shared DbContext.
            async ValueTask<RuntimeDurableTimerClaim?> RenewAsync(RuntimeDurableTimerClaim claim, DateTimeOffset at,
                TimeSpan visibility, CancellationToken ct)
            {
                RuntimeDurableTimerClaimTransitionResult result;
                if (scopeFactory is null)
                    result = await timerStore.RenewClaimAsync(claim, at, visibility, ct);
                else
                {
                    await using var scope = await scopeFactory.CreateAsync(persistenceScope, ct);
                    result = await scope.ServiceProvider.GetRequiredService<IDurableTimerStore>().RenewClaimAsync(claim, at, visibility, ct);
                }
                return result is { Status: RuntimeDurableTimerClaimTransitionStatus.Succeeded, Claim: { } renewed } ? renewed : null;
            }
            var lease = new FencedClaimLease<RuntimeDurableTimerClaim>(RenewAsync, options.ClaimVisibilityTimeout, _timeProvider);
            var claimedDispatches = 0;
            if (scopeFactory is not null && options.MaxConcurrentDispatches > 1)
            {
                // Preserve provider order for one execution, while independent mailboxes can drain together.
                await Parallel.ForEachAsync(claims.GroupBy(x => x.Timer.WorkflowExecutionId, StringComparer.Ordinal),
                    new ParallelOptions { MaxDegreeOfParallelism = options.MaxConcurrentDispatches, CancellationToken = cancellationToken },
                    async (group, ct) =>
                    {
                        foreach (var claim in group)
                        {
                            await using var scope = await scopeFactory.CreateAsync(persistenceScope, ct);
                            if (await FireClaimedAsync(persistenceScope,
                                    scope.ServiceProvider.GetRequiredService<IDurableTimerStore>(),
                                    scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>(),
                                    lease, claim, options, now, ct, loadSignal,
                                    new HistoryServices(scope.ServiceProvider, persistenceScope), historyDrain))
                                Interlocked.Increment(ref claimedDispatches);
                        }
                    });
            }
            else
            {
                foreach (var claim in claims)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (await FireClaimedAsync(persistenceScope, timerStore, dispatcher, lease, claim, options, now,
                            cancellationToken, loadSignal, history, historyDrain))
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

            await DrainHistoryAsync(persistenceScope, timerStore, history, options.MaxTimersPerTick, cancellationToken);
            return;
        }

        RuntimeStorePage<DurableTimer>? page = null;
        DueTraversal? traversal = null;
        IReadOnlyCollection<DurableTimer> due;
        if (timerStore is IDurableTimerHistoryStore owner)
        {
            traversal = _dueCursors.GetValueOrDefault(persistenceScope);
            if (traversal is not null && (traversal.Limit != options.MaxTimersPerTick || traversal.Cutoff > now)) traversal = null;
            page = await owner.ListDuePageAsync(traversal?.Cutoff ?? now,
                new(options.MaxTimersPerTick, traversal?.Token), cancellationToken);
            due = page.Items;
        }
        else due = await timerStore.ListDueAsync(now, options.MaxTimersPerTick, cancellationToken);
        var dispatched = 0;

        foreach (var timer in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (timer.DueTime > now || IsBackingOff(persistenceScope, timer, now)) continue;

            if (await FireAsync(persistenceScope, timerStore, dispatcher, timer, options, now, cancellationToken, loadSignal, history, historyDrain))
                dispatched++;
        }

        if (page?.NextContinuationToken is { } next)
            _dueCursors[persistenceScope] = new(traversal?.Cutoff ?? now, options.MaxTimersPerTick, next);
        else _dueCursors.TryRemove(persistenceScope, out _);

        if (dispatched > 0 && Logger.IsEnabled(LogLevel.Debug))
            Logger.LogDebug("Durable timer sweep fired {Dispatched}/{DueCount} due timer(s)", dispatched, due.Count);
        await DrainHistoryAsync(persistenceScope, timerStore, history, options.MaxTimersPerTick, cancellationToken);
    }

    private async Task<bool> FireClaimedAsync(
        PersistenceScope persistenceScope,
        IDurableTimerStore timerStore,
        IBookmarkResumeDispatcher dispatcher,
        FencedClaimLease<RuntimeDurableTimerClaim> lease,
        RuntimeDurableTimerClaim claim,
        DurableTimerPumpOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        IRuntimeAdmissionLoadSignal? loadSignal = null,
        HistoryServices? history = null, HistoryDrain? historyDrain = null)
    {
        if (!PrepareHistory(persistenceScope, timerStore, dispatcher, claim.Timer, history, historyDrain, cancellationToken))
        {
            await ReleaseAfterFailureAsync(persistenceScope, timerStore, claim, options, now, cancellationToken);
            return false;
        }
        var run = await lease.RunRenewingAsync(
            claim,
            async dispatchCancellationToken =>
            {
                using var charge = loadSignal is { HasAmbientCharge: false } ? loadSignal.OpenCharge() : null;
                return await dispatcher.DispatchAsync(CreateRequest(claim.Timer), cancellationToken: dispatchCancellationToken);
            },
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
        try
        {
            switch (result.Status)
            {
                case BookmarkResumeDispatchStatus.Dispatched:
                case BookmarkResumeDispatchStatus.Duplicate:
                case BookmarkResumeDispatchStatus.WorkflowExecutionMissing:
                case BookmarkResumeDispatchStatus.ExecutableMissing:
                    await CompleteClaimAsync(persistenceScope, timerStore, run.Claim, result.Status, cancellationToken);
                    return result.Status is BookmarkResumeDispatchStatus.Dispatched or BookmarkResumeDispatchStatus.Duplicate;

                case BookmarkResumeDispatchStatus.NotFound:
                    if (now - claim.Timer.DueTime > options.NotFoundGrace)
                        await CompleteClaimAsync(persistenceScope, timerStore, run.Claim, result.Status, cancellationToken);
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
        catch (Exception error) when (run.Claim.Timer.HistoryResponsibility is not null &&
            IsTerminalDisposition(run.Claim.Timer, result.Status, options, now) && IsHistoryFailure(error))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger.LogError(error, "Durable timer '{TimerId}' terminal history is unavailable; keeping its responsibility for retry", run.Claim.Timer.TimerId);
            try
            {
                await ReleaseAfterFailureAsync(persistenceScope, timerStore, run.Claim, options, now, cancellationToken);
            }
            catch (Exception releaseError) when (IsHistoryFailure(releaseError))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Logger.LogError(releaseError, "Durable timer '{TimerId}' claim release is unavailable; retaining its visibility lease for recovery", run.Claim.Timer.TimerId);
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
        CancellationToken cancellationToken,
        IRuntimeAdmissionLoadSignal? loadSignal = null,
        HistoryServices? history = null, HistoryDrain? historyDrain = null)
    {
        if (!PrepareHistory(persistenceScope, timerStore, dispatcher, timer, history, historyDrain, cancellationToken))
        {
            BackOff(persistenceScope, timer, options, now);
            return false;
        }
        BookmarkResumeDispatchResult result;
        try
        {
            using var charge = loadSignal is { HasAmbientCharge: false } ? loadSignal.OpenCharge() : null;
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

        try
        {
            switch (result.Status)
            {
                case BookmarkResumeDispatchStatus.Dispatched:
                case BookmarkResumeDispatchStatus.Duplicate:
                // The workflow execution or its executable is gone: the timer is orphaned, nothing to resume.
                case BookmarkResumeDispatchStatus.WorkflowExecutionMissing:
                case BookmarkResumeDispatchStatus.ExecutableMissing:
                    await DeleteAsync(persistenceScope, timerStore, timer, result.Status, cancellationToken);
                    return result.Status is BookmarkResumeDispatchStatus.Dispatched or BookmarkResumeDispatchStatus.Duplicate;

                case BookmarkResumeDispatchStatus.NotFound:
                    // Past grace: the bookmark was consumed by an earlier fire (or never will match) — delete.
                    // Within grace: a very short delay may still be committing its bookmark — keep and retry.
                    if (now - timer.DueTime > options.NotFoundGrace)
                        await DeleteAsync(persistenceScope, timerStore, timer, result.Status, cancellationToken);
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
        catch (Exception error) when (timer.HistoryResponsibility is not null &&
            IsTerminalDisposition(timer, result.Status, options, now) && IsHistoryFailure(error))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger.LogError(error, "Durable timer '{TimerId}' terminal history is unavailable; keeping its responsibility for retry", timer.TimerId);
            BackOff(persistenceScope, timer, options, now);
            return false;
        }
    }

    private async Task DeleteAsync(
        PersistenceScope persistenceScope,
        IDurableTimerStore timerStore,
        DurableTimer timer,
        BookmarkResumeDispatchStatus status,
        CancellationToken cancellationToken)
    {
        if (timer.HistoryResponsibility is not null)
            await ((IDurableTimerHistoryStore)timerStore).RetireWithHistoryAsync(timer.WorkflowExecutionId, timer.TimerId,
                TerminalObservation(timer, status), cancellationToken);
        else await timerStore.DeleteAsync(timer.WorkflowExecutionId, timer.TimerId, cancellationToken);
        _timerBackoff.TryRemove(new TimerKey(persistenceScope, timer.WorkflowExecutionId, timer.TimerId), out _);
    }

    private async Task CompleteClaimAsync(
        PersistenceScope persistenceScope,
        IDurableTimerStore timerStore,
        RuntimeDurableTimerClaim claim,
        BookmarkResumeDispatchStatus status,
        CancellationToken cancellationToken)
    {
        var completion = claim.Timer.HistoryResponsibility is not null
            ? await ((IDurableTimerHistoryStore)timerStore).CompleteClaimWithHistoryAsync(claim, TerminalObservation(claim.Timer, status), cancellationToken)
            : await timerStore.CompleteClaimAsync(claim, cancellationToken);
        if (!completion.Succeeded)
        {
            LogClaimLost(claim, "complete", completion.Status, exception: null);
            return;
        }

        _timerBackoff.TryRemove(
            new TimerKey(persistenceScope, claim.Timer.WorkflowExecutionId, claim.Timer.TimerId),
            out _);
    }

    private static bool IsTerminalDisposition(DurableTimer timer, BookmarkResumeDispatchStatus status,
        DurableTimerPumpOptions options, DateTimeOffset now) => status is
        BookmarkResumeDispatchStatus.Dispatched or BookmarkResumeDispatchStatus.Duplicate or
        BookmarkResumeDispatchStatus.WorkflowExecutionMissing or BookmarkResumeDispatchStatus.ExecutableMissing ||
        status == BookmarkResumeDispatchStatus.NotFound && now - timer.DueTime > options.NotFoundGrace;

    private RuntimeHistoryObservation TerminalObservation(DurableTimer timer, BookmarkResumeDispatchStatus status) =>
        (status is BookmarkResumeDispatchStatus.Dispatched or BookmarkResumeDispatchStatus.Duplicate
            ? DurableTimerHistoryFacts.CreateDispatched(timer, _timeProvider.GetUtcNow(), status, TimerCause(timer))
            : DurableTimerHistoryFacts.CreateRetired(timer, _timeProvider.GetUtcNow(), status.ToString(), TimerCause(timer)))
        ?? throw new InvalidOperationException("The original timer policy omits its terminal disposition.");

    private bool PrepareHistory(PersistenceScope scope, IDurableTimerStore store, IBookmarkResumeDispatcher dispatcher,
        DurableTimer timer, HistoryServices? services, HistoryDrain? drain, CancellationToken token)
    {
        if (timer.HistoryResponsibility is null) return true;
        if (drain is { Available: false } || drain?.Failed.Contains(new(scope, timer.WorkflowExecutionId, timer.TimerId)) == true)
            return false;
        try
        {
            var writer = services?.Writer; var reader = services?.Reader;
            if (store is not IDurableTimerHistoryStore { SupportsHistoryResponsibility: true } owner ||
                writer is null || reader is null || !owner.CanInspectHistorySources(reader) ||
                !writer.CanRecordHistory(reader) || !dispatcher.CanRecordHistory(writer, reader))
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable",
                    "The retained timer requires its actual history owner, dispatcher and observation chain.");
            return true;
        }
        catch (Exception error) when (IsHistoryFailure(error))
        {
            token.ThrowIfCancellationRequested();
            Logger.LogError(error, "Durable timer '{TimerId}' history chain is unavailable; keeping it for retry", timer.TimerId);
            return false;
        }
    }

    private static bool IsHistoryFailure(Exception error) => error is RuntimeHistoryUnavailableException or
        RuntimeHistoryIntegrityException or RuntimeSchedulerWorkUnavailableException or IOException or TimeoutException or OperationCanceledException;

    private async Task<HistoryDrain> DrainHistoryAsync(PersistenceScope scope, IDurableTimerStore timerStore,
        HistoryServices? services, int limit, CancellationToken token)
    {
        var failed = new HashSet<TimerKey>();
        if (timerStore is not IDurableTimerHistoryStore { SupportsHistoryResponsibility: true } owner) return new(true, failed);
        try
        {
            var page = await owner.ListPendingHistoryAsync(new(limit, _historyCursors.GetValueOrDefault(scope)), token);
            if (page.Items.Count > 0)
            {
                var writer = services?.Writer; var reader = services?.Reader;
                if (writer is null || reader is null || !owner.CanInspectHistorySources(reader) || !writer.CanRecordHistory(reader))
                    throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Pending timer history requires its selected observation chain.");
                foreach (var timer in page.Items)
                {
                    if (timer.HistoryResponsibility is not { } responsibility) throw new InvalidOperationException("A pending timer has no original history responsibility.");
                    try
                    {
                        foreach (var e in responsibility.PendingEvents)
                        {
                            var recorded = await writer.RecordAsync(new(responsibility.Policy, e), token);
                            if (!recorded.Recorded) break; // Native history may not exist yet; source facts remain owed.
                            if (!await owner.AcknowledgeHistoryAsync(timer.WorkflowExecutionId, timer.TimerId, e.HistoryEventId, token)) break;
                        }
                    }
                    catch (Exception error) when (IsHistoryFailure(error))
                    {
                        token.ThrowIfCancellationRequested(); failed.Add(new(scope, timer.WorkflowExecutionId, timer.TimerId));
                        Logger.LogError(error, "Durable timer '{TimerId}' history remains owed; keeping it for retry", timer.TimerId);
                    }
                }
            }
            if (page.NextContinuationToken is { } next) _historyCursors[scope] = next;
            else _historyCursors.TryRemove(scope, out _);
            return new(true, failed);
        }
        catch (Exception error) when (IsHistoryFailure(error))
        {
            token.ThrowIfCancellationRequested();
            Logger.LogError(error, "Timer history source inspection is unavailable; captured timers remain held for retry");
            return new(false, failed);
        }
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
            providerId: timer.ProviderId) { HistoryCausation = TimerCause(timer) };

    private static RuntimeHistoryCausation? TimerCause(DurableTimer timer) => timer.HistoryResponsibility is { } responsibility
        ? new(HistoryEventId: DurableTimerHistoryFacts.CreateScheduled(timer, responsibility.Policy)!.Event.HistoryEventId,
            WorkflowExecutionId: timer.WorkflowExecutionId) : null;


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


    private bool IsBackingOff(PersistenceScope scope, DurableTimer timer, DateTimeOffset now) =>
        _timerBackoff.TryGetValue(new(scope, timer.WorkflowExecutionId, timer.TimerId), out var backoff) && backoff.NextEligibleAt > now;

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
