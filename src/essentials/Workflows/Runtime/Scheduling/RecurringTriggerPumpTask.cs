using Elsa.Tasks.Schedules;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Elsa.Workflows.Runtime.Services.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Recurring background pump that fires due <see cref="RecurringTriggerSchedule"/>s. Each tick runs one
/// bounded sweep: it claims at most <see cref="RecurringTriggerPumpOptions.MaxSchedulesPerTick"/> due occurrences
/// and, for each, dispatches the schedule's start stimulus through <see cref="IStimulusRouter"/> in
/// <see cref="StimulusRoutingMode.StartOnly"/> mode — starting a new workflow instance with no execution id,
/// the piece the resume-oriented durable-timer pump cannot do.
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-scoped dispatch.</b> A stimulus hash is a pure function of the authored interval/cron literal, so two
/// published workflows that authored the same literal share one (StimulusType, StimulusHash) pair. The router's
/// start path intentionally fans an externally-sent stimulus out to every matching artifact — correct for events,
/// but a recurring schedule is owned by exactly one trigger node: hash-broadcasting a fire would start every
/// same-literal workflow on EVERY schedule's cadence (double-starts per cycle). The pump therefore resolves the
/// binding the schedule owns — matching artifact, trigger node, and activation scope — and dispatches with that
/// binding pre-matched (<see cref="StimulusDispatchRequest.MatchedTriggerBindings"/>), so a fire starts only the
/// workflow whose publish wrote the schedule.
/// </para>
/// <para>
/// <b>At least once per occurrence (#2198).</b> The occurrence is recorded as in flight before it is routed: the pump
/// claims it (<see cref="IRecurringTriggerScheduleStore.ClaimDueAsync"/>) under a fenced lease of
/// <see cref="RecurringTriggerPumpOptions.ClaimVisibilityTimeout"/>, which leaves the cursor on the occurrence, and moves
/// the cursor past it only by settling the claim after the route returned. Nothing else drops it. A node that dies after
/// claiming, before or after routing, leaves the claim to lapse, and a peer then fires the occurrence again. A route that
/// throws, or that finds no binding owned by the schedule (index drift mid-republish), releases the claim with a
/// geometric backoff capped at <see cref="RecurringTriggerPumpOptions.MaxBackoffInterval"/>, so the occurrence is retried
/// rather than skipped. Each claim is renewed immediately before its route (<see cref="FencedClaimLease{TClaim}"/>), and a
/// claim lost before the route, or found stale when settling or releasing, is logged and skipped without ending the sweep.
/// </para>
/// <para>
/// <b>Start once per occurrence.</b> Every fire of one occurrence carries the same idempotency key,
/// <c>recurring:{ScheduleId}:{occurrenceTicks}</c>, so the router starts it as a keyed start
/// (<see cref="KeyedWorkflowStartIdentity"/>, #2195): a repeated fire, on any node and after any restart, converges on
/// the execution the first fire started instead of starting another.
/// </para>
/// <para>
/// <b>Missed-occurrence policy — no catch-up.</b> A schedule is due when its
/// <see cref="RecurringTriggerSchedule.NextOccurrence"/> is at or before the wake instant. Settling moves the cursor to
/// the first occurrence strictly after <i>now</i> (via <see cref="IRecurringScheduleCalculator"/>), so the occurrence in
/// the cursor fires, and the occurrences that elapsed while the pump was down, or while a failing occurrence was being
/// retried, are not replayed.
/// </para>
/// <para>
/// <b>Whole-sweep backoff.</b> Comes from <see cref="BackoffSweepPumpTask"/>: a sweep that throws is caught,
/// logged, and never rethrown; consecutive failures widen the schedule interval geometrically up to
/// <see cref="RecurringTriggerPumpOptions.MaxBackoffInterval"/>, and the first clean sweep resets it.
/// </para>
/// </remarks>
public sealed class RecurringTriggerPumpTask : BackoffSweepPumpTask
{
    private const string PumpRequestedBy = "runtime.recurring-trigger";

    private readonly IPersistenceScopeRunner? _scopeRunner;
    private readonly IRecurringTriggerScheduleStore? _store;
    private readonly IWorkflowTriggerBindingStore? _bindingStore;
    private readonly IStimulusRouter? _router;
    private readonly IRecurringScheduleCalculator? _calculator;
    private readonly IOptions<RecurringTriggerPumpOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly string _claimOwnerId = $"recurring-trigger-pump:{Guid.NewGuid():N}";

    [ActivatorUtilitiesConstructor]
    public RecurringTriggerPumpTask(
        IPersistenceScopeRunner scopeRunner,
        IOptions<RecurringTriggerPumpOptions> options,
        TimeProvider timeProvider,
        ILogger<RecurringTriggerPumpTask> logger)
        : this(options, timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(scopeRunner);
        _scopeRunner = scopeRunner;
    }

    /// <summary>Direct-construction seam retained for focused pump tests and custom hosts.</summary>
    public RecurringTriggerPumpTask(
        IRecurringTriggerScheduleStore store,
        IWorkflowTriggerBindingStore bindingStore,
        IStimulusRouter router,
        IRecurringScheduleCalculator calculator,
        IOptions<RecurringTriggerPumpOptions> options,
        TimeProvider timeProvider,
        ILogger<RecurringTriggerPumpTask> logger)
        : this(options, timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(bindingStore);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(calculator);

        _store = store;
        _bindingStore = bindingStore;
        _router = router;
        _calculator = calculator;
    }

    private RecurringTriggerPumpTask(
        IOptions<RecurringTriggerPumpOptions> options,
        TimeProvider timeProvider,
        ILogger<RecurringTriggerPumpTask> logger)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = options;
        _timeProvider = timeProvider;
        if (_options.Value.ClaimVisibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Recurring-trigger claim visibility timeout must be greater than zero.");
    }

    protected override TimeSpan SweepInterval => _options.Value.SweepInterval;

    protected override TimeSpan MaxBackoffInterval => _options.Value.MaxBackoffInterval;

    protected override async Task SweepAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var now = _timeProvider.GetUtcNow();

        if (_scopeRunner is null)
        {
            await SweepAsync(_store!, _bindingStore!, _router!, _calculator!, options, now, cancellationToken);
        }
        else
        {
            await _scopeRunner.RunAsync(async (_, operationScope, operationCancellationToken) =>
            {
                await SweepAsync(
                    operationScope.ServiceProvider.GetRequiredService<IRecurringTriggerScheduleStore>(),
                    operationScope.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>(),
                    operationScope.ServiceProvider.GetRequiredService<IStimulusRouter>(),
                    operationScope.ServiceProvider.GetRequiredService<IRecurringScheduleCalculator>(),
                    options,
                    now,
                    operationCancellationToken);
            }, cancellationToken);
        }
    }

    protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval) =>
        Logger.LogError(
            exception,
            "Recurring-trigger sweep failed ({ConsecutiveFailures} consecutive); backing off to {Interval}",
            consecutiveFailures,
            backoffInterval);

    private async Task SweepAsync(
        IRecurringTriggerScheduleStore store,
        IWorkflowTriggerBindingStore bindingStore,
        IStimulusRouter router,
        IRecurringScheduleCalculator calculator,
        RecurringTriggerPumpOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var claims = await store.ClaimDueAsync(
            new RecurringTriggerOccurrenceClaimRequest(_claimOwnerId, now, options.ClaimVisibilityTimeout, options.MaxSchedulesPerTick),
            cancellationToken);
        // Renewed before each route only, never during it: the router's start dispatch shares this scope's unit of work, so
        // a renewal running beside it would collide with it. A route that outlives the lease is repeated by a peer, and the
        // repeat converges on the keyed start.
        var lease = new FencedClaimLease<RecurringTriggerOccurrenceClaim>(store.RenewClaimAsync, options.ClaimVisibilityTimeout, _timeProvider);
        var fired = 0;

        foreach (var claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await FireAsync(store, bindingStore, router, calculator, lease, claim, options, now, cancellationToken))
                fired++;
        }

        if (fired > 0 && Logger.IsEnabled(LogLevel.Debug))
            Logger.LogDebug("Recurring-trigger sweep fired {Fired}/{DueCount} claimed occurrence(s)", fired, claims.Count);
    }

    // Returns true when the claimed occurrence was routed. A per-occurrence failure never escapes the sweep; it leaves the
    // occurrence in the cursor to be fired again.
    private async Task<bool> FireAsync(
        IRecurringTriggerScheduleStore store,
        IWorkflowTriggerBindingStore bindingStore,
        IStimulusRouter router,
        IRecurringScheduleCalculator calculator,
        FencedClaimLease<RecurringTriggerOccurrenceClaim> lease,
        RecurringTriggerOccurrenceClaim claim,
        RecurringTriggerPumpOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var schedule = claim.Schedule;
        DateTimeOffset? next;
        try
        {
            next = calculator.ComputeNext(schedule.Kind, schedule.Expression, now);
        }
        catch (Exception exception)
        {
            // A schedule whose expression no longer parses cannot advance; drop it so it does not jam the sweep.
            Logger.LogError(exception, "Recurring schedule '{ScheduleId}' has an invalid expression; deleting it", schedule.ScheduleId);
            await store.DeleteAsync(schedule.ScheduleId, cancellationToken);
            return false;
        }

        if (next is null)
        {
            // Cron exhausted (no future occurrence): remove the schedule rather than leave it perpetually due.
            await store.DeleteAsync(schedule.ScheduleId, cancellationToken);
            return false;
        }

        var run = await lease.RunAsync(
            claim,
            routeCancellationToken => RouteAsync(bindingStore, router, schedule, routeCancellationToken),
            cancellationToken);

        switch (run.Status)
        {
            case FencedClaimRunStatus.LostBeforeSideEffect:
            case FencedClaimRunStatus.LostDuringSideEffect:
                LogClaimLost(claim, "renew before routing", run.Exception);
                return false;

            case FencedClaimRunStatus.Faulted:
                Logger.LogError(
                    run.Exception,
                    "Recurring schedule '{ScheduleId}' start dispatch for occurrence {Occurrence} threw; the occurrence is kept and retried after backoff",
                    schedule.ScheduleId,
                    schedule.NextOccurrence);
                await ReleaseAsync(store, run.Claim, options, now, cancellationToken);
                return false;
        }

        if (!run.Result)
        {
            // Index drift (e.g. mid-republish): the occurrence is kept rather than hash-broadcast to whatever other
            // artifacts share the stimulus hash, and is retried against the refreshed index after backoff.
            Logger.LogWarning(
                "Recurring schedule '{ScheduleId}' is due but no trigger binding is owned by artifact '{ArtifactId}' node '{ExecutableNodeId}'; the occurrence is kept and retried after backoff",
                schedule.ScheduleId,
                schedule.ArtifactId,
                schedule.ExecutableNodeId);
            await ReleaseAsync(store, run.Claim, options, now, cancellationToken);
            return false;
        }

        if (!await store.SettleClaimAsync(run.Claim, next.Value, cancellationToken))
            LogClaimLost(claim, "settle", exception: null);
        return true;
    }

    // Routes the occurrence through the schedule's own binding. Returns false when no binding is owned by the schedule.
    private static async ValueTask<bool> RouteAsync(
        IWorkflowTriggerBindingStore bindingStore,
        IStimulusRouter router,
        RecurringTriggerSchedule schedule,
        CancellationToken cancellationToken)
    {
        // Owner scoping: the stimulus hash is shared by every workflow that authored the same literal, so the fire must
        // carry the schedule's OWN binding rather than let the router hash-broadcast the start.
        var ownedBindings = await ResolveOwnedBindingsAsync(bindingStore, schedule, cancellationToken);
        if (ownedBindings.Count == 0)
            return false;

        var request = new StimulusDispatchRequest(
            stimulusType: schedule.StimulusType,
            stimulusHash: schedule.StimulusHash,
            mode: StimulusRoutingMode.StartOnly,
            idempotencyKey: $"recurring:{schedule.ScheduleId}:{schedule.NextOccurrence.UtcTicks}",
            requestedBy: PumpRequestedBy,
            matchedTriggerBindings: ownedBindings);

        await router.RouteAsync(request, cancellationToken);
        return true;
    }

    private async Task ReleaseAsync(
        IRecurringTriggerScheduleStore store,
        RecurringTriggerOccurrenceClaim claim,
        RecurringTriggerPumpOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var delay = ComputeBackoff(options.SweepInterval, options.MaxBackoffInterval, checked(claim.FailureCount + 1));
        if (!await store.ReleaseClaimAsync(claim, now.Add(delay), cancellationToken))
            LogClaimLost(claim, "release", exception: null);
    }

    // Warning, not Error: another claimant holds the occurrence and fires it, so nothing is lost. It is still worth seeing,
    // because it means a fire outlasted the claim visibility timeout or the schedule changed while it was in flight.
    private void LogClaimLost(RecurringTriggerOccurrenceClaim claim, string transition, Exception? exception) =>
        Logger.LogWarning(
            exception,
            "Recurring schedule '{ScheduleId}' lost its claim on occurrence {Occurrence} during '{Transition}'; skipping it and leaving successor-owned state untouched",
            claim.Schedule.ScheduleId,
            claim.Schedule.NextOccurrence,
            transition);

    // The binding the schedule owns: same artifact, same trigger node, and same activation scope (named slots may
    // share one artifact, so an activation-scoped schedule must not start through another slot's binding). The
    // stimulus identity is already the query key. Normally exactly one binding survives the filter.
    private static async Task<IReadOnlyList<WorkflowTriggerBinding>> ResolveOwnedBindingsAsync(
        IWorkflowTriggerBindingStore bindingStore,
        RecurringTriggerSchedule schedule,
        CancellationToken cancellationToken)
    {
        var matches = await bindingStore.ListAllByStimulusAsync(schedule.StimulusType, schedule.StimulusHash, cancellationToken);
        return matches
            .Where(binding =>
                StringComparer.Ordinal.Equals(binding.ArtifactId, schedule.ArtifactId) &&
                StringComparer.Ordinal.Equals(binding.ExecutableNodeId, schedule.ExecutableNodeId) &&
                StringComparer.Ordinal.Equals(binding.ActivationId, schedule.ActivationId) &&
                StringComparer.Ordinal.Equals(binding.SlotId, schedule.SlotId))
            .ToArray();
    }
}
