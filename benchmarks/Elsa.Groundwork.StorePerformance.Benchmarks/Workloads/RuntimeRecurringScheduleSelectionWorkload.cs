using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>
/// Executes the catalog-owned recurring-schedule selection correctness baseline through
/// <see cref="IRecurringTriggerScheduleStore"/> only. It proves public-contract observations over
/// independently created clients with shared backing; provider, restart, native-route, and timing evidence
/// remain #646 responsibilities.
/// </summary>
public sealed class RuntimeRecurringScheduleSelectionWorkload
{
    private const string AdvancedScheduleId = "schedule-due-0000";
    private static readonly ReproducibleWorkloadScenario Scenario = ReproducibleWorkloadScenarioCatalog.Get(WorkloadId);
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public const string WorkloadId = "recurring-schedule-selection";
    public const string ExpectedInputFingerprint = "384bcbf0fd72f306b63d78b71a8130c4e2e02de146cbd45d066ef581f4d78d17";
    public const string ExpectedResultDigest = "9728bad4f576c7e50c3f6210994524ffb1d77761c5258a71f27fe1cf1793cec4";
    public static string ScenarioId => Scenario.ScenarioId;
    public static string Version => Scenario.Version;
    public static string Seed => Scenario.Seed;
    public static int PublicationCount => Int("publicationCount");
    public static int ScheduleCount => Int("scheduleCount");
    public static int DueSchedules => Int("dueSchedules");
    public static int PageSize => Int("pageSize");
    public static int InactivePublications => Int("inactivePublications");
    public static int ConcurrentAdvancers => Int("concurrentAdvancers");
    public static DateTimeOffset FixedNowUtc => DateTimeOffset.Parse(String("fixedNowUtc"), System.Globalization.CultureInfo.InvariantCulture);

    public async ValueTask<RuntimeRecurringScheduleSelectionResult> ExecuteAsync(
        IRuntimeRecurringScheduleSelectionWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        RequireIndependentClients(clients);

        var operations = new List<string>();
        var dueAt = FixedNowUtc;
        var advancedTo = FixedNowUtc.AddHours(1);
        await SeedAsync(clients.Primary, dueAt, cancellationToken);
        operations.Add("seed-publications-and-schedules");

        RequireAdvancedSchedule(await clients.Secondary.FindAsync(AdvancedScheduleId, cancellationToken), AdvancedScheduleId, dueAt, "independent seeded-client lookup");
        var beforeCutoff = await clients.Primary.ListDueAsync(FixedNowUtc.AddTicks(-1), 1, cancellationToken);
        if (beforeCutoff.Count != 0)
            throw new InvalidOperationException("The recurring-schedule due cutoff returned a schedule before its due instant.");
        var dueFirstPage = await clients.Primary.ListDueAsync(FixedNowUtc, PageSize, cancellationToken);
        RequireDueSchedules(dueFirstPage, ExpectedDueSchedules(dueAt).Take(PageSize), "first bounded due page");
        var allDue = await clients.Primary.ListDueAsync(FixedNowUtc, DueSchedules, cancellationToken);
        RequireDueSchedules(allDue, ExpectedDueSchedules(dueAt), "full finite due page");
        operations.Add("list-bounded-due-schedules");

        var projections = await clients.Primary.ListByPublicationPageAsync(
            new RecurringTriggerSchedulePublicationPageQuery(PublicationId(0), PageSize),
            cancellationToken);
        RequireProjectionPage(projections, "publication projection page");
        operations.Add("load-publication-projections");

        await RequireSingleAdvanceWinnerAsync(clients, AdvancedScheduleId, dueAt, advancedTo, cancellationToken);
        operations.Add("advance-current-schedule");

        if (await clients.Primary.TryAdvanceAsync(AdvancedScheduleId, dueAt, FixedNowUtc.AddHours(2), cancellationToken))
            throw new InvalidOperationException("The recurring-schedule workload accepted a stale advance.");
        RequireAdvancedSchedule(await clients.Primary.FindAsync(AdvancedScheduleId, cancellationToken), AdvancedScheduleId, advancedTo, "post-stale-retry lookup");
        operations.Add("attempt-stale-advance");

        var reopened = await adapter.ReopenClientAsync(cancellationToken);
        if (reopened is null || ReferenceEquals(reopened, clients.Primary) || ReferenceEquals(reopened, clients.Secondary))
            throw new InvalidOperationException("The recurring-schedule workload adapter must reopen a genuinely separate public-store client.");
        var reopenedTarget = await reopened.FindAsync(AdvancedScheduleId, cancellationToken);
        RequireAdvancedSchedule(reopenedTarget, AdvancedScheduleId, advancedTo, "reopened schedule lookup");
        var reopenedDue = await reopened.ListDueAsync(FixedNowUtc, DueSchedules, cancellationToken);
        RequireDueSchedules(reopenedDue, ExpectedDueSchedules(dueAt).Skip(1), "reopened due schedules");
        var reopenedProjection = await reopened.ListByPublicationPageAsync(
            new RecurringTriggerSchedulePublicationPageQuery(PublicationId(0), PageSize),
            cancellationToken);
        RequireProjectionPage(reopenedProjection, "reopened publication projection page");
        operations.Add("reopen-and-read-projection-state");

        if (!operations.SequenceEqual(scenario.OperationSequence, StringComparer.Ordinal))
            throw new InvalidOperationException("The recurring-schedule workload operation order no longer matches the catalog contract.");

        var actualObservations = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["advancedScheduleId"] = AdvancedScheduleId,
            ["dueScheduleIdentityDigest"] = Hash(JsonSerializer.Serialize(DueScheduleIds(), CanonicalJsonOptions)),
            ["firstPageCount"] = dueFirstPage.Count,
            ["inactivePublicationResultCount"] = allDue.Count(schedule => IsInactivePublication(schedule.PublicationId)),
            ["loadedProjectionCount"] = projections.Items.Count,
            ["reopenedProjectionMatched"] = reopenedTarget!.NextOccurrence == advancedTo &&
                                           reopenedDue.Count == DueSchedules - 1 &&
                                           reopenedProjection.Items.Count == PageSize,
            ["staleAdvanceRejected"] = true
        };
        var expectedObservations = scenario.CreateExpectedObservations();
        if (!ObservationsMatch(actualObservations, expectedObservations))
            throw new InvalidOperationException("The recurring-schedule observable results no longer match the catalog contract.");

        var resultDigest = ReproducibleWorkloadScenarioCatalog.Hash(ReproducibleWorkloadScenarioCatalog.Serialize(new
        {
            WorkloadId,
            scenario.ScenarioId,
            InputFingerprint = scenario.ComputeInputFingerprint(),
            Operations = operations,
            ObservableResults = actualObservations
        }));
        if (resultDigest != ExpectedResultDigest)
            throw new InvalidOperationException("The recurring-schedule observable result digest no longer matches its ratified value.");

        return new RuntimeRecurringScheduleSelectionResult(scenario.ComputeInputFingerprint(), resultDigest, operations, actualObservations);
    }

    private static ReproducibleWorkloadScenario ValidateScenario()
    {
        if (Scenario.Version != "1.1.0" ||
            Scenario.ScenarioId != "runtime-recurring-schedule-selection" ||
            Scenario.Seed != "spec094-recurring-schedule-selection-v1.1" ||
            Scenario.ComputeInputFingerprint() != ExpectedInputFingerprint ||
            Scenario.ComputeResultDigest() != ExpectedResultDigest ||
            !ReproducibleWorkloadScenarioCatalog.GoldenVectors.TryGetValue(WorkloadId, out var golden) ||
            golden.InputFingerprint != ExpectedInputFingerprint ||
            golden.ResultDigest != ExpectedResultDigest ||
            PublicationCount != 256 || ScheduleCount != 2048 || DueSchedules != 179 || PageSize != 50 ||
            InactivePublications != 41 || ConcurrentAdvancers != 2 ||
            FixedNowUtc != new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero))
            throw new InvalidOperationException("The recurring-schedule scenario no longer matches its frozen v1.1 catalog contract.");

        return Scenario;
    }

    private static async ValueTask SeedAsync(IRecurringTriggerScheduleStore store, DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        for (var publicationIndex = 0; publicationIndex < PublicationCount; publicationIndex++)
        {
            var publicationId = PublicationId(publicationIndex);
            var schedules = SchedulesForPublication(publicationIndex, dueAt).ToArray();
            await store.PreparePublicationAsync(publicationId, schedules, cancellationToken);
            if (!IsInactivePublication(publicationId))
                await store.ActivatePublicationAsync(publicationId, replacedPublicationId: null, cancellationToken);
        }
    }

    private static IEnumerable<RecurringTriggerSchedule> SchedulesForPublication(int publicationIndex, DateTimeOffset dueAt)
    {
        var count = publicationIndex == 0 ? 64 : publicationIndex < 248 ? 8 : 1;
        var publicationId = PublicationId(publicationIndex);
        var globalOffset = publicationIndex == 0 ? 0 : (publicationIndex - 1) * 8;
        if (publicationIndex >= 248)
            globalOffset = 1976 + publicationIndex - 248;

        for (var index = 0; index < count; index++)
        {
            var globalIndex = globalOffset + index;
            var isProjection = publicationIndex == 0;
            var isDue = !isProjection && globalIndex < DueSchedules;
            var isInactive = IsInactivePublication(publicationId);
            yield return new RecurringTriggerSchedule(
                ScheduleId: isProjection ? $"schedule-projection-{globalIndex:D4}" : isDue ? $"schedule-due-{globalIndex:D4}" : isInactive ? $"schedule-inactive-{globalIndex:D4}" : $"schedule-future-{globalIndex:D4}",
                ArtifactId: $"artifact-{publicationIndex:D4}",
                ExecutableNodeId: $"node-{globalIndex:D4}",
                StimulusType: "Timer",
                StimulusHash: $"sha256:recurring-{globalIndex:D4}",
                Kind: RecurringScheduleKind.Interval,
                Expression: "PT5M",
                NextOccurrence: isDue ? dueAt : isInactive ? FixedNowUtc.AddMinutes(-1) : FixedNowUtc.AddDays(1),
                CreatedAt: FixedNowUtc.AddDays(-1),
                PublicationId: publicationId,
                SlotId: $"slot-{globalIndex:D4}");
        }
    }

    private static async ValueTask RequireSingleAdvanceWinnerAsync(
        RuntimeRecurringScheduleSelectionClients clients,
        string scheduleId,
        DateTimeOffset expectedOccurrence,
        DateTimeOffset advancedOccurrence,
        CancellationToken cancellationToken)
    {
        var contenders = new[] { clients.Primary, clients.Secondary };
        if (contenders.Length != ConcurrentAdvancers)
            throw new InvalidOperationException("The recurring-schedule contention wave no longer matches the catalog advancer count.");

        var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;
        var attempts = contenders.Select(async store =>
        {
            if (Interlocked.Increment(ref readyCount) == ConcurrentAdvancers)
                allReady.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await store.TryAdvanceAsync(scheduleId, expectedOccurrence, advancedOccurrence, cancellationToken);
        }).ToArray();

        await allReady.Task.WaitAsync(cancellationToken);
        release.TrySetResult();
        var outcomes = await Task.WhenAll(attempts);
        if (outcomes.Count(outcome => outcome) != 1 || outcomes.Count(outcome => !outcome) != ConcurrentAdvancers - 1)
            throw new InvalidOperationException("The recurring-schedule contention wave did not converge to one winning advance and one rejected advance.");

        var persisted = await clients.Primary.FindAsync(scheduleId, cancellationToken);
        RequireAdvancedSchedule(persisted, scheduleId, advancedOccurrence, "persisted contention winner");
        RequireAdvancedSchedule(await clients.Secondary.FindAsync(scheduleId, cancellationToken), scheduleId, advancedOccurrence, "secondary contention-winner lookup");
    }

    private static void RequireIndependentClients(RuntimeRecurringScheduleSelectionClients? clients)
    {
        if (clients is null || clients.Primary is null || clients.Secondary is null || ReferenceEquals(clients.Primary, clients.Secondary))
            throw new InvalidOperationException("The recurring-schedule workload adapter must open two independent public-store clients over shared backing.");
    }

    private static void RequireDueSchedules(
        IReadOnlyCollection<RecurringTriggerSchedule>? schedules,
        IEnumerable<RecurringTriggerSchedule> expectedSchedules,
        string operation)
    {
        var expected = expectedSchedules.ToArray();
        if (schedules is null || schedules.Count != expected.Length ||
            !schedules.SequenceEqual(expected, RecurringScheduleComparer.Instance) ||
            schedules.Any(schedule => !schedule.IsActive || schedule.NextOccurrence > FixedNowUtc || IsInactivePublication(schedule.PublicationId)))
            throw new InvalidOperationException($"The {operation} does not match the exact active, due, ordered recurring-schedule contract.");
    }

    private static void RequireProjectionPage(RuntimeStorePage<RecurringTriggerSchedule>? page, string operation)
    {
        if (page is null || string.IsNullOrWhiteSpace(page.NextContinuationToken) || page.Items.Count != PageSize ||
            page.Items.Any(schedule => schedule.PublicationId != PublicationId(0) || !schedule.IsActive) ||
            !page.Items.SequenceEqual(ExpectedProjectionSchedules().Take(PageSize), RecurringScheduleComparer.Instance))
            throw new InvalidOperationException($"The {operation} does not match the requested active publication projection state.");
    }

    private static void RequireAdvancedSchedule(RecurringTriggerSchedule? schedule, string expectedId, DateTimeOffset expectedNextOccurrence, string operation)
    {
        var expected = ExpectedDueSchedules(FixedNowUtc).Single(candidate => candidate.ScheduleId == expectedId) with
        {
            NextOccurrence = expectedNextOccurrence
        };
        if (schedule is null || !RecurringScheduleComparer.Instance.Equals(schedule, expected))
            throw new InvalidOperationException($"The {operation} did not preserve the winning recurring-schedule transition.");
    }

    private static IEnumerable<string> DueScheduleIds()
    {
        for (var index = 0; index < DueSchedules; index++)
            yield return $"schedule-due-{index:D4}";
    }

    private static IEnumerable<RecurringTriggerSchedule> ExpectedDueSchedules(DateTimeOffset dueAt) =>
        Enumerable.Range(0, DueSchedules).Select(index => ScheduleForNonProjectionIndex(index, dueAt) with { IsActive = true });

    private static IReadOnlyList<RecurringTriggerSchedule> ExpectedProjectionSchedules() =>
        SchedulesForPublication(0, FixedNowUtc).Select(schedule => schedule with { IsActive = true }).ToArray();

    private static RecurringTriggerSchedule ScheduleForNonProjectionIndex(int globalIndex, DateTimeOffset dueAt)
    {
        var publicationIndex = globalIndex < 1976 ? globalIndex / 8 + 1 : globalIndex - 1976 + 248;
        return SchedulesForPublication(publicationIndex, dueAt).Single(schedule => schedule.ScheduleId == $"schedule-due-{globalIndex:D4}");
    }

    private static string PublicationId(int index) => $"publication-{index:D4}";
    private static bool IsInactivePublication(string? publicationId) =>
        publicationId is not null &&
        publicationId.StartsWith("publication-", StringComparison.Ordinal) &&
        int.TryParse(
            publicationId.AsSpan("publication-".Length),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var index) &&
        index >= PublicationCount - InactivePublications;
    private static int Int(string name) => (int)Scenario.Parameters[name];
    private static string String(string name) => (string)Scenario.Parameters[name];
    private static bool ObservationsMatch(IReadOnlyDictionary<string, object> actual, IReadOnlyDictionary<string, object> expected) =>
        actual.Count == expected.Count && actual.All(pair => expected.TryGetValue(pair.Key, out var value) && ValuesEqual(pair.Value, value));
    private static bool ValuesEqual(object actual, object expected) =>
        JsonSerializer.Serialize(actual, CanonicalJsonOptions) == JsonSerializer.Serialize(expected, CanonicalJsonOptions);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class RecurringScheduleComparer : IEqualityComparer<RecurringTriggerSchedule>
    {
        public static RecurringScheduleComparer Instance { get; } = new();
        public bool Equals(RecurringTriggerSchedule? left, RecurringTriggerSchedule? right) =>
            left == right;
        public int GetHashCode(RecurringTriggerSchedule schedule) => StringComparer.Ordinal.GetHashCode(schedule.ScheduleId);
    }
}

/// <summary>Opens independent public recurring-schedule clients over one adapter-selected shared backing.</summary>
public interface IRuntimeRecurringScheduleSelectionWorkloadAdapter
{
    ValueTask<RuntimeRecurringScheduleSelectionClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default);
    ValueTask<IRecurringTriggerScheduleStore> ReopenClientAsync(CancellationToken cancellationToken = default);
}

/// <summary>Two independently created public recurring-schedule clients sharing one backing.</summary>
public sealed record RuntimeRecurringScheduleSelectionClients(IRecurringTriggerScheduleStore Primary, IRecurringTriggerScheduleStore Secondary);

public sealed record RuntimeRecurringScheduleSelectionResult(
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, object> ObservableResults);
