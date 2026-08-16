using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class RuntimeRecurringScheduleSelectionWorkloadTests
{
    [Fact]
    public async Task Reproduces_the_exact_catalog_golden_through_independent_shared_backing_clients()
    {
        var adapter = new AtomicRecurringScheduleAdapter();
        var result = await new RuntimeRecurringScheduleSelectionWorkload().ExecuteAsync(adapter);

        Assert.Equal(RuntimeRecurringScheduleSelectionWorkload.ExpectedInputFingerprint, result.InputFingerprint);
        Assert.Equal(RuntimeRecurringScheduleSelectionWorkload.ExpectedResultDigest, result.ResultDigest);
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.Get(RuntimeRecurringScheduleSelectionWorkload.WorkloadId).OperationSequence, result.ObservableOperations);
        Assert.Equal("schedule-due-0000", result.ObservableResults["advancedScheduleId"]);
        Assert.Equal(50, result.ObservableResults["firstPageCount"]);
        Assert.Equal(0, result.ObservableResults["inactivePublicationResultCount"]);
        Assert.Equal(50, result.ObservableResults["loadedProjectionCount"]);
        Assert.Equal(true, result.ObservableResults["reopenedProjectionMatched"]);
        Assert.Equal(true, result.ObservableResults["staleAdvanceRejected"]);
        Assert.Equal(3, adapter.OpenedClients.Count);
        Assert.Equal(3, adapter.OpenedClients.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(RuntimeRecurringScheduleSelectionWorkload.ScheduleCount, adapter.Shared.Schedules.Count);
        Assert.Equal(RuntimeRecurringScheduleSelectionWorkload.PublicationCount, adapter.Shared.PreparedPublications.Count);
        Assert.Equal(
            RuntimeRecurringScheduleSelectionWorkload.InactivePublications,
            adapter.Shared.Schedules.Values
                .Where(schedule => !schedule.IsActive)
                .Select(schedule => schedule.PublicationId)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(
            adapter.Shared.Schedules.Values,
            schedule => Assert.Equal(!IsExpectedInactivePublication(schedule.PublicationId), schedule.IsActive));
        Assert.Equal(
            RuntimeRecurringScheduleSelectionWorkload.DueSchedules - 1,
            adapter.Shared.Schedules.Values.Count(schedule =>
                schedule.IsActive &&
                schedule.NextOccurrence <= RuntimeRecurringScheduleSelectionWorkload.FixedNowUtc));
        Assert.Equal(
            (RuntimeRecurringScheduleSelectionWorkload.PublicationCount + 1) * 2,
            adapter.Shared.PublicationPageRequests.Count);
        Assert.All(adapter.Shared.PublicationPageRequests, request =>
        {
            Assert.Equal(RuntimeRecurringScheduleSelectionWorkload.PageSize, request.Limit);
        });
        var requestsByPublication = adapter.Shared.PublicationPageRequests
            .GroupBy(request => request.PublicationId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        Assert.Equal(RuntimeRecurringScheduleSelectionWorkload.PublicationCount, requestsByPublication.Count);
        Assert.Equal(4, requestsByPublication["publication-0000"].Length);
        Assert.Equal(2, requestsByPublication["publication-0000"].Count(request => request.ContinuationToken is null));
        Assert.Equal(2, requestsByPublication["publication-0000"].Count(request => request.ContinuationToken is not null));
        Assert.All(
            requestsByPublication.Where(pair => pair.Key != "publication-0000"),
            pair => Assert.Equal(2, pair.Value.Length));
    }

    [Fact]
    public async Task Performs_the_frozen_public_operation_order()
    {
        var result = await new RuntimeRecurringScheduleSelectionWorkload().ExecuteAsync(new AtomicRecurringScheduleAdapter());

        Assert.Equal(
            [
                "seed-publications-and-schedules",
                "list-bounded-due-schedules",
                "load-publication-projections",
                "advance-current-schedule",
                "attempt-stale-advance",
                "reopen-and-read-projection-state"
            ],
            result.ObservableOperations);
    }

    [Theory]
    [InlineData(RecurringScheduleAdapterFault.AliasInitialClients)]
    [InlineData(RecurringScheduleAdapterFault.SeparateInitialBacking)]
    [InlineData(RecurringScheduleAdapterFault.ReopenSameClient)]
    [InlineData(RecurringScheduleAdapterFault.ReopenSeparateBacking)]
    public async Task Rejects_clients_that_are_not_independent_or_visible_through_a_reopened_client(RecurringScheduleAdapterFault fault)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeRecurringScheduleSelectionWorkload().ExecuteAsync(new AtomicRecurringScheduleAdapter(fault)).AsTask());
    }

    [Theory]
    [InlineData(RecurringScheduleAdapterFault.IgnoreActivation)]
    [InlineData(RecurringScheduleAdapterFault.IgnoreActiveFilter)]
    [InlineData(RecurringScheduleAdapterFault.IgnoreDueCutoff)]
    [InlineData(RecurringScheduleAdapterFault.IgnoreDueLimit)]
    [InlineData(RecurringScheduleAdapterFault.ReverseDueOrder)]
    [InlineData(RecurringScheduleAdapterFault.WrongDueRecord)]
    [InlineData(RecurringScheduleAdapterFault.IgnorePublicationFilter)]
    [InlineData(RecurringScheduleAdapterFault.MissingPublicationContinuation)]
    [InlineData(RecurringScheduleAdapterFault.IgnorePublicationContinuation)]
    [InlineData(RecurringScheduleAdapterFault.WrongProjectionRecord)]
    [InlineData(RecurringScheduleAdapterFault.IgnoreReplacement)]
    [InlineData(RecurringScheduleAdapterFault.DeactivateUnrelatedPublication)]
    [InlineData(RecurringScheduleAdapterFault.NonAtomicDualSuccess)]
    [InlineData(RecurringScheduleAdapterFault.ResponseOnlyAdvance)]
    [InlineData(RecurringScheduleAdapterFault.IgnoreExpectedOccurrence)]
    [InlineData(RecurringScheduleAdapterFault.WrongFindRecord)]
    public async Task Fails_closed_when_public_store_outcomes_drift(RecurringScheduleAdapterFault fault)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeRecurringScheduleSelectionWorkload().ExecuteAsync(new AtomicRecurringScheduleAdapter(fault)).AsTask());
    }

    [Fact]
    public void Public_adapter_surface_contains_no_provider_or_timing_inputs()
    {
        var types = new[]
        {
            typeof(IRuntimeRecurringScheduleSelectionWorkloadAdapter),
            typeof(RuntimeRecurringScheduleSelectionClients)
        };
        var publicSurface = types
            .SelectMany(type => type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            .Select(member => member.ToString()!)
            .Append(typeof(IRuntimeRecurringScheduleSelectionWorkloadAdapter).ToString());

        Assert.DoesNotContain(publicSurface, value =>
            value.Contains("Provider", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("DocumentStore", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Raw", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Timing", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Matrix", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Ledger", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Manifest", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Physical", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExpectedInactivePublication(string? publicationId)
    {
        Assert.NotNull(publicationId);
        var index = int.Parse(
            publicationId.AsSpan("publication-".Length),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture);
        var replacementCandidate = RuntimeRecurringScheduleSelectionWorkload.PublicationCount -
                                   RuntimeRecurringScheduleSelectionWorkload.InactivePublications;
        return index == replacementCandidate - 1 || index > replacementCandidate;
    }

    private sealed class AtomicRecurringScheduleAdapter(RecurringScheduleAdapterFault fault = RecurringScheduleAdapterFault.None) : IRuntimeRecurringScheduleSelectionWorkloadAdapter
    {
        private readonly RecurringScheduleBacking _secondaryBacking = fault == RecurringScheduleAdapterFault.SeparateInitialBacking ? new() : null!;
        public RecurringScheduleBacking Shared { get; } = new();
        public List<AtomicRecurringScheduleStore> OpenedClients { get; } = [];

        public ValueTask<RuntimeRecurringScheduleSelectionClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primary = Open(Shared);
            var secondary = fault == RecurringScheduleAdapterFault.AliasInitialClients
                ? primary
                : Open(fault == RecurringScheduleAdapterFault.SeparateInitialBacking ? _secondaryBacking : Shared);
            return new(new RuntimeRecurringScheduleSelectionClients(primary, secondary));
        }

        public ValueTask<IRecurringTriggerScheduleStore> ReopenClientAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fault == RecurringScheduleAdapterFault.ReopenSameClient)
                return new(OpenedClients[0]);
            return new(Open(fault == RecurringScheduleAdapterFault.ReopenSeparateBacking ? new RecurringScheduleBacking() : Shared));
        }

        private AtomicRecurringScheduleStore Open(RecurringScheduleBacking backing)
        {
            var client = new AtomicRecurringScheduleStore(backing, fault);
            OpenedClients.Add(client);
            return client;
        }
    }

    private sealed class RecurringScheduleBacking
    {
        public object Sync { get; } = new();
        public Dictionary<string, RecurringTriggerSchedule> Schedules { get; } = new(StringComparer.Ordinal);
        public HashSet<string> PreparedPublications { get; } = new(StringComparer.Ordinal);
        public List<RecurringTriggerSchedulePublicationPageQuery> PublicationPageRequests { get; } = [];
        public int NonAtomicReadyCount { get; set; }
        public TaskCompletionSource NonAtomicRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ResponseOnlyGranted { get; set; }
    }

    private sealed class AtomicRecurringScheduleStore(RecurringScheduleBacking backing, RecurringScheduleAdapterFault fault) : IRecurringTriggerScheduleStore
    {
        public ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(schedule);
            cancellationToken.ThrowIfCancellationRequested();
            lock (backing.Sync)
            {
                backing.Schedules[schedule.ScheduleId] = schedule;
                return new(schedule);
            }
        }

        public ValueTask PrepareActivationAsync(string publicationId, IReadOnlyCollection<RecurringTriggerSchedule> schedules, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (backing.Sync)
            {
                foreach (var existing in backing.Schedules.Values.Where(schedule => schedule.PublicationId == publicationId).Select(schedule => schedule.ScheduleId).ToArray())
                    backing.Schedules.Remove(existing);
                foreach (var schedule in schedules)
                    backing.Schedules.Add(schedule.ScheduleId, schedule with { IsActive = false });
                backing.PreparedPublications.Add(publicationId);
                return ValueTask.CompletedTask;
            }
        }

        public ValueTask ActivateAsync(string publicationId, string? replacedPublicationId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (backing.Sync)
            {
                if (!backing.PreparedPublications.Contains(publicationId))
                    throw new InvalidOperationException("Publication was not prepared.");
                if (fault == RecurringScheduleAdapterFault.IgnoreActivation)
                    return ValueTask.CompletedTask;
                SetActive(publicationId, true);
                if (replacedPublicationId is not null && fault != RecurringScheduleAdapterFault.IgnoreReplacement)
                    SetActive(replacedPublicationId, false);
                if (replacedPublicationId is not null && fault == RecurringScheduleAdapterFault.DeactivateUnrelatedPublication)
                    SetActive("publication-0001", false);
                return ValueTask.CompletedTask;
            }
        }

        public ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (backing.Sync)
            {
                IEnumerable<RecurringTriggerSchedule> schedules = backing.Schedules.Values
                    .Where(schedule =>
                        (fault == RecurringScheduleAdapterFault.IgnoreActiveFilter || schedule.IsActive) &&
                        (fault == RecurringScheduleAdapterFault.IgnoreDueCutoff || schedule.NextOccurrence <= asOf))
                    .OrderBy(schedule => schedule.NextOccurrence)
                    .ThenBy(schedule => schedule.ScheduleId, StringComparer.Ordinal);
                if (fault == RecurringScheduleAdapterFault.ReverseDueOrder)
                    schedules = schedules.Reverse();
                var result = schedules
                    .Take(fault == RecurringScheduleAdapterFault.IgnoreDueLimit ? int.MaxValue : limit)
                    .Select(Copy)
                    .ToArray();
                if (fault == RecurringScheduleAdapterFault.WrongDueRecord && result.Length > 0)
                    result[0] = result[0] with { Expression = "PT999M" };
                return new(result);
            }
        }

        public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByPublicationPageAsync(RecurringTriggerSchedulePublicationPageQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (backing.Sync)
            {
                backing.PublicationPageRequests.Add(query);
                var allSchedules = backing.Schedules.Values
                    .Where(schedule => fault == RecurringScheduleAdapterFault.IgnorePublicationFilter || schedule.PublicationId == query.PublicationId)
                    .OrderBy(schedule => schedule.ScheduleId, StringComparer.Ordinal)
                    .ToArray();
                var offset = query.ContinuationToken is null || fault == RecurringScheduleAdapterFault.IgnorePublicationContinuation
                    ? 0
                    : ParseContinuation(query.ContinuationToken);
                var schedules = allSchedules
                    .Skip(offset)
                    .Take(query.Limit)
                    .Select(Copy)
                    .ToArray();
                if (fault == RecurringScheduleAdapterFault.WrongProjectionRecord && schedules.Length > 0)
                    schedules[0] = schedules[0] with { StimulusHash = "sha256:wrong" };
                var nextOffset = fault == RecurringScheduleAdapterFault.IgnorePublicationContinuation && query.ContinuationToken is not null
                    ? query.Limit * 2
                    : offset + schedules.Length;
                var continuation = nextOffset < allSchedules.Length && fault != RecurringScheduleAdapterFault.MissingPublicationContinuation
                    ? $"offset:{nextOffset}"
                    : null;
                return new(new RuntimeStorePage<RecurringTriggerSchedule>(query, schedules, continuation));
            }
        }

        public ValueTask<RecurringTriggerSchedule?> FindAsync(string scheduleId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (backing.Sync)
            {
                if (!backing.Schedules.TryGetValue(scheduleId, out var schedule))
                    return new((RecurringTriggerSchedule?)null);
                return new(fault == RecurringScheduleAdapterFault.WrongFindRecord ? schedule with { Expression = "PT999M" } : Copy(schedule));
            }
        }

        public ValueTask<bool> TryAdvanceAsync(string scheduleId, DateTimeOffset expectedNextOccurrence, DateTimeOffset newNextOccurrence, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fault == RecurringScheduleAdapterFault.NonAtomicDualSuccess)
                return SimulateNonAtomicAdvanceAsync(scheduleId, expectedNextOccurrence, newNextOccurrence, cancellationToken);
            lock (backing.Sync)
            {
                if (!backing.Schedules.TryGetValue(scheduleId, out var schedule) || !schedule.IsActive ||
                    (fault != RecurringScheduleAdapterFault.IgnoreExpectedOccurrence && schedule.NextOccurrence != expectedNextOccurrence))
                    return new(false);
                if (fault == RecurringScheduleAdapterFault.ResponseOnlyAdvance)
                    return new(++backing.ResponseOnlyGranted == 1);
                backing.Schedules[scheduleId] = schedule with { NextOccurrence = newNextOccurrence };
                return new(true);
            }
        }

        public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByArtifactPageAsync(RecurringTriggerScheduleArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The recurring schedule workload does not use artifact pages.");

        public ValueTask DeleteByActivationAsync(string publicationId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DeleteAsync(string scheduleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        private async ValueTask<bool> SimulateNonAtomicAdvanceAsync(string scheduleId, DateTimeOffset expectedNextOccurrence, DateTimeOffset newNextOccurrence, CancellationToken cancellationToken)
        {
            lock (backing.Sync)
            {
                if (!backing.Schedules.TryGetValue(scheduleId, out var schedule) || !schedule.IsActive || schedule.NextOccurrence != expectedNextOccurrence)
                    return false;
                if (++backing.NonAtomicReadyCount == RuntimeRecurringScheduleSelectionWorkload.ConcurrentAdvancers)
                    backing.NonAtomicRelease.TrySetResult();
            }
            await backing.NonAtomicRelease.Task.WaitAsync(cancellationToken);
            lock (backing.Sync)
            {
                var current = backing.Schedules[scheduleId];
                backing.Schedules[scheduleId] = current with { NextOccurrence = newNextOccurrence };
                return true;
            }
        }

        private void SetActive(string publicationId, bool active)
        {
            foreach (var schedule in backing.Schedules.Values.Where(schedule => schedule.PublicationId == publicationId).ToArray())
                backing.Schedules[schedule.ScheduleId] = schedule with { IsActive = active };
        }

        private static int ParseContinuation(string continuationToken)
        {
            if (!continuationToken.StartsWith("offset:", StringComparison.Ordinal) ||
                !int.TryParse(
                    continuationToken.AsSpan("offset:".Length),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var offset))
            {
                throw new InvalidOperationException("The test adapter received an invalid continuation.");
            }

            return offset;
        }

        private static RecurringTriggerSchedule Copy(RecurringTriggerSchedule schedule) => schedule with { };
    }
}

public enum RecurringScheduleAdapterFault
{
    None,
    AliasInitialClients,
    SeparateInitialBacking,
    ReopenSameClient,
    ReopenSeparateBacking,
    IgnoreActivation,
    IgnoreActiveFilter,
    IgnoreDueCutoff,
    IgnoreDueLimit,
    ReverseDueOrder,
    WrongDueRecord,
    IgnorePublicationFilter,
    MissingPublicationContinuation,
    IgnorePublicationContinuation,
    WrongProjectionRecord,
    IgnoreReplacement,
    DeactivateUnrelatedPublication,
    NonAtomicDualSuccess,
    ResponseOnlyAdvance,
    IgnoreExpectedOccurrence,
    WrongFindRecord
}
