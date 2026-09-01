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
    private const int ProjectionPublicationIndex = 0;
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

        var firstProjectionPage = await LoadAndVerifyPublicationProjectionsAsync(
            clients.Primary,
            advancedOccurrence: null,
            cancellationToken);
        operations.Add("load-publication-projections");

        var persistedAdvance = await RequireSingleAdvanceWinnerAsync(
            clients,
            AdvancedScheduleId,
            dueAt,
            advancedTo,
            cancellationToken);
        operations.Add("advance-current-schedule");

        var staleAdvanceAccepted = await clients.Primary.TryAdvanceAsync(
            AdvancedScheduleId,
            dueAt,
            FixedNowUtc.AddHours(2),
            cancellationToken);
        if (staleAdvanceAccepted)
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
        var reopenedFirstProjectionPage = await LoadAndVerifyPublicationProjectionsAsync(
            reopened,
            advancedTo,
            cancellationToken);
        operations.Add("reopen-and-read-projection-state");

        if (!operations.SequenceEqual(scenario.OperationSequence, StringComparer.Ordinal))
            throw new InvalidOperationException("The recurring-schedule workload operation order no longer matches the catalog contract.");

        var actualObservations = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["advancedScheduleId"] = persistedAdvance.ScheduleId,
            ["dueScheduleIdentityDigest"] = Hash(JsonSerializer.Serialize(allDue.Select(schedule => schedule.ScheduleId), CanonicalJsonOptions)),
            ["firstPageCount"] = dueFirstPage.Count,
            ["inactivePublicationResultCount"] = allDue.Count(schedule => IsInactivePublication(schedule.PublicationId)),
            ["loadedProjectionCount"] = firstProjectionPage.Items.Count,
            ["reopenedProjectionMatched"] = reopenedTarget!.NextOccurrence == advancedTo &&
                                           reopenedDue.Count == DueSchedules - 1 &&
                                           reopenedFirstProjectionPage.Items.Count == PageSize,
            ["staleAdvanceRejected"] = !staleAdvanceAccepted
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

    /// <summary>
    /// Prepares the five bounded public recurring-schedule operations used by process measurement.
    /// Publication seeding and the reopened client are established before timing; invocation-specific
    /// schedule fixtures are reset in <see cref="IRuntimeRecurringScheduleSelectionWorkloadOperation.PrepareInvocationAsync"/>.
    /// </summary>
    public async ValueTask<IReadOnlyList<IRuntimeRecurringScheduleSelectionWorkloadOperation>> PrepareMeasuredOperationsAsync(
        IRuntimeRecurringScheduleSelectionWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        if (!scenario.OperationSequence.SequenceEqual(
                [
                    "seed-publications-and-schedules",
                    "list-bounded-due-schedules",
                    "load-publication-projections",
                    "advance-current-schedule",
                    "attempt-stale-advance",
                    "reopen-and-read-projection-state"
                ],
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The recurring-schedule scenario operation sequence no longer matches the measured contract.");
        }

        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        RequireIndependentClients(clients);
        var primary = clients.Primary;
        var dueAt = FixedNowUtc;
        var advancedTo = FixedNowUtc.AddHours(1);
        var dueProbe = await primary.ListDueAsync(dueAt, DueSchedules, cancellationToken);
        if (dueProbe.Count == 0)
        {
            await SeedAsync(primary, dueAt, cancellationToken);
            if (!await primary.TryAdvanceAsync(AdvancedScheduleId, dueAt, advancedTo, cancellationToken))
                throw new InvalidOperationException("The measured recurring-schedule setup could not establish the advanced schedule baseline.");
            dueProbe = await primary.ListDueAsync(dueAt, DueSchedules, cancellationToken);
        }
        else if (dueProbe.Count == DueSchedules)
        {
            if (!await primary.TryAdvanceAsync(AdvancedScheduleId, dueAt, advancedTo, cancellationToken))
                throw new InvalidOperationException("The measured recurring-schedule setup could not advance the seeded schedule.");
            dueProbe = await primary.ListDueAsync(dueAt, DueSchedules, cancellationToken);
        }

        var expectedDue = ExpectedDueSchedules(dueAt).Skip(1).ToArray();
        if (dueProbe.Count != expectedDue.Length)
            throw new InvalidOperationException("The measured recurring-schedule setup did not establish the expected due baseline.");
        RequireDueSchedules(
            dueProbe.Take(PageSize).ToArray(),
            expectedDue.Take(PageSize),
            "measured due-schedule setup");

        var reopened = await adapter.ReopenClientAsync(cancellationToken);
        if (reopened is null || ReferenceEquals(reopened, clients.Primary) || ReferenceEquals(reopened, clients.Secondary))
            throw new InvalidOperationException("The recurring-schedule workload adapter must reopen a separate public-store client for measurement.");

        var projectionQuery = new RecurringTriggerSchedulePublicationPageQuery(PublicationId(ProjectionPublicationIndex), PageSize);
        var projectionProbe = await primary.ListByPublicationPageAsync(projectionQuery, cancellationToken);
        var expectedProjection = ExpectedSchedulesForPublication(ProjectionPublicationIndex, advancedTo).ToArray();
        RequireProjectionPage(
            projectionProbe,
            expectedProjection.Take(PageSize).ToArray(),
            projectionQuery.PublicationId,
            expectsContinuation: true,
            "measured publication projection setup");

        var advanceSchedules = new Dictionary<long, RecurringTriggerSchedule>();
        var staleSchedules = new Dictionary<long, RecurringTriggerSchedule>();
        return
        [
            new RuntimeRecurringScheduleSelectionWorkloadOperation(
                scenario.OperationSequence[1],
                (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var duePage = await primary.ListDueAsync(dueAt, PageSize, token);
                    RequireDueSchedules(duePage, expectedDue.Take(PageSize), "measured bounded due-schedule page");
                }),
            new RuntimeRecurringScheduleSelectionWorkloadOperation(
                scenario.OperationSequence[2],
                (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var page = await primary.ListByPublicationPageAsync(
                        new RecurringTriggerSchedulePublicationPageQuery(PublicationId(ProjectionPublicationIndex), PageSize),
                        token);
                    RequireProjectionPage(
                        page,
                        expectedProjection.Take(PageSize).ToArray(),
                        PublicationId(ProjectionPublicationIndex),
                        expectsContinuation: true,
                        "measured publication projection page");
                }),
            new RuntimeRecurringScheduleSelectionWorkloadOperation(
                scenario.OperationSequence[3],
                async (invocation, token) =>
                {
                    var schedule = CreateMeasuredSchedule($"benchmark-recurring-advance-{OperationIdentity(invocation)}", dueAt);
                    await primary.SaveAsync(schedule, token);
                    advanceSchedules[invocation] = schedule;
                },
                async (invocation, token) =>
                {
                    if (!advanceSchedules.TryGetValue(invocation, out var schedule))
                        throw new InvalidOperationException("The advance-current-schedule operation was invoked without its prepared schedule.");
                    if (!await primary.TryAdvanceAsync(schedule.ScheduleId, dueAt, advancedTo, token))
                        throw new InvalidOperationException("The measured recurring-schedule advance was rejected.");
                    var persisted = await primary.FindAsync(schedule.ScheduleId, token);
                    if (persisted is null || persisted.NextOccurrence != advancedTo)
                        throw new InvalidOperationException("The measured recurring-schedule advance did not persist its new occurrence.");
                }),
            new RuntimeRecurringScheduleSelectionWorkloadOperation(
                scenario.OperationSequence[4],
                async (invocation, token) =>
                {
                    var schedule = CreateMeasuredSchedule($"benchmark-recurring-stale-{OperationIdentity(invocation)}", advancedTo);
                    await primary.SaveAsync(schedule, token);
                    staleSchedules[invocation] = schedule;
                },
                async (invocation, token) =>
                {
                    if (!staleSchedules.TryGetValue(invocation, out var schedule))
                        throw new InvalidOperationException("The attempt-stale-advance operation was invoked without its prepared schedule.");
                    if (await primary.TryAdvanceAsync(schedule.ScheduleId, dueAt, FixedNowUtc.AddHours(2), token))
                        throw new InvalidOperationException("The measured recurring-schedule operation accepted a stale occurrence.");
                }),
            new RuntimeRecurringScheduleSelectionWorkloadOperation(
                scenario.OperationSequence[5],
                (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    RequireAdvancedSchedule(
                        await reopened.FindAsync(AdvancedScheduleId, token),
                        AdvancedScheduleId,
                        advancedTo,
                        "measured reopened schedule lookup");
                    var page = await reopened.ListByPublicationPageAsync(
                        new RecurringTriggerSchedulePublicationPageQuery(PublicationId(ProjectionPublicationIndex), PageSize),
                        token);
                    RequireProjectionPage(
                        page,
                        expectedProjection.Take(PageSize).ToArray(),
                        PublicationId(ProjectionPublicationIndex),
                        expectsContinuation: true,
                        "measured reopened publication projection page");
                })
        ];
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
        }

        for (var publicationIndex = 0; publicationIndex < ReplacementCandidatePublicationIndex; publicationIndex++)
            await store.ActivatePublicationAsync(PublicationId(publicationIndex), replacedPublicationId: null, cancellationToken);

        await store.ActivatePublicationAsync(
            PublicationId(ReplacementCandidatePublicationIndex),
            PublicationId(ReplacedPublicationIndex),
            cancellationToken);
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

    private static RecurringTriggerSchedule CreateMeasuredSchedule(string scheduleId, DateTimeOffset nextOccurrence) =>
        new(
            scheduleId,
            $"benchmark-artifact-{scheduleId}",
            "benchmark-node",
            "Timer",
            $"sha256:{scheduleId}",
            RecurringScheduleKind.Interval,
            "PT5M",
            nextOccurrence,
            FixedNowUtc.AddDays(-1));

    private static string OperationIdentity(long invocation) =>
        invocation < 0
            ? $"warmup-{(-invocation):D4}"
            : invocation.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);

    private static async ValueTask<RecurringTriggerSchedule> RequireSingleAdvanceWinnerAsync(
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
        return persisted!;
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

    private static async ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> LoadAndVerifyPublicationProjectionsAsync(
        IRecurringTriggerScheduleStore store,
        DateTimeOffset? advancedOccurrence,
        CancellationToken cancellationToken)
    {
        RuntimeStorePage<RecurringTriggerSchedule>? firstProjectionPage = null;
        for (var publicationIndex = 0; publicationIndex < PublicationCount; publicationIndex++)
        {
            var publicationId = PublicationId(publicationIndex);
            var expected = ExpectedSchedulesForPublication(publicationIndex, advancedOccurrence).ToArray();
            var observed = 0;
            string? continuation = null;
            var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                var page = await store.ListByPublicationPageAsync(
                    new RecurringTriggerSchedulePublicationPageQuery(publicationId, PageSize, continuation),
                    cancellationToken);
                var expectedItems = expected.Skip(observed).Take(PageSize).ToArray();
                var expectsContinuation = observed + expectedItems.Length < expected.Length;
                RequireProjectionPage(
                    page,
                    expectedItems,
                    publicationId,
                    expectsContinuation,
                    $"publication projection page at offset {observed}");
                if (publicationIndex == ProjectionPublicationIndex && observed == 0)
                    firstProjectionPage = page;

                observed += page.Items.Count;
                continuation = page.NextContinuationToken;
                if (continuation is not null && !seenContinuations.Add(continuation))
                    throw new InvalidOperationException($"Publication '{publicationId}' repeated a continuation token.");
            } while (continuation is not null);

            if (observed != expected.Length)
                throw new InvalidOperationException($"Publication '{publicationId}' did not expose its complete prepared projection.");
        }

        return firstProjectionPage
            ?? throw new InvalidOperationException("The recurring-schedule workload did not observe the projection publication.");
    }

    private static void RequireProjectionPage(
        RuntimeStorePage<RecurringTriggerSchedule>? page,
        IReadOnlyList<RecurringTriggerSchedule> expected,
        string publicationId,
        bool expectsContinuation,
        string operation)
    {
        if (page is null ||
            page.Items.Count != expected.Count ||
            page.Items.Any(schedule => schedule.PublicationId != publicationId) ||
            !page.Items.SequenceEqual(expected, RecurringScheduleComparer.Instance) ||
            expectsContinuation == string.IsNullOrWhiteSpace(page.NextContinuationToken))
        {
            throw new InvalidOperationException(
                $"The {operation} does not match publication '{publicationId}' or its continuation boundary.");
        }
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

    private static IEnumerable<RecurringTriggerSchedule> ExpectedDueSchedules(DateTimeOffset dueAt) =>
        Enumerable.Range(0, DueSchedules).Select(index => ScheduleForNonProjectionIndex(index, dueAt) with { IsActive = true });

    private static IEnumerable<RecurringTriggerSchedule> ExpectedSchedulesForPublication(
        int publicationIndex,
        DateTimeOffset? advancedOccurrence)
    {
        foreach (var schedule in SchedulesForPublication(publicationIndex, FixedNowUtc))
        {
            yield return schedule with
            {
                IsActive = !IsInactivePublication(schedule.PublicationId),
                NextOccurrence = schedule.ScheduleId == AdvancedScheduleId && advancedOccurrence is not null
                    ? advancedOccurrence.Value
                    : schedule.NextOccurrence
            };
        }
    }

    private static RecurringTriggerSchedule ScheduleForNonProjectionIndex(int globalIndex, DateTimeOffset dueAt)
    {
        var publicationIndex = globalIndex < 1976 ? globalIndex / 8 + 1 : globalIndex - 1976 + 248;
        return SchedulesForPublication(publicationIndex, dueAt).Single(schedule => schedule.ScheduleId == $"schedule-due-{globalIndex:D4}");
    }

    private static int ReplacementCandidatePublicationIndex => PublicationCount - InactivePublications;
    private static int ReplacedPublicationIndex => ReplacementCandidatePublicationIndex - 1;
    private static string PublicationId(int index) => $"publication-{index:D4}";
    private static bool IsInactivePublication(string? publicationId) =>
        TryParsePublicationIndex(publicationId, out var index) &&
        (index == ReplacedPublicationIndex || index > ReplacementCandidatePublicationIndex);

    private static bool TryParsePublicationIndex(string? publicationId, out int index)
    {
        index = default;
        return publicationId is not null &&
               publicationId.StartsWith("publication-", StringComparison.Ordinal) &&
               int.TryParse(
                   publicationId.AsSpan("publication-".Length),
                   System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out index);
    }
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

/// <summary>One workload-owned bounded public recurring-schedule operation for process measurement.</summary>
public interface IRuntimeRecurringScheduleSelectionWorkloadOperation
{
    string Id { get; }
    ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default);
    ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default);
}

internal sealed class RuntimeRecurringScheduleSelectionWorkloadOperation(
    string id,
    Func<long, CancellationToken, ValueTask> prepare,
    Func<long, CancellationToken, ValueTask> invoke) : IRuntimeRecurringScheduleSelectionWorkloadOperation
{
    public string Id { get; } = id;

    public ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default) =>
        prepare(invocation, cancellationToken);

    public ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default) =>
        invoke(invocation, cancellationToken);
}

/// <summary>Two independently created public recurring-schedule clients sharing one backing.</summary>
public sealed record RuntimeRecurringScheduleSelectionClients(IRecurringTriggerScheduleStore Primary, IRecurringTriggerScheduleStore Secondary);

public sealed record RuntimeRecurringScheduleSelectionResult(
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, object> ObservableResults);
