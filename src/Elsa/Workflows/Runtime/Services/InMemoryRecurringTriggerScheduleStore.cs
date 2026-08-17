using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Process-local <see cref="IRecurringTriggerScheduleStore"/>. Schedules are held in memory only, so a
/// Timer/Cron start trigger backed by this store is <b>not</b> restart-durable — a process restart forgets every
/// schedule until the workflow is republished. Compose a durable persistence provider (the Groundwork bridge)
/// to make recurring schedules survive restarts.
/// </summary>
public sealed class InMemoryRecurringTriggerScheduleStore : IRecurringTriggerScheduleStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, RecurringTriggerSchedule> _schedules = new(StringComparer.Ordinal);
    private readonly HashSet<string> _preparedActivations = new(StringComparer.Ordinal);

    public ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            // Upsert: republish rewrites the schedule (including a re-anchored NextOccurrence), unlike the
            // durable-timer store's existing-wins rule — a recurring schedule has no one-shot deadline to protect.
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
            RemoveByActivation(activationId);
            foreach (var schedule in schedules)
            {
                var prepared = schedule with { IsActive = false };
                _schedules[prepared.ScheduleId] = prepared;
            }
            _preparedActivations.Add(activationId);
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
        {
            if (!_preparedActivations.Contains(activationId))
                throw new InvalidOperationException($"Activation '{activationId}' has no prepared recurring-schedule projection.");

            SetActivationActive(activationId, true);
            if (replacedActivationId is not null && !StringComparer.Ordinal.Equals(replacedActivationId, activationId))
                SetActivationActive(replacedActivationId, false);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            RemoveByActivation(activationId);
            _preparedActivations.Remove(activationId);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
    {
        if (limit is <= 0 or > RuntimeStorePageRequest.MaximumLimit)
            throw new ArgumentOutOfRangeException(nameof(limit), $"Due-schedule listing limit must be between 1 and {RuntimeStorePageRequest.MaximumLimit}.");
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var due = _schedules.Values
                .Where(schedule => schedule.IsActive && schedule.NextOccurrence <= asOf)
                .OrderBy(schedule => schedule.NextOccurrence)
                .ThenBy(schedule => schedule.ScheduleId, StringComparer.Ordinal)
                .Take(limit)
                .ToArray();

            return new ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>>(due);
        }
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

    public ValueTask<bool> TryAdvanceAsync(string scheduleId, DateTimeOffset expectedNextOccurrence, DateTimeOffset newNextOccurrence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            // Compare-and-swap: claim the occurrence only if no other worker (or an earlier sweep) already moved
            // the cursor. This is the single-node realization of the W20 cluster-safe claim contract.
            if (!_schedules.TryGetValue(scheduleId, out var schedule) || !schedule.IsActive || schedule.NextOccurrence != expectedNextOccurrence)
                return new ValueTask<bool>(false);

            _schedules[scheduleId] = schedule with { NextOccurrence = newNextOccurrence };
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
                .Select(schedule => schedule.ScheduleId)
                .ToArray();

            foreach (var scheduleId in doomed)
                _schedules.Remove(scheduleId);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string scheduleId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _schedules.Remove(scheduleId);
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

    private void SetActivationActive(string activationId, bool isActive)
    {
        foreach (var schedule in _schedules.Values
                     .Where(schedule => StringComparer.Ordinal.Equals(schedule.ActivationId, activationId))
                     .ToArray())
            _schedules[schedule.ScheduleId] = schedule with { IsActive = isActive };
    }

    private void RemoveByActivation(string activationId)
    {
        foreach (var scheduleId in _schedules.Values
                     .Where(schedule => StringComparer.Ordinal.Equals(schedule.ActivationId, activationId))
                     .Select(schedule => schedule.ScheduleId)
                     .ToArray())
            _schedules.Remove(scheduleId);
    }

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
