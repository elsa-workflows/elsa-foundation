using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>
/// Executes the frozen due-timer selection contract through the public durable-timer store.
/// Provider setup, schema admission, and fixture seeding remain outside measured operations.
/// </summary>
public sealed class RuntimeDueTimerSelectionWorkload
{
    private const string AdvancedTimerId = "timer-due-0000";
    private const string WorkflowExecutionId = "due-timer-selection-workflow";
    private static readonly ReproducibleWorkloadScenario Scenario = ReproducibleWorkloadScenarioCatalog.Get(WorkloadId);
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public const string WorkloadId = "due-timer-selection";
    public const string ExpectedInputFingerprint = "02cfb91f4f415fcfe8fe6cd64e7c056b88b908e068735d2ec91eb81e0ec8d5bd";
    public const string ExpectedResultDigest = "8f380d449eb3a8e88f1edbea73cf9a7ddfa7a7502cab3ac5a8fcfe3e175ffed3";

    public static string ScenarioId => Scenario.ScenarioId;
    public static string Version => Scenario.Version;
    public static string Seed => Scenario.Seed;
    public static int ConcurrentClaimants => Int("concurrentClaimants");
    public static int DueTimers => Int("dueTimers");
    public static DateTimeOffset FixedNowUtc => DateTimeOffset.Parse(String("fixedNowUtc"), CultureInfo.InvariantCulture);
    public static int PageSize => Int("pageSize");
    public static int SameDueTimestampTimers => Int("sameDueTimestampTimers");
    public static int TimerCount => Int("timerCount");

    public async ValueTask<RuntimeDueTimerSelectionResult> ExecuteAsync(
        IDueTimerSelectionWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        RequireIndependentClients(clients);
        RequireClaimTransitions(clients);

        var operations = new List<string>();
        await SeedAsync(clients.Primary, WorkflowExecutionId, "timer", cancellationToken);
        var secondarySeed = await clients.Secondary.FindAsync(WorkflowExecutionId, AdvancedTimerId, cancellationToken);
        RequireTimer(secondarySeed, WorkflowExecutionId, AdvancedTimerId, "independent seeded-client lookup");
        operations.Add(scenario.OperationSequence[0]);

        var dueFirstPage = await clients.Primary.ListDueAsync(FixedNowUtc, PageSize, cancellationToken);
        RequireDueTimers(dueFirstPage, WorkflowExecutionId, "timer", PageSize, "first bounded due page");
        var allDue = await clients.Primary.ListDueAsync(FixedNowUtc, DueTimers, cancellationToken);
        RequireDueTimers(allDue, WorkflowExecutionId, "timer", DueTimers, "full finite due page");
        if (allDue.Any(timer => timer.TimerId.StartsWith("timer-future-", StringComparison.Ordinal)))
            throw new InvalidOperationException("The due-timer selection returned a not-due timer.");
        operations.Add(scenario.OperationSequence[1]);

        await PrepareAdvanceTargetAsync(clients.Primary, WorkflowExecutionId, "timer", cancellationToken);
        var winner = await RequireSingleAdvanceWinnerAsync(clients, WorkflowExecutionId, "timer", cancellationToken);
        if (!StringComparer.Ordinal.Equals(winner.Timer.TimerId, AdvancedTimerId))
            throw new InvalidOperationException("The due-timer contention wave advanced a timer other than the frozen target.");
        operations.Add(scenario.OperationSequence[2]);

        var staleClaims = await clients.Secondary.ClaimDueAsync(
            ClaimRequest("stale-advance", FixedNowUtc),
            cancellationToken);
        var staleAdvanceRejected = staleClaims.Count == 0;
        if (!staleAdvanceRejected)
            throw new InvalidOperationException("The due-timer store accepted a stale conditional advance.");
        operations.Add(scenario.OperationSequence[3]);

        var reopened = await adapter.ReopenClientAsync(cancellationToken);
        RequireReopenedClient(reopened, clients);
        var reopenedTarget = await reopened.FindAsync(WorkflowExecutionId, AdvancedTimerId, cancellationToken);
        RequireTimer(reopenedTarget, WorkflowExecutionId, AdvancedTimerId, "reopened due-timer lookup");
        var reopenedDue = await reopened.ListDueAsync(FixedNowUtc, DueTimers, cancellationToken);
        RequireDueTimers(reopenedDue, WorkflowExecutionId, "timer", DueTimers, "reopened due-timer page");
        var reopenedClaims = await reopened.ClaimDueAsync(
            ClaimRequest("reopened-stale-advance", FixedNowUtc),
            cancellationToken);
        var reopenedAdvanceMatched = reopenedClaims.Count == 0 &&
                                     reopenedTarget is not null &&
                                     StringComparer.Ordinal.Equals(reopenedTarget.TimerId, AdvancedTimerId);
        if (!reopenedAdvanceMatched)
            throw new InvalidOperationException("The reopened due-timer client did not observe the current claim state.");
        operations.Add(scenario.OperationSequence[4]);

        if (!operations.SequenceEqual(scenario.OperationSequence, StringComparer.Ordinal))
            throw new InvalidOperationException("The due-timer workload operation order no longer matches the catalog contract.");

        var tieTimers = allDue.Take(SameDueTimestampTimers).ToArray();
        if (tieTimers.Length != SameDueTimestampTimers || tieTimers.Select(timer => timer.DueTime).Distinct().Count() != 1)
            throw new InvalidOperationException("The due-timer fixture no longer preserves its frozen timestamp tie boundary.");

        var actualObservations = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["advancedTimerId"] = winner.Timer.TimerId,
            ["dueIdentityDigest"] = Hash(JsonSerializer.Serialize(allDue.Select(timer => timer.TimerId), CanonicalJsonOptions)),
            ["firstPageCount"] = dueFirstPage.Count,
            ["notDueResultCount"] = allDue.Count(timer => timer.DueTime > FixedNowUtc || timer.TimerId.StartsWith("timer-future-", StringComparison.Ordinal)),
            ["reopenedAdvanceMatched"] = reopenedAdvanceMatched,
            ["staleAdvanceRejected"] = staleAdvanceRejected,
            ["tieIdentityDigest"] = Hash(JsonSerializer.Serialize(tieTimers.Select(timer => timer.TimerId), CanonicalJsonOptions))
        };
        if (!ObservationsMatch(actualObservations, scenario.CreateExpectedObservations()))
            throw new InvalidOperationException("The due-timer observable results no longer match the catalog contract.");

        var resultDigest = ReproducibleWorkloadScenarioCatalog.Hash(ReproducibleWorkloadScenarioCatalog.Serialize(new
        {
            WorkloadId,
            scenario.ScenarioId,
            InputFingerprint = scenario.ComputeInputFingerprint(),
            Operations = operations,
            ObservableResults = actualObservations
        }));
        if (!StringComparer.Ordinal.Equals(resultDigest, ExpectedResultDigest))
            throw new InvalidOperationException("The due-timer observable result digest no longer matches its ratified value.");

        return new RuntimeDueTimerSelectionResult(scenario.ComputeInputFingerprint(), resultDigest, operations, actualObservations);
    }

    /// <summary>
    /// Prepares the four bounded public operations after the catalog seed phase. All 2,048 timer writes
    /// and claim priming are performed by operation preparation, outside the timed invocation.
    /// </summary>
    public async ValueTask<IReadOnlyList<IDueTimerSelectionWorkloadOperation>> PrepareMeasuredOperationsAsync(
        IDueTimerSelectionWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        if (!scenario.OperationSequence.SequenceEqual(
                [
                    "seed-due-and-not-due-timers",
                    "list-bounded-due-timers",
                    "advance-due-timer",
                    "attempt-stale-advance",
                    "reopen-and-read-due-state"
                ],
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The due-timer scenario operation sequence no longer matches the measured contract.");
        }

        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        RequireIndependentClients(clients);
        RequireClaimTransitions(clients);
        var primary = clients.Primary;
        var secondary = clients.Secondary;
        var reopened = await adapter.ReopenClientAsync(cancellationToken);
        RequireReopenedClient(reopened, clients);

        var listFixtures = new Dictionary<long, MeasuredFixture>();
        var advanceFixtures = new Dictionary<long, MeasuredFixture>();
        var staleFixtures = new Dictionary<long, MeasuredFixture>();
        var reopenFixtures = new Dictionary<long, MeasuredFixture>();

        // Correctness uses the frozen fixture in this same persistence scope. Remove it before the
        // measured global due-timer queries, then keep each operation invocation isolated from the
        // preceding invocation's fixture. The deletes are preparation work and are excluded from timing.
        await ClearFixtureAsync(primary, WorkflowExecutionId, "timer", cancellationToken);

        async ValueTask ClearMeasuredStateAsync(CancellationToken token)
        {
            foreach (var fixture in listFixtures.Values
                         .Concat(advanceFixtures.Values)
                         .Concat(staleFixtures.Values)
                         .Concat(reopenFixtures.Values)
                         .Distinct())
            {
                await ClearFixtureAsync(primary, fixture.WorkflowExecutionId, fixture.TimerPrefix, token);
            }

            listFixtures.Clear();
            advanceFixtures.Clear();
            staleFixtures.Clear();
            reopenFixtures.Clear();
        }

        return
        [
            new DueTimerSelectionWorkloadOperation(
                scenario.OperationSequence[1],
                async (invocation, token) =>
                {
                    await ClearMeasuredStateAsync(token);
                    var fixture = Fixture("list", invocation);
                    await SeedAsync(primary, fixture.WorkflowExecutionId, fixture.TimerPrefix, token);
                    listFixtures[invocation] = fixture;
                },
                async (invocation, token) =>
                {
                    if (!listFixtures.TryGetValue(invocation, out var fixture))
                        throw new InvalidOperationException("The list-bounded-due-timers operation was invoked without its prepared fixture.");
                    var page = await primary.ListDueAsync(FixedNowUtc, PageSize, token);
                    RequireDueTimers(page, fixture.WorkflowExecutionId, fixture.TimerPrefix, PageSize, "measured bounded due-timer page");
                }),
            new DueTimerSelectionWorkloadOperation(
                scenario.OperationSequence[2],
                async (invocation, token) =>
                {
                    await ClearMeasuredStateAsync(token);
                    var fixture = Fixture("advance", invocation);
                    await SeedAsync(primary, fixture.WorkflowExecutionId, fixture.TimerPrefix, token);
                    await PrepareAdvanceTargetAsync(primary, fixture.WorkflowExecutionId, fixture.TimerPrefix, token);
                    advanceFixtures[invocation] = fixture;
                },
                async (invocation, token) =>
                {
                    if (!advanceFixtures.TryGetValue(invocation, out var fixture))
                        throw new InvalidOperationException("The advance-due-timer operation was invoked without its prepared fixture.");
                    var winner = await RequireSingleAdvanceWinnerAsync(primary, secondary, fixture.WorkflowExecutionId, fixture.TimerPrefix, token);
                    if (!StringComparer.Ordinal.Equals(winner.Timer.TimerId, DueId(fixture.TimerPrefix, 0)))
                        throw new InvalidOperationException("The measured due-timer advance selected the wrong fixture target.");
                }),
            new DueTimerSelectionWorkloadOperation(
                scenario.OperationSequence[3],
                async (invocation, token) =>
                {
                    await ClearMeasuredStateAsync(token);
                    var fixture = Fixture("stale", invocation);
                    await SeedAsync(primary, fixture.WorkflowExecutionId, fixture.TimerPrefix, token);
                    await PrepareAdvanceTargetAsync(primary, fixture.WorkflowExecutionId, fixture.TimerPrefix, token);
                    var current = await primary.ClaimDueAsync(
                        ClaimRequest($"measured-current-{OperationIdentity(invocation)}", FixedNowUtc),
                        token);
                    RequireSingleClaim(current, DueId(fixture.TimerPrefix, 0), "measured stale-advance fixture");
                    staleFixtures[invocation] = fixture;
                },
                async (invocation, token) =>
                {
                    if (!staleFixtures.TryGetValue(invocation, out _))
                        throw new InvalidOperationException("The attempt-stale-advance operation was invoked without its prepared fixture.");
                    var stale = await secondary.ClaimDueAsync(
                        ClaimRequest($"measured-stale-{OperationIdentity(invocation)}", FixedNowUtc),
                        token);
                    if (stale.Count != 0)
                        throw new InvalidOperationException("The measured due-timer operation accepted a stale advance.");
                }),
            new DueTimerSelectionWorkloadOperation(
                scenario.OperationSequence[4],
                async (invocation, token) =>
                {
                    await ClearMeasuredStateAsync(token);
                    var fixture = Fixture("reopen", invocation);
                    await SeedAsync(primary, fixture.WorkflowExecutionId, fixture.TimerPrefix, token);
                    await PrepareAdvanceTargetAsync(primary, fixture.WorkflowExecutionId, fixture.TimerPrefix, token);
                    var current = await primary.ClaimDueAsync(
                        ClaimRequest($"measured-reopen-current-{OperationIdentity(invocation)}", FixedNowUtc),
                        token);
                    RequireSingleClaim(current, DueId(fixture.TimerPrefix, 0), "measured reopened fixture");
                    reopenFixtures[invocation] = fixture;
                },
                async (invocation, token) =>
                {
                    if (!reopenFixtures.TryGetValue(invocation, out var fixture))
                        throw new InvalidOperationException("The reopen-and-read-due-state operation was invoked without its prepared fixture.");
                    var target = await reopened.FindAsync(fixture.WorkflowExecutionId, DueId(fixture.TimerPrefix, 0), token);
                    RequireTimer(target, fixture.WorkflowExecutionId, DueId(fixture.TimerPrefix, 0), "measured reopened due-timer lookup");
                    var due = await reopened.ListDueAsync(FixedNowUtc, DueTimers, token);
                    RequireDueTimers(due, fixture.WorkflowExecutionId, fixture.TimerPrefix, DueTimers, "measured reopened due-timer page");
                    var blocked = await reopened.ClaimDueAsync(
                        ClaimRequest($"measured-reopen-stale-{OperationIdentity(invocation)}", FixedNowUtc),
                        token);
                    if (blocked.Count != 0)
                        throw new InvalidOperationException("The measured reopened due-timer operation did not preserve the current claim.");
                })
        ];
    }

    private static async ValueTask SeedAsync(
        IDurableTimerStore store,
        string workflowExecutionId,
        string timerPrefix,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < TimerCount; index++)
        {
            var due = index < DueTimers;
            var dueTime = due
                ? DueTime(index)
                : FixedNowUtc.AddMinutes(1).AddTicks(index - DueTimers);
            await store.SaveAsync(
                new DurableTimer(
                    TimerId(timerPrefix, index),
                    workflowExecutionId,
                    "Timer",
                    $"sha256:{timerPrefix}-{index:D4}",
                    dueTime,
                    FixedNowUtc.AddDays(-1)),
                cancellationToken);
        }
    }

    private static async ValueTask ClearFixtureAsync(
        IDurableTimerStore store,
        string workflowExecutionId,
        string timerPrefix,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < TimerCount; index++)
            await store.DeleteAsync(workflowExecutionId, TimerId(timerPrefix, index), cancellationToken);
    }

    private static async ValueTask PrepareAdvanceTargetAsync(
        IDurableTimerStore store,
        string workflowExecutionId,
        string timerPrefix,
        CancellationToken cancellationToken)
    {
        var claims = await store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest(
                $"prime-{timerPrefix}",
                FixedNowUtc,
                TimeSpan.FromHours(1),
                DueTimers),
            cancellationToken);
        if (claims.Count != DueTimers)
            throw new InvalidOperationException("The due-timer setup did not claim the complete due fixture before contention.");

        foreach (var claim in claims)
        {
            var visibleAt = StringComparer.Ordinal.Equals(claim.Timer.TimerId, DueId(timerPrefix, 0))
                ? FixedNowUtc
                : FixedNowUtc.AddHours(1);
            var released = await store.ReleaseClaimAsync(claim, visibleAt, cancellationToken);
            if (released.Status != RuntimeDurableTimerClaimTransitionStatus.Succeeded)
                throw new InvalidOperationException("The due-timer setup could not establish its bounded visibility baseline.");
        }
    }

    private static async ValueTask<RuntimeDurableTimerClaim> RequireSingleAdvanceWinnerAsync(
        DueTimerSelectionClients clients,
        string workflowExecutionId,
        string timerPrefix,
        CancellationToken cancellationToken) =>
        await RequireSingleAdvanceWinnerAsync(clients.Primary, clients.Secondary, workflowExecutionId, timerPrefix, cancellationToken);

    private static async ValueTask<RuntimeDurableTimerClaim> RequireSingleAdvanceWinnerAsync(
        IDurableTimerStore primary,
        IDurableTimerStore secondary,
        string workflowExecutionId,
        string timerPrefix,
        CancellationToken cancellationToken)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;
        var contenders = new[] { primary, secondary };
        var attempts = contenders.Select(async (store, index) =>
        {
            if (Interlocked.Increment(ref readyCount) == ConcurrentClaimants)
                ready.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await store.ClaimDueAsync(
                ClaimRequest($"contender-{index}", FixedNowUtc),
                cancellationToken);
        }).ToArray();

        await ready.Task.WaitAsync(cancellationToken);
        release.TrySetResult();
        var outcomes = await Task.WhenAll(attempts);
        if (outcomes.Length != ConcurrentClaimants || outcomes.Count(batch => batch.Count == 1) != 1 || outcomes.Any(batch => batch.Count > 1))
            throw new InvalidOperationException("The due-timer contention wave did not converge to one winning advance and one rejected advance.");

        var winner = outcomes.Single(batch => batch.Count == 1).Single();
        if (!StringComparer.Ordinal.Equals(winner.Timer.TimerId, DueId(timerPrefix, 0)))
            throw new InvalidOperationException("The due-timer contention wave returned a claim for the wrong timer.");
        var persisted = await primary.FindAsync(workflowExecutionId, winner.Timer.TimerId, cancellationToken);
        RequireTimer(persisted, workflowExecutionId, winner.Timer.TimerId, "persisted contention winner");
        return winner;
    }

    private static RuntimeDurableTimerClaimRequest ClaimRequest(
        string ownerId,
        DateTimeOffset now) =>
        new(ownerId, now, TimeSpan.FromMinutes(1), 1);

    private static void RequireSingleClaim(
        IReadOnlyCollection<RuntimeDurableTimerClaim> claims,
        string expectedTimerId,
        string operation)
    {
        if (claims.Count != 1 || !StringComparer.Ordinal.Equals(claims.Single().Timer.TimerId, expectedTimerId))
            throw new InvalidOperationException($"The {operation} did not establish the expected current due-timer claim.");
    }

    private static void RequireDueTimers(
        IReadOnlyCollection<DurableTimer>? timers,
        string workflowExecutionId,
        string timerPrefix,
        int expectedCount,
        string operation)
    {
        var expected = Enumerable.Range(0, expectedCount).Select(index => DueId(timerPrefix, index)).ToArray();
        if (timers is null || timers.Count != expectedCount ||
            !timers.Select(timer => timer.TimerId).SequenceEqual(expected, StringComparer.Ordinal) ||
            timers.Any(timer => !StringComparer.Ordinal.Equals(timer.WorkflowExecutionId, workflowExecutionId) || timer.DueTime > FixedNowUtc))
        {
            throw new InvalidOperationException($"The {operation} does not match the exact ordered due-timer contract.");
        }
    }

    private static void RequireTimer(DurableTimer? timer, string workflowExecutionId, string timerId, string operation)
    {
        if (timer is null || !StringComparer.Ordinal.Equals(timer.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(timer.TimerId, timerId))
            throw new InvalidOperationException($"The {operation} did not expose the expected durable timer.");
    }

    private static void RequireIndependentClients(DueTimerSelectionClients? clients)
    {
        if (clients is null || clients.Primary is null || clients.Secondary is null || ReferenceEquals(clients.Primary, clients.Secondary))
            throw new InvalidOperationException("The due-timer workload adapter must open two independent public-store clients over shared backing.");
    }

    private static void RequireClaimTransitions(DueTimerSelectionClients clients)
    {
        if (!clients.Primary.SupportsClaimTransitions || !clients.Secondary.SupportsClaimTransitions)
            throw new InvalidOperationException("The due-timer selection workload requires provider-atomic claim transitions.");
    }

    private static void RequireReopenedClient(IDurableTimerStore reopened, DueTimerSelectionClients clients)
    {
        if (reopened is null || ReferenceEquals(reopened, clients.Primary) || ReferenceEquals(reopened, clients.Secondary))
            throw new InvalidOperationException("The due-timer workload adapter must reopen a genuinely distinct public-store client.");
        if (!reopened.SupportsClaimTransitions)
            throw new InvalidOperationException("The reopened due-timer client does not support provider-atomic claim transitions.");
    }

    private static ReproducibleWorkloadScenario ValidateScenario()
    {
        if (Scenario.Version != "1.1.0" ||
            Scenario.ScenarioId != "runtime-durable-timer-selection" ||
            Scenario.Seed != "spec094-due-timer-selection-v1.1" ||
            Scenario.ComputeInputFingerprint() != ExpectedInputFingerprint ||
            Scenario.ComputeResultDigest() != ExpectedResultDigest ||
            !ReproducibleWorkloadScenarioCatalog.GoldenVectors.TryGetValue(WorkloadId, out var golden) ||
            golden.InputFingerprint != ExpectedInputFingerprint ||
            golden.ResultDigest != ExpectedResultDigest ||
            ConcurrentClaimants != 2 || DueTimers != 193 || PageSize != 50 ||
            SameDueTimestampTimers != 17 || TimerCount != 2048 ||
            FixedNowUtc != new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero))
        {
            throw new InvalidOperationException("The due-timer scenario no longer matches its frozen v1.1 catalog contract.");
        }

        return Scenario;
    }

    private static DateTimeOffset DueTime(int index) => index < SameDueTimestampTimers
        ? FixedNowUtc.AddMinutes(-2)
        : FixedNowUtc.AddMinutes(-1).AddTicks(index - SameDueTimestampTimers);

    private static string DueId(string timerPrefix, int index) => $"{timerPrefix}-due-{index:D4}";

    private static string TimerId(string timerPrefix, int index) => index < DueTimers
        ? DueId(timerPrefix, index)
        : $"{timerPrefix}-future-{index - DueTimers:D4}";

    private static MeasuredFixture Fixture(string operation, long invocation)
    {
        var identity = OperationIdentity(invocation);
        return new MeasuredFixture($"benchmark-due-{operation}-{identity}", $"benchmark-{operation}-{identity}");
    }

    private static string OperationIdentity(long invocation) =>
        invocation < 0
            ? $"warmup-{(-invocation):D4}"
            : invocation.ToString("D8", CultureInfo.InvariantCulture);

    private static int Int(string name) => (int)Scenario.Parameters[name];
    private static string String(string name) => (string)Scenario.Parameters[name];
    private static bool ObservationsMatch(IReadOnlyDictionary<string, object> actual, IReadOnlyDictionary<string, object> expected) =>
        actual.Count == expected.Count && actual.All(pair => expected.TryGetValue(pair.Key, out var value) &&
            JsonSerializer.Serialize(pair.Value, CanonicalJsonOptions) == JsonSerializer.Serialize(value, CanonicalJsonOptions));
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record MeasuredFixture(string WorkflowExecutionId, string TimerPrefix);
}

/// <summary>Opens independent durable-timer clients over one adapter-selected shared backing.</summary>
public interface IDueTimerSelectionWorkloadAdapter
{
    ValueTask<DueTimerSelectionClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default);
    ValueTask<IDurableTimerStore> ReopenClientAsync(CancellationToken cancellationToken = default);
}

/// <summary>One bounded public durable-timer operation for process measurement.</summary>
public interface IDueTimerSelectionWorkloadOperation
{
    string Id { get; }
    ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default);
    ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default);
}

internal sealed class DueTimerSelectionWorkloadOperation(
    string id,
    Func<long, CancellationToken, ValueTask> prepare,
    Func<long, CancellationToken, ValueTask> invoke) : IDueTimerSelectionWorkloadOperation
{
    public string Id { get; } = id;

    public ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default) =>
        prepare(invocation, cancellationToken);

    public ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default) =>
        invoke(invocation, cancellationToken);
}

/// <summary>Two independently created durable-timer clients sharing one backing.</summary>
public sealed record DueTimerSelectionClients(IDurableTimerStore Primary, IDurableTimerStore Secondary);

public sealed record RuntimeDueTimerSelectionResult(
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, object> ObservableResults);
