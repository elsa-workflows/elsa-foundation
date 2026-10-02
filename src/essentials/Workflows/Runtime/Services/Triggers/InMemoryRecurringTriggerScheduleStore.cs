using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>
/// Process-local <see cref="IRecurringTriggerScheduleStore"/>. Schedules are held in memory only, so a
/// Timer/Cron start trigger backed by this store is <b>not</b> restart-durable — a process restart forgets every
/// schedule until the workflow is republished. Compose a durable persistence provider (the Runtime EF Core module)
/// to make recurring schedules survive restarts.
/// </summary>
[RuntimeDefaultRegistration]
public sealed class InMemoryRecurringTriggerScheduleStore(TimeProvider? timeProvider = null) : IRecurringTriggerScheduleStore, IInMemoryActivationProjection
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private const string ProjectionName = "recurring-schedule";
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, RecurringTriggerSchedule> _schedules = new(StringComparer.Ordinal);
    private readonly InMemoryActivationProjectionStates _activations = new();

    // Occurrence claims (#2198), kept beside the schedules the way the EF store keeps them in their own columns. A claim
    // is current only while its schedule is still the record it was granted on, so any other change to the schedule
    // fences it out, as a revision bump does in EF.
    private readonly Dictionary<string, ClaimState> _claims = new(StringComparer.Ordinal);
    private long _lastFencingToken;
    private long _lastRevision;

    public ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            // Upsert: republish rewrites the schedule (including a re-anchored NextOccurrence), unlike the
            // durable-timer store's existing-wins rule — a recurring schedule has no one-shot deadline to protect.
            // An overwrite with a different schedule resets its claim, as the EF store's rewrite does.
            if (!_schedules.TryGetValue(schedule.ScheduleId, out var existing) || existing != schedule)
                _claims.Remove(schedule.ScheduleId);
            _schedules[schedule.ScheduleId] = schedule;
            return new ValueTask<RecurringTriggerSchedule>(schedule);
        }
    }

    public ValueTask PrepareActivationAsync(
        string activationId,
        IReadOnlyCollection<RecurringTriggerSchedule> schedules,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        ArgumentNullException.ThrowIfNull(schedules);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateActivationSchedules(activationId, schedules);

        lock (_syncRoot)
        {
            _activations.Prepare(activationId, ProjectionName);
            RemoveByActivation(activationId);
            foreach (var schedule in schedules)
            {
                var prepared = schedule with { IsActive = false };
                _schedules[prepared.ScheduleId] = prepared;
            }
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListByActivationAsync(
        string activationId,
        CancellationToken cancellationToken = default) =>
        await RuntimeOperationalStorePagingExtensions.ListAllByActivationAsync(this, activationId, cancellationToken);

    public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByActivationPageAsync(
        RecurringTriggerScheduleActivationPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var schedules = _schedules.Values
                .Where(schedule => StringComparer.Ordinal.Equals(schedule.ActivationId, query.ActivationId))
                .OrderBy(schedule => schedule.ScheduleId, StringComparer.Ordinal)
                .ToArray();
            return new(CreatePage(query, schedules));
        }
    }

    public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByArtifactPageAsync(
        RecurringTriggerScheduleArtifactPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var schedules = _schedules.Values
                .Where(schedule => StringComparer.Ordinal.Equals(schedule.ArtifactId, query.ArtifactId))
                .OrderBy(schedule => schedule.ScheduleId, StringComparer.Ordinal)
                .ToArray();
            return new(CreatePage(query, schedules));
        }
    }

    public ValueTask ActivateAsync(
        string activationId,
        string? replacedActivationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        if (replacedActivationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(replacedActivationId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
            Switch(activationId, replacedActivationId);

        return ValueTask.CompletedTask;
    }

    object IInMemoryActivationProjection.SyncRoot => _syncRoot;

    bool IInMemoryActivationProjection.Serves(string activationId) => _activations.Serves(activationId);

    void IInMemoryActivationProjection.CheckSwitch(string activationId, string? replacedActivationId) =>
        _activations.CheckActivation(activationId, replacedActivationId, ProjectionName);

    void IInMemoryActivationProjection.Delete(string activationId) => Delete(activationId);

    void IInMemoryActivationProjection.Switch(string activationId, string? replacedActivationId) => Switch(activationId, replacedActivationId);

    private void Switch(string activationId, string? replacedActivationId)
    {
        if (!_activations.Activate(activationId, replacedActivationId, ProjectionName))
            return;

        // The activation switches on here, and only here, so each schedule it activates takes over a due occurrence from
        // the replaced schedule of its trigger, read before the replaced schedules are deactivated; the deactivation then
        // changes them, so a claim in flight on one is stale from then on (#2198).
        var replacing = replacedActivationId is not null && !StringComparer.Ordinal.Equals(replacedActivationId, activationId);
        var replaced = replacing ? SchedulesOf(replacedActivationId!).Where(schedule => schedule.IsActive).ToArray() : [];
        var activatedAt = _timeProvider.GetUtcNow();
        foreach (var schedule in SchedulesOf(activationId))
        {
            var predecessor = replaced.SingleOrDefault(schedule.IsSameTriggerAs);
            _schedules[schedule.ScheduleId] = (predecessor is null ? schedule : schedule.TakeOverFrom(predecessor, activatedAt)) with { IsActive = true };
        }
        if (replacing)
            SetRowsActive(replacedActivationId!, false);
    }

    private void Delete(string activationId)
    {
        RemoveByActivation(activationId);
        _activations.Remove(activationId);
    }

    public ValueTask<WorkflowActivationProjectionState> FindActivationStateAsync(
        string activationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return ValueTask.FromResult(_activations.Find(activationId));
        }
    }

    public ValueTask<IReadOnlyCollection<string>> ListServingActivationIdsAsync(
        string slotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return ValueTask.FromResult<IReadOnlyCollection<string>>(_schedules.Values
                .Where(schedule => schedule.IsActive && schedule.ActivationId is not null && StringComparer.Ordinal.Equals(schedule.SlotId, slotId))
                .Select(schedule => schedule.ActivationId!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
        }
    }

    public ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
            Delete(activationId);

        return ValueTask.CompletedTask;
    }

    public ValueTask<RecurringTriggerSchedule?> FindAsync(string scheduleId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return new ValueTask<RecurringTriggerSchedule?>(
                _schedules.TryGetValue(scheduleId, out var schedule) ? schedule : null);
        }
    }

    public ValueTask<IReadOnlyCollection<RecurringTriggerOccurrenceClaim>> ClaimDueAsync(
        RecurringTriggerOccurrenceClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var claimable = _schedules.Values
                .Where(schedule => schedule.IsActive && schedule.NextOccurrence <= request.Now && IsVisible(schedule.ScheduleId, request.Now))
                .OrderBy(schedule => schedule.NextOccurrence)
                .ThenBy(schedule => schedule.ScheduleId, StringComparer.Ordinal)
                .Take(request.Limit)
                .ToArray();
            var claims = new List<RecurringTriggerOccurrenceClaim>(claimable.Length);
            foreach (var schedule in claimable)
            {
                var failureCount = _claims.TryGetValue(schedule.ScheduleId, out var previous) ? previous.FailureCount : 0;
                var state = new ClaimState(
                    request.OwnerId, ++_lastFencingToken, ++_lastRevision, request.Now, request.Now.Add(request.VisibilityTimeout), failureCount);
                _claims[schedule.ScheduleId] = state;
                claims.Add(ToClaim(schedule, state));
            }

            return new ValueTask<IReadOnlyCollection<RecurringTriggerOccurrenceClaim>>(claims);
        }
    }

    public ValueTask<RecurringTriggerOccurrenceClaim?> RenewClaimAsync(
        RecurringTriggerOccurrenceClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "A recurring-trigger claim visibility timeout must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!TryGetCurrent(claim, out var state))
                return new ValueTask<RecurringTriggerOccurrenceClaim?>((RecurringTriggerOccurrenceClaim?)null);

            var renewed = state with { Revision = ++_lastRevision, VisibleAfter = now.Add(visibilityTimeout) };
            _claims[claim.Schedule.ScheduleId] = renewed;
            return new ValueTask<RecurringTriggerOccurrenceClaim?>(ToClaim(claim.Schedule, renewed));
        }
    }

    public ValueTask<bool> SettleClaimAsync(
        RecurringTriggerOccurrenceClaim claim,
        DateTimeOffset nextOccurrence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!TryGetCurrent(claim, out _))
                return new ValueTask<bool>(false);

            _schedules[claim.Schedule.ScheduleId] = claim.Schedule with { NextOccurrence = nextOccurrence };
            _claims.Remove(claim.Schedule.ScheduleId);
            return new ValueTask<bool>(true);
        }
    }

    public ValueTask<bool> ReleaseClaimAsync(
        RecurringTriggerOccurrenceClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!TryGetCurrent(claim, out var state))
                return new ValueTask<bool>(false);

            _claims[claim.Schedule.ScheduleId] = state with
            {
                OwnerId = null,
                Revision = ++_lastRevision,
                ClaimedAt = null,
                VisibleAfter = visibleAt,
                FailureCount = checked(state.FailureCount + 1)
            };
            return new ValueTask<bool>(true);
        }
    }

    public ValueTask DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var doomed = _schedules.Values
                .Where(schedule => StringComparer.Ordinal.Equals(schedule.ArtifactId, artifactId))
                .ToArray();

            foreach (var schedule in doomed)
                Remove(schedule.ScheduleId);
            foreach (var activationId in doomed.Select(schedule => schedule.ActivationId).OfType<string>().Distinct(StringComparer.Ordinal))
                _activations.Remove(activationId);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string scheduleId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            Remove(scheduleId);
        }

        return ValueTask.CompletedTask;
    }

    private static void ValidateActivationSchedules(
        string activationId,
        IReadOnlyCollection<RecurringTriggerSchedule> schedules)
    {
        foreach (var schedule in schedules)
        {
            ArgumentNullException.ThrowIfNull(schedule);
            if (!StringComparer.Ordinal.Equals(schedule.ActivationId, activationId))
                throw new ArgumentException($"Schedule '{schedule.ScheduleId}' does not belong to activation '{activationId}'.", nameof(schedules));
            ArgumentException.ThrowIfNullOrWhiteSpace(schedule.SlotId);
        }
    }

    private void SetRowsActive(string activationId, bool isActive)
    {
        foreach (var schedule in SchedulesOf(activationId))
            _schedules[schedule.ScheduleId] = schedule with { IsActive = isActive };
    }

    private RecurringTriggerSchedule[] SchedulesOf(string activationId) =>
        _schedules.Values.Where(schedule => StringComparer.Ordinal.Equals(schedule.ActivationId, activationId)).ToArray();

    private void RemoveByActivation(string activationId)
    {
        foreach (var schedule in SchedulesOf(activationId))
            Remove(schedule.ScheduleId);
    }

    private void Remove(string scheduleId)
    {
        _schedules.Remove(scheduleId);
        _claims.Remove(scheduleId);
    }

    private bool IsVisible(string scheduleId, DateTimeOffset now) =>
        !_claims.TryGetValue(scheduleId, out var state) || state.VisibleAfter is not { } visibleAfter || visibleAfter <= now;

    private bool TryGetCurrent(RecurringTriggerOccurrenceClaim claim, out ClaimState state) =>
        _claims.TryGetValue(claim.Schedule.ScheduleId, out state!) &&
        StringComparer.Ordinal.Equals(state.OwnerId, claim.OwnerId) &&
        state.FencingToken == claim.FencingToken &&
        state.Revision == claim.Revision &&
        _schedules.TryGetValue(claim.Schedule.ScheduleId, out var current) &&
        current == claim.Schedule;

    private static RecurringTriggerOccurrenceClaim ToClaim(RecurringTriggerSchedule schedule, ClaimState state) =>
        new(schedule, state.OwnerId!, state.FencingToken, state.Revision, state.ClaimedAt!.Value, state.VisibleAfter!.Value, state.FailureCount);

    private sealed record ClaimState(
        string? OwnerId,
        long FencingToken,
        long Revision,
        DateTimeOffset? ClaimedAt,
        DateTimeOffset? VisibleAfter,
        int FailureCount);

    private static RuntimeStorePage<RecurringTriggerSchedule> CreatePage(
        RuntimeStorePageRequest query,
        IReadOnlyList<RecurringTriggerSchedule> schedules)
    {
        var offset = ParseOffset(query.ContinuationToken);
        var items = schedules.Skip(offset).Take(query.Limit).ToArray();
        var nextOffset = checked(offset + items.Length);
        return new(query, items, nextOffset < schedules.Count ? nextOffset.ToString() : null);
    }

    private static int ParseOffset(string? continuationToken)
    {
        if (continuationToken is null)
            return 0;
        if (!int.TryParse(continuationToken, out var offset) || offset < 0)
            throw new ArgumentException("The recurring-schedule page continuation is invalid.", nameof(continuationToken));
        return offset;
    }
}
