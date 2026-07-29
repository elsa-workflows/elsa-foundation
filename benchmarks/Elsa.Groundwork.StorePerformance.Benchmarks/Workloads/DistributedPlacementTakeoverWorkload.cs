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
            throw new InvalidOperationException("The placement workload adapter must open two independent public-store clients over shared durable backing.");
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
            return await contender.Store.TryClaimAsync(
                Claim(executionId, contender.Owner, now, duration),
                now,
                cancellationToken);
        }).ToArray();

        await allReady.Task.WaitAsync(cancellationToken);
        release.TrySetResult();
        var results = await Task.WhenAll(attempts);
        var winners = results.Where(result => result.Outcome == ExecutionPlacementClaimOutcome.Granted).ToArray();
        var denials = results.Where(result => result.Outcome == ExecutionPlacementClaimOutcome.Denied).ToArray();
        if (winners.Length != 1 || denials.Length != ConcurrentClaimants - 1)
            throw new InvalidOperationException("The placement takeover contention wave did not converge to one granted winner and one denied contender.");

        var winner = winners[0];
        var denied = denials[0];
        RequireLease(winner.Lease, executionId, winner.Lease.OwnerId, 2, now, now.Add(duration), "contention winner");
        RequireLease(denied.Lease, executionId, winner.Lease.OwnerId, 2, now, now.Add(duration), "contention denial");
        var persisted = await clients.Primary.FindAsync(executionId, cancellationToken);
        RequireLease(persisted, executionId, winner.Lease.OwnerId, 2, now, now.Add(duration), "persisted contention winner");
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

/// <summary>Two independently created public placement clients sharing one durable backing.</summary>
public sealed record DistributedPlacementTakeoverClients(IExecutionPlacementStore Primary, IExecutionPlacementStore Secondary);

public sealed record DistributedPlacementTakeoverResult(
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, object> ObservableResults);
