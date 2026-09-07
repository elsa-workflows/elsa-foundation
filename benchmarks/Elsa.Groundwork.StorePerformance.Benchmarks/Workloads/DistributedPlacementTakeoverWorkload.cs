using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>
/// Executes the provider-neutral, catalog-owned execution-placement takeover correctness baseline
/// through <see cref="IExecutionPlacementStore"/> only.
/// </summary>
public sealed class DistributedPlacementTakeoverWorkload
{
    private const string InitialOwner = "worker-alpha";
    private const string TakeoverOwner = "worker-beta";
    private const string ContendingOwner = "worker-gamma";
    private const string UnusedOwner = "worker-delta";
    private static readonly ReproducibleWorkloadScenario Scenario = ReproducibleWorkloadScenarioCatalog.Get(WorkloadId);
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public const string WorkloadId = "placement-takeover";
    public const string ExpectedInputFingerprint = "17f22a7e7896b3842ebd771e604b13e859d1b480bc5b6093ce576f14a673e985";
    public const string ExpectedResultDigest = "3ad65cc7ff9287f9c20a68ec6cd267bc78fa083fb775dda36062c185706fb4b4";
    public static string ScenarioId => Scenario.ScenarioId;
    public static string Version => Scenario.Version;
    public static string Seed => Scenario.Seed;
    public static int ExecutionCount => Int("executionCount");
    public static int ActivePlacements => Int("activePlacements");
    public static int LeaseDurationSeconds => Int("leaseDurationSeconds");
    public static int TakeoverCandidates => Int("takeoverCandidates");
    public static int ConcurrentClaimants => Int("concurrentClaimants");
    public static DateTimeOffset FixedNowUtc => DateTimeOffset.Parse(String("fixedNowUtc"), System.Globalization.CultureInfo.InvariantCulture);

    public async ValueTask<DistributedPlacementTakeoverResult> ExecuteAsync(
        IDistributedPlacementTakeoverWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        RequireIndependentClients(clients);

        var operations = new List<string>();
        var now = FixedNowUtc;
        var duration = TimeSpan.FromSeconds(LeaseDurationSeconds);
        var activeExecutionIds = ActiveExecutionIds().ToArray();
        var candidateIds = CandidateExecutionIds().ToArray();
        var seededLeases = await SeedAsync(clients.Primary, now, duration, cancellationToken);
        await RequireUnplacedAsync(clients.Primary, cancellationToken);
        var candidates = await clients.Primary.ListOwnedAsync(
            new ExecutionPlacementLeaseListRequest(InitialOwner, now, TakeoverCandidates),
            cancellationToken);
        RequireCandidates(candidates, candidateIds, now, duration);
        var activeLeases = await clients.Primary.ListOwnedAsync(
            new ExecutionPlacementLeaseListRequest(InitialOwner, now, ActivePlacements),
            cancellationToken);
        RequireActivePlacements(activeLeases, activeExecutionIds, now, duration);
        operations.Add("seed-placement-leases");

        var selectedId = candidateIds[0];
        var originalLease = seededLeases[selectedId];
        var denied = await clients.Secondary.TryClaimAsync(Claim(selectedId, ContendingOwner, now, duration), now, cancellationToken);
        RequireClaim(denied, ExecutionPlacementClaimOutcome.Denied, selectedId, InitialOwner, originalLease.PlacementToken, now, now.Add(duration), "current placement denial");
        var currentPlacement = await clients.Primary.FindAsync(selectedId, cancellationToken);
        RequireLease(currentPlacement, selectedId, InitialOwner, originalLease.PlacementToken, now, now.Add(duration), "current placement lookup");
        operations.Add("claim-current-placement");

        var renewed = await clients.Primary.TryClaimAsync(Claim(selectedId, InitialOwner, now, duration), now, cancellationToken);
        RequireClaim(renewed, ExecutionPlacementClaimOutcome.Renewed, selectedId, InitialOwner, originalLease.PlacementToken + 1, now, now.Add(duration), "current placement renewal");
        operations.Add("renew-current-placement");

        var afterExpiry = now.Add(duration).AddTicks(1);
        operations.Add("advance-past-expiry");

        await RequireSingleContentionWinnerAsync(
            clients,
            candidateIds[1],
            afterExpiry,
            duration,
            cancellationToken);
        var takeover = await clients.Secondary.TryClaimAsync(Claim(selectedId, TakeoverOwner, afterExpiry, duration), afterExpiry, cancellationToken);
        RequireClaim(takeover, ExecutionPlacementClaimOutcome.Granted, selectedId, TakeoverOwner, renewed.Lease.PlacementToken + 1, afterExpiry, afterExpiry.Add(duration), "expired placement takeover");
        await RequireEmptyOwnedListAsync(clients.Primary, InitialOwner, afterExpiry, TakeoverCandidates, "expired owner filtering", cancellationToken);
        await RequireEmptyOwnedListAsync(clients.Primary, UnusedOwner, afterExpiry, 1, "owner filtering", cancellationToken);
        operations.Add("take-over-expired-placement");

        await clients.Primary.ReleaseAsync(renewed.Lease, cancellationToken);
        var winnerAfterStaleRelease = await clients.Secondary.FindAsync(selectedId, cancellationToken);
        RequireLease(winnerAfterStaleRelease, selectedId, TakeoverOwner, takeover.Lease.PlacementToken, afterExpiry, afterExpiry.Add(duration), "stale release rejection");
        operations.Add("attempt-stale-release");

        var reopened = await adapter.ReopenClientAsync(cancellationToken);
        if (reopened is null || ReferenceEquals(reopened, clients.Primary) || ReferenceEquals(reopened, clients.Secondary))
            throw new InvalidOperationException("The placement workload adapter must reopen a genuinely separate public-store client.");
        var reopenedLease = await reopened.FindAsync(selectedId, cancellationToken);
        RequireLease(reopenedLease, selectedId, TakeoverOwner, takeover.Lease.PlacementToken, afterExpiry, afterExpiry.Add(duration), "reopened placement lookup");
        var verifiedReopenedLease = reopenedLease!;
        operations.Add("reopen-and-read-current-placement");

        if (!operations.SequenceEqual(scenario.OperationSequence, StringComparer.Ordinal))
            throw new InvalidOperationException("The placement takeover workload operation order no longer matches the catalog contract.");

        var actualObservations = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["currentOwner"] = verifiedReopenedLease.OwnerId,
            ["placementTokenSequence"] = new[] { originalLease.PlacementToken, renewed.Lease.PlacementToken, takeover.Lease.PlacementToken },
            ["reopenedOwnerMatched"] = verifiedReopenedLease.OwnerId == TakeoverOwner,
            ["staleReleaseRejected"] = winnerAfterStaleRelease!.OwnerId == TakeoverOwner && winnerAfterStaleRelease.PlacementToken == takeover.Lease.PlacementToken,
            ["takeoverCandidateIdentityDigest"] = Hash(JsonSerializer.Serialize(candidateIds, CanonicalJsonOptions))
        };
        var expectedObservations = scenario.CreateExpectedObservations();
        if (!ObservationsMatch(actualObservations, expectedObservations))
            throw new InvalidOperationException("The placement takeover observable results no longer match the catalog contract.");

        var resultDigest = ReproducibleWorkloadScenarioCatalog.Hash(ReproducibleWorkloadScenarioCatalog.Serialize(new
        {
            WorkloadId,
            scenario.ScenarioId,
            InputFingerprint = scenario.ComputeInputFingerprint(),
            Operations = operations,
            ObservableResults = actualObservations
        }));
        if (resultDigest != ExpectedResultDigest)
            throw new InvalidOperationException("The placement takeover observable result digest no longer matches its ratified value.");

        return new DistributedPlacementTakeoverResult(scenario.ComputeInputFingerprint(), resultDigest, operations, actualObservations);
    }

    /// <summary>
    /// Prepares the five bounded placement operations that have a public store route. The seed and the
    /// frozen time-only expiry phase remain outside timing; each invocation fixture uses a unique execution
    /// identity and is established through the public claim contract before its named operation runs.
    /// </summary>
    public async ValueTask<IReadOnlyList<IDistributedPlacementTakeoverWorkloadOperation>> PrepareMeasuredOperationsAsync(
        IDistributedPlacementTakeoverWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        if (!scenario.OperationSequence.SequenceEqual(
                [
                    "seed-placement-leases",
                    "claim-current-placement",
                    "renew-current-placement",
                    "advance-past-expiry",
                    "take-over-expired-placement",
                    "attempt-stale-release",
                    "reopen-and-read-current-placement"
                ],
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The placement takeover scenario operation sequence no longer matches the measured contract.");
        }

        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        RequireIndependentClients(clients);
        var reopened = await adapter.ReopenClientAsync(cancellationToken);
        if (reopened is null || ReferenceEquals(reopened, clients.Primary) || ReferenceEquals(reopened, clients.Secondary))
            throw new InvalidOperationException("The placement workload adapter must reopen a separate public-store client for measurement.");

        var now = FixedNowUtc;
        var duration = TimeSpan.FromSeconds(LeaseDurationSeconds);
        var afterExpiry = now.Add(duration).AddTicks(1);
        var claimLeases = new Dictionary<long, ExecutionPlacementLease>();
        var renewalLeases = new Dictionary<long, ExecutionPlacementLease>();
        var takeoverLeases = new Dictionary<long, ExecutionPlacementLease>();
        var staleReleases = new Dictionary<long, (ExecutionPlacementLease Initial, ExecutionPlacementLease Winner)>();
        var reopenedLeases = new Dictionary<long, ExecutionPlacementLease>();

        return
        [
            new DistributedPlacementTakeoverWorkloadOperation(
                scenario.OperationSequence[1],
                async (invocation, token) =>
                {
                    var executionId = MeasuredExecutionId("claim", invocation);
                    claimLeases[invocation] = await PrepareLeaseAsync(
                        clients.Primary,
                        executionId,
                        InitialOwner,
                        now,
                        duration,
                        token);
                },
                async (invocation, token) =>
                {
                    if (!claimLeases.TryGetValue(invocation, out var initial))
                        throw new InvalidOperationException("The claim-current-placement operation was invoked without its prepared lease.");
                    var denied = await clients.Secondary.TryClaimAsync(
                        Claim(initial.WorkflowExecutionId, ContendingOwner, now, duration),
                        now,
                        token);
                    RequireClaim(
                        denied,
                        ExecutionPlacementClaimOutcome.Denied,
                        initial.WorkflowExecutionId,
                        InitialOwner,
                        initial.PlacementToken,
                        now,
                        now.Add(duration),
                        "measured current placement denial");
                    RequireLease(
                        await clients.Primary.FindAsync(initial.WorkflowExecutionId, token),
                        initial.WorkflowExecutionId,
                        InitialOwner,
                        initial.PlacementToken,
                        now,
                        now.Add(duration),
                        "measured current placement lookup");
                }),
            new DistributedPlacementTakeoverWorkloadOperation(
                scenario.OperationSequence[2],
                async (invocation, token) =>
                {
                    var executionId = MeasuredExecutionId("renew", invocation);
                    renewalLeases[invocation] = await PrepareLeaseAsync(
                        clients.Primary,
                        executionId,
                        InitialOwner,
                        now,
                        duration,
                        token);
                },
                async (invocation, token) =>
                {
                    if (!renewalLeases.TryGetValue(invocation, out var initial))
                        throw new InvalidOperationException("The renew-current-placement operation was invoked without its prepared lease.");
                    var renewed = await clients.Primary.TryClaimAsync(
                        Claim(initial.WorkflowExecutionId, InitialOwner, now, duration),
                        now,
                        token);
                    RequireClaim(
                        renewed,
                        ExecutionPlacementClaimOutcome.Renewed,
                        initial.WorkflowExecutionId,
                        InitialOwner,
                        initial.PlacementToken + 1,
                        now,
                        now.Add(duration),
                        "measured current placement renewal");
                }),
            new DistributedPlacementTakeoverWorkloadOperation(
                scenario.OperationSequence[4],
                async (invocation, token) =>
                {
                    var executionId = MeasuredExecutionId("takeover", invocation);
                    takeoverLeases[invocation] = await PrepareLeaseAsync(
                        clients.Primary,
                        executionId,
                        InitialOwner,
                        now,
                        duration,
                        token);
                },
                async (invocation, token) =>
                {
                    if (!takeoverLeases.TryGetValue(invocation, out var initial))
                        throw new InvalidOperationException("The take-over-expired-placement operation was invoked without its prepared lease.");
                    var takeover = await clients.Secondary.TryClaimAsync(
                        Claim(initial.WorkflowExecutionId, TakeoverOwner, afterExpiry, duration),
                        afterExpiry,
                        token);
                    RequireClaim(
                        takeover,
                        ExecutionPlacementClaimOutcome.Granted,
                        initial.WorkflowExecutionId,
                        TakeoverOwner,
                        initial.PlacementToken + 1,
                        afterExpiry,
                        afterExpiry.Add(duration),
                        "measured expired placement takeover");
                }),
            new DistributedPlacementTakeoverWorkloadOperation(
                scenario.OperationSequence[5],
                async (invocation, token) =>
                {
                    var executionId = MeasuredExecutionId("stale-release", invocation);
                    var initial = await PrepareLeaseAsync(
                        clients.Primary,
                        executionId,
                        InitialOwner,
                        now,
                        duration,
                        token);
                    var takeover = await clients.Secondary.TryClaimAsync(
                        Claim(executionId, TakeoverOwner, afterExpiry, duration),
                        afterExpiry,
                        token);
                    RequireClaim(
                        takeover,
                        ExecutionPlacementClaimOutcome.Granted,
                        executionId,
                        TakeoverOwner,
                        initial.PlacementToken + 1,
                        afterExpiry,
                        afterExpiry.Add(duration),
                        "measured stale-release setup takeover");
                    staleReleases[invocation] = (initial, takeover.Lease);
                },
                async (invocation, token) =>
                {
                    if (!staleReleases.TryGetValue(invocation, out var state))
                        throw new InvalidOperationException("The attempt-stale-release operation was invoked without its prepared leases.");
                    await clients.Primary.ReleaseAsync(state.Initial, token);
                    RequireLease(
                        await clients.Secondary.FindAsync(state.Initial.WorkflowExecutionId, token),
                        state.Initial.WorkflowExecutionId,
                        TakeoverOwner,
                        state.Winner.PlacementToken,
                        afterExpiry,
                        afterExpiry.Add(duration),
                        "measured stale release");
                }),
            new DistributedPlacementTakeoverWorkloadOperation(
                scenario.OperationSequence[6],
                async (invocation, token) =>
                {
                    var executionId = MeasuredExecutionId("reopen", invocation);
                    var initial = await PrepareLeaseAsync(
                        clients.Primary,
                        executionId,
                        InitialOwner,
                        now,
                        duration,
                        token);
                    var takeover = await clients.Secondary.TryClaimAsync(
                        Claim(executionId, TakeoverOwner, afterExpiry, duration),
                        afterExpiry,
                        token);
                    RequireClaim(
                        takeover,
                        ExecutionPlacementClaimOutcome.Granted,
                        executionId,
                        TakeoverOwner,
                        initial.PlacementToken + 1,
                        afterExpiry,
                        afterExpiry.Add(duration),
                        "measured reopen setup takeover");
                    reopenedLeases[invocation] = takeover.Lease;
                },
                async (invocation, token) =>
                {
                    if (!reopenedLeases.TryGetValue(invocation, out var expected))
                        throw new InvalidOperationException("The reopen-and-read-current-placement operation was invoked without its prepared lease.");
                    RequireLease(
                        await reopened.FindAsync(expected.WorkflowExecutionId, token),
                        expected.WorkflowExecutionId,
                        TakeoverOwner,
                        expected.PlacementToken,
                        afterExpiry,
                        afterExpiry.Add(duration),
                        "measured reopened placement lookup");
                })
        ];
    }

    private static ReproducibleWorkloadScenario ValidateScenario()
    {
        if (Scenario.Version != "1.1.0" ||
            Scenario.ScenarioId != "distributed-placement-takeover" ||
            Scenario.Seed != "spec094-placement-takeover-v1.1" ||
            Scenario.ComputeInputFingerprint() != ExpectedInputFingerprint ||
            Scenario.ComputeResultDigest() != ExpectedResultDigest ||
            !ReproducibleWorkloadScenarioCatalog.GoldenVectors.TryGetValue(WorkloadId, out var golden) ||
            golden.InputFingerprint != ExpectedInputFingerprint ||
            golden.ResultDigest != ExpectedResultDigest ||
            ExecutionCount != 512 ||
            ActivePlacements != 256 ||
            LeaseDurationSeconds != 30 ||
            TakeoverCandidates != 64 ||
            ConcurrentClaimants != 2 ||
            FixedNowUtc != new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero))
            throw new InvalidOperationException("The placement takeover scenario no longer matches its frozen v1.1 catalog contract.");

        return Scenario;
    }

    private static async ValueTask<Dictionary<string, ExecutionPlacementLease>> SeedAsync(
        IExecutionPlacementStore store,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var leases = new Dictionary<string, ExecutionPlacementLease>(StringComparer.Ordinal);
        foreach (var executionId in ActiveExecutionIds())
        {
            var claim = await store.TryClaimAsync(Claim(executionId, InitialOwner, now, duration), now, cancellationToken);
            RequireClaim(claim, ExecutionPlacementClaimOutcome.Granted, executionId, InitialOwner, 1, now, now.Add(duration), "seed placement");
            leases.Add(executionId, claim.Lease);
        }

        return leases;
    }

    private static async ValueTask<ExecutionPlacementLease> PrepareLeaseAsync(
        IExecutionPlacementStore store,
        string executionId,
        string owner,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var result = await store.TryClaimAsync(Claim(executionId, owner, now, duration), now, cancellationToken);
        RequireClaim(result, ExecutionPlacementClaimOutcome.Granted, executionId, owner, 1, now, now.Add(duration), "measured lease setup");
        return result.Lease;
    }

    private static string MeasuredExecutionId(string operation, long invocation) =>
        $"placement-measured-{operation}-{OperationIdentity(invocation)}";

    private static string OperationIdentity(long invocation) =>
        invocation < 0
            ? $"warmup-{(-invocation):D4}"
            : invocation.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);

    private static ExecutionPlacementClaim Claim(string executionId, string owner, DateTimeOffset now, TimeSpan duration) =>
        new(executionId, owner, now, now.Add(duration));

    private static IEnumerable<string> ActiveExecutionIds()
    {
        foreach (var candidateId in CandidateExecutionIds())
            yield return candidateId;
        for (var index = TakeoverCandidates; index < ActivePlacements; index++)
            yield return $"placement-live-{index:D4}";
    }

    private static IEnumerable<string> UnplacedExecutionIds()
    {
        for (var index = ActivePlacements; index < ExecutionCount; index++)
            yield return $"placement-unplaced-{index:D4}";
    }

    private static IEnumerable<string> CandidateExecutionIds()
    {
        for (var index = 0; index < TakeoverCandidates; index++)
            yield return $"placement-expired-{index:D4}";
    }

    private static void RequireIndependentClients(DistributedPlacementTakeoverClients? clients)
    {
        if (clients is null || clients.Primary is null || clients.Secondary is null || ReferenceEquals(clients.Primary, clients.Secondary))
            throw new InvalidOperationException("The placement workload adapter must open two independent public-store clients over shared backing.");
    }

    private static void RequireActivePlacements(
        IReadOnlyList<ExecutionPlacementLease> activeLeases,
        IReadOnlyList<string> activeExecutionIds,
        DateTimeOffset now,
        TimeSpan duration)
    {
        if (activeLeases.Count != ActivePlacements ||
            !activeLeases.Select(lease => lease.WorkflowExecutionId).SequenceEqual(activeExecutionIds, StringComparer.Ordinal) ||
            activeLeases.Any(lease => lease.OwnerId != InitialOwner || lease.PlacementToken != 1 || lease.AcquiredAt != now || lease.ExpiresAt != now.Add(duration)))
            throw new InvalidOperationException("The placement takeover workload requires every active placement through one bounded, ordered public list route.");
    }

    private static void RequireCandidates(
        IReadOnlyList<ExecutionPlacementLease> candidates,
        IReadOnlyList<string> candidateIds,
        DateTimeOffset now,
        TimeSpan duration)
    {
        if (candidates.Count != TakeoverCandidates ||
            !candidates.Select(lease => lease.WorkflowExecutionId).SequenceEqual(candidateIds, StringComparer.Ordinal) ||
            candidates.Any(lease => lease.OwnerId != InitialOwner || lease.PlacementToken != 1 || lease.AcquiredAt != now || lease.ExpiresAt != now.Add(duration)))
            throw new InvalidOperationException("The placement takeover workload requires the exact bounded, ordered live placement candidate list.");
    }

    private static async ValueTask RequireUnplacedAsync(IExecutionPlacementStore store, CancellationToken cancellationToken)
    {
        foreach (var executionId in UnplacedExecutionIds())
        {
            if (await store.FindAsync(executionId, cancellationToken) is not null)
                throw new InvalidOperationException("The placement takeover workload found an unplaced execution in the seeded active placement set.");
        }
    }

    private static async ValueTask RequireSingleContentionWinnerAsync(
        DistributedPlacementTakeoverClients clients,
        string executionId,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var contenders = new[]
        {
            (Store: clients.Primary, Owner: TakeoverOwner),
            (Store: clients.Secondary, Owner: ContendingOwner)
        };
        if (contenders.Length != ConcurrentClaimants)
            throw new InvalidOperationException("The placement takeover contention wave no longer matches the catalog claimant count.");

        var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;
        var attempts = contenders.Select(async contender =>
        {
            if (Interlocked.Increment(ref readyCount) == ConcurrentClaimants)
                allReady.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            var result = await contender.Store.TryClaimAsync(
                Claim(executionId, contender.Owner, now, duration),
                now,
                cancellationToken);
            return (contender.Owner, Result: result);
        }).ToArray();

        await allReady.Task.WaitAsync(cancellationToken);
        release.TrySetResult();
        var results = await Task.WhenAll(attempts);
        var winners = results.Where(attempt => attempt.Result.Outcome == ExecutionPlacementClaimOutcome.Granted).ToArray();
        var denials = results.Where(attempt => attempt.Result.Outcome == ExecutionPlacementClaimOutcome.Denied).ToArray();
        if (winners.Length != 1 || denials.Length != ConcurrentClaimants - 1)
            throw new InvalidOperationException("The placement takeover contention wave did not converge to one granted winner and one denied contender.");

        var winner = winners[0];
        var denied = denials[0];
        if (winner.Owner != winner.Result.Lease.OwnerId)
            throw new InvalidOperationException("The placement takeover contention grant did not belong to the claimant that received it.");
        RequireLease(winner.Result.Lease, executionId, winner.Owner, 2, now, now.Add(duration), "contention winner");
        RequireLease(denied.Result.Lease, executionId, winner.Owner, 2, now, now.Add(duration), "contention denial");
        var persisted = await clients.Primary.FindAsync(executionId, cancellationToken);
        RequireLease(persisted, executionId, winner.Owner, 2, now, now.Add(duration), "persisted contention winner");
    }

    private static async ValueTask RequireEmptyOwnedListAsync(
        IExecutionPlacementStore store,
        string owner,
        DateTimeOffset now,
        int take,
        string operation,
        CancellationToken cancellationToken)
    {
        var leases = await store.ListOwnedAsync(
            new ExecutionPlacementLeaseListRequest(owner, now, take),
            cancellationToken);
        if (leases.Count != 0)
            throw new InvalidOperationException($"The placement takeover {operation} returned a lease that should have been excluded.");
    }

    private static void RequireClaim(
        ExecutionPlacementClaimResult? result,
        ExecutionPlacementClaimOutcome expectedOutcome,
        string expectedExecutionId,
        string expectedOwner,
        long expectedToken,
        DateTimeOffset expectedAcquiredAt,
        DateTimeOffset expectedExpiresAt,
        string operation)
    {
        if (result is null || result.Outcome != expectedOutcome)
            throw new InvalidOperationException($"The {operation} returned an unexpected placement claim outcome.");
        RequireLease(result.Lease, expectedExecutionId, expectedOwner, expectedToken, expectedAcquiredAt, expectedExpiresAt, operation);
    }

    private static void RequireLease(
        ExecutionPlacementLease? lease,
        string expectedExecutionId,
        string expectedOwner,
        long expectedToken,
        DateTimeOffset expectedAcquiredAt,
        DateTimeOffset expectedExpiresAt,
        string operation)
    {
        if (lease is null ||
            lease.WorkflowExecutionId != expectedExecutionId ||
            lease.OwnerId != expectedOwner ||
            lease.PlacementToken != expectedToken ||
            lease.AcquiredAt != expectedAcquiredAt ||
            lease.ExpiresAt != expectedExpiresAt)
            throw new InvalidOperationException($"The {operation} returned an unexpected placement lease.");
    }

    private static int Int(string name) => (int)Scenario.Parameters[name];
    private static string String(string name) => (string)Scenario.Parameters[name];

    private static bool ObservationsMatch(IReadOnlyDictionary<string, object> actual, IReadOnlyDictionary<string, object> expected) =>
        actual.Count == expected.Count && actual.All(pair => expected.TryGetValue(pair.Key, out var expectedValue) && ValuesEqual(pair.Value, expectedValue));

    private static bool ValuesEqual(object actual, object expected) =>
        JsonSerializer.Serialize(actual, CanonicalJsonOptions) == JsonSerializer.Serialize(expected, CanonicalJsonOptions);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>Opens independent public placement clients over one adapter-selected shared backing.</summary>
public interface IDistributedPlacementTakeoverWorkloadAdapter
{
    ValueTask<DistributedPlacementTakeoverClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default);
    ValueTask<IExecutionPlacementStore> ReopenClientAsync(CancellationToken cancellationToken = default);
}

/// <summary>One workload-owned bounded public placement operation for process measurement.</summary>
public interface IDistributedPlacementTakeoverWorkloadOperation
{
    string Id { get; }
    ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default);
    ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default);
}

internal sealed class DistributedPlacementTakeoverWorkloadOperation(
    string id,
    Func<long, CancellationToken, ValueTask> prepare,
    Func<long, CancellationToken, ValueTask> invoke) : IDistributedPlacementTakeoverWorkloadOperation
{
    public string Id { get; } = id;

    public ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default) =>
        prepare(invocation, cancellationToken);

    public ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default) =>
        invoke(invocation, cancellationToken);
}

/// <summary>Two independently created public placement clients sharing one backing.</summary>
public sealed record DistributedPlacementTakeoverClients(IExecutionPlacementStore Primary, IExecutionPlacementStore Secondary);

public sealed record DistributedPlacementTakeoverResult(
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, object> ObservableResults);
