using Elsa.Persistence.Core;
using Elsa.Tasks.Schedules;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Recurring background pump that fires due <see cref="RecurringTriggerSchedule"/>s (W16). Each tick runs one
/// bounded sweep: it loads at most <see cref="RecurringTriggerPumpOptions.MaxSchedulesPerTick"/> due schedules
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
/// workflow whose publish wrote the schedule. A fire whose owning binding no longer exists (index drift between
/// republish steps) is dropped with a warning rather than broadcast.
/// </para>
/// <para>
/// <b>Missed-occurrence policy — fire at most once, then advance.</b> A schedule is due when its
/// <see cref="RecurringTriggerSchedule.NextOccurrence"/> is at or before the wake instant. The pump does NOT
/// replay the backlog of occurrences that elapsed while it was down: it advances the cursor to the first
/// occurrence strictly after <i>now</i> (via <see cref="IRecurringScheduleCalculator"/>) and fires exactly once.
/// </para>
/// <para>
/// <b>Claim-first (single fire, W20-ready).</b> The cursor is advanced through the store's compare-and-swap
/// (<see cref="IRecurringTriggerScheduleStore.TryAdvanceAsync"/>) <i>before</i> the start is dispatched, so at
/// most one worker fires a given occurrence even if several sweep concurrently — the single-node realization of
/// the cluster-safe claim a future distributed store (W20) keeps. A crash between the claim and the dispatch
/// loses that one fire, which is the accepted at-most-once trade: the next occurrence still fires, and the
/// backlog is never replayed.
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
        var due = await store.ListDueAsync(now, options.MaxSchedulesPerTick, cancellationToken);
        var fired = 0;

        foreach (var schedule in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await FireAsync(store, bindingStore, router, calculator, schedule, now, cancellationToken))
                fired++;
        }

        if (fired > 0 && Logger.IsEnabled(LogLevel.Debug))
            Logger.LogDebug("Recurring-trigger sweep fired {Fired}/{DueCount} due schedule(s)", fired, due.Count);
    }

    // Returns true when the schedule's occurrence was claimed and a start dispatched. A per-schedule failure
    // never escapes the sweep.
    private async Task<bool> FireAsync(
        IRecurringTriggerScheduleStore store,
        IWorkflowTriggerBindingStore bindingStore,
        IStimulusRouter router,
        IRecurringScheduleCalculator calculator,
        RecurringTriggerSchedule schedule,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
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

        // Claim-first: advance the cursor via CAS before dispatching, so a concurrent sweep cannot double-fire
        // this occurrence.
        var claimed = await store.TryAdvanceAsync(schedule.ScheduleId, schedule.NextOccurrence, next.Value, cancellationToken);
        if (!claimed)
            return false;

        try
        {
            // Owner scoping: the stimulus hash is shared by every workflow that authored the same literal, so the
            // fire must carry the schedule's OWN binding rather than let the router hash-broadcast the start.
            var ownedBindings = await ResolveOwnedBindingsAsync(bindingStore, schedule, cancellationToken);
            if (ownedBindings.Count == 0)
            {
                // Index drift (e.g. mid-republish): the occurrence stays claimed by the at-most-once contract, and
                // the next occurrence fires against the refreshed index.
                Logger.LogWarning(
                    "Recurring schedule '{ScheduleId}' fired but no trigger binding is owned by artifact '{ArtifactId}' node '{ExecutableNodeId}'; occurrence dropped",
                    schedule.ScheduleId,
                    schedule.ArtifactId,
                    schedule.ExecutableNodeId);
                return false;
            }

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The occurrence was already claimed (cursor advanced), so this fire is lost by the at-most-once
            // contract; the next occurrence will still fire. Log and move on rather than replay.
            Logger.LogError(exception, "Recurring schedule '{ScheduleId}' start dispatch threw after claim; occurrence dropped", schedule.ScheduleId);
            return false;
        }
    }

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
