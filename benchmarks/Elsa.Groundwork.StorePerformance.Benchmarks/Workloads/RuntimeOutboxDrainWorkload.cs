using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>Executes the catalog-owned post-commit outbox correctness baseline through public runtime contracts only.</summary>
public sealed class RuntimeOutboxDrainWorkload
{
    private static readonly ReproducibleWorkloadScenario Scenario = ReproducibleWorkloadScenarioCatalog.Get(WorkloadId);
    private static readonly JsonSerializerOptions CanonicalJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);

    private const string PrimaryWorkflowExecutionId = "outbox-drain-primary";
    private const string ContentionWorkflowExecutionId = "outbox-drain-contention";
    private const string OtherWorkflowExecutionId = "outbox-drain-other";
    private const string IntentKind = "runtime-outbox-drain";
    private static readonly TimeSpan PrimaryVisibility = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SuccessorVisibility = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    public const string WorkloadId = "outbox-drain";
    public const string ExpectedInputFingerprint = "bc5c6ca1113e78fe948a61de35c66a644129c79028a198d9143dc316cea7bede";
    public const string ExpectedResultDigest = "7228f024095bc2fadc0649e0841d56259f3408b55368911ea402b7d96c8b2e71";

    public static string ScenarioId => Scenario.ScenarioId;
    public static string Version => Scenario.Version;
    public static string Seed => Scenario.Seed;
    public static int OutboxEntryCount => Int("outboxEntryCount");
    public static int DueEntries => Int("dueEntries");
    public static int BatchSize => Int("batchSize");
    public static int RetryableEntries => Int("retryableEntries");
    public static int ConcurrentClaimants => Int("concurrentClaimants");
    public static DateTimeOffset FixedNowUtc => DateTimeOffset.Parse((string)Scenario.Parameters["fixedNowUtc"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);

    public async ValueTask<RuntimeOutboxDrainWorkloadResult> ExecuteAsync(
        IRuntimeOutboxDrainWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        RequireIndependentClients(clients);

        var operations = new List<string>();
        var seeded = CreateSeedItems();
        foreach (var item in seeded)
            await SeedAsync(clients.Primary.Checkpoints, item, cancellationToken);
        operations.Add("seed-due-and-not-due-outbox-entries");

        var primaryClaims = await clients.Primary.Claims.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("outbox-primary", FixedNowUtc, PrimaryVisibility, BatchSize),
            cancellationToken);
        RequireExactClaims(primaryClaims, ExpectedDueIds(BatchSize), "outbox-primary", expectedFence: 1, BatchSize);

        var contentionClaims = await ClaimContentionSentinelAsync(clients, cancellationToken);
        if (contentionClaims.Count != 1 || contentionClaims[0].OutboxItemId != DueId(BatchSize) || contentionClaims[0].FencingToken != 1)
            throw new InvalidOperationException("Concurrent claimants did not produce exactly one current claim for the outbox contention sentinel.");
        operations.Add("claim-due-batch");

        var completedAt = FixedNowUtc.AddSeconds(1);
        foreach (var claim in primaryClaims)
        {
            var retryable = IsRetryable(claim.OutboxItemId);
            await clients.Primary.Completions.CompleteClaimAsync(
                new RuntimePostCommitOutboxClaimCompletion(
                    claim,
                    new RuntimePostCommitOutboxDeliveryResult(
                        claim.OutboxItemId,
                        retryable ? RuntimePostCommitOutboxStatus.FailedRetryable : RuntimePostCommitOutboxStatus.Delivered,
                        completedAt,
                        retryable ? "frozen-retryable-result" : null)),
                cancellationToken);
        }
        var retryBefore = completedAt.Add(RetryDelay).AddTicks(-1);
        var invisibleRetryables = await clients.Primary.Claims.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("outbox-retry-before", retryBefore, SuccessorVisibility, BatchSize, PrimaryWorkflowExecutionId),
            cancellationToken);
        if (invisibleRetryables.Count != 0)
            throw new InvalidOperationException("Retryable outbox entries became claimable before their recorded retry time.");
        var retryAt = completedAt.Add(RetryDelay);
        var retryClaims = await clients.Primary.Claims.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("outbox-retry", retryAt, SuccessorVisibility, BatchSize, PrimaryWorkflowExecutionId),
            cancellationToken);
        RequireExactClaims(retryClaims, ExpectedDueIds(RetryableEntries), "outbox-retry", expectedFence: 2, RetryableEntries);
        operations.Add("record-delivered-and-retryable-results");

        var originalSentinelClaim = contentionClaims.Single();
        var originalSentinelClient = StringComparer.Ordinal.Equals(originalSentinelClaim.OwnerId, "outbox-contender-0")
            ? clients.Primary
            : clients.Secondary;
        var successorSentinelClient = ReferenceEquals(originalSentinelClient, clients.Primary)
            ? clients.Secondary
            : clients.Primary;
        var successorNow = FixedNowUtc.Add(PrimaryVisibility);
        var successorSentinelClaims = await successorSentinelClient.Claims.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("outbox-sentinel-successor", successorNow, SuccessorVisibility, 1, ContentionWorkflowExecutionId),
            cancellationToken);
        RequireExactClaims(successorSentinelClaims, [DueId(BatchSize)], "outbox-sentinel-successor", expectedFence: 2, 1);
        var successorSentinelClaim = successorSentinelClaims.Single();
        operations.Add("reclaim-after-visibility-expiry");

        var staleCompletionRejected = await RequireStaleCompletionRejectedAsync(
            originalSentinelClient.Completions,
            originalSentinelClaim,
            successorNow,
            cancellationToken);
        RequireCurrentClaim(
            await successorSentinelClient.Lookup.FindAsync(originalSentinelClaim.OutboxItemId, cancellationToken),
            successorSentinelClaim,
            "stale completion");
        operations.Add("attempt-stale-completion");

        var reopened = await adapter.ReopenClientAsync(cancellationToken);
        if (reopened is null || !IsDistinctClient(reopened, clients.Primary) || !IsDistinctClient(reopened, clients.Secondary))
            throw new InvalidOperationException("The outbox workload adapter must reopen a genuinely distinct public-store client.");
        await RequireReopenedStateAsync(reopened, primaryClaims, retryClaims, successorSentinelClaim, cancellationToken);

        if (!operations.SequenceEqual(scenario.OperationSequence, StringComparer.Ordinal))
            throw new InvalidOperationException("The outbox workload operation order no longer matches the catalog contract.");

        var actualObservations = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["claimedIdentityDigest"] = Hash(JsonSerializer.Serialize(primaryClaims.Select(claim => claim.OutboxItemId), CanonicalJson)),
            ["deliveredEntryCount"] = primaryClaims.Count(claim => !IsRetryable(claim.OutboxItemId)),
            ["notDueEntryResultCount"] = primaryClaims.Count(claim => !IsDue(claim.OutboxItemId)),
            ["retryableEntryCount"] = retryClaims.Count,
            ["staleCompletionRejected"] = staleCompletionRejected,
            ["visibilityNowUtc"] = (string)scenario.Parameters["fixedNowUtc"]
        };
        if (!ObservationsMatch(actualObservations, scenario.CreateExpectedObservations()))
            throw new InvalidOperationException("The outbox observable results no longer match the catalog contract.");

        var resultDigest = ReproducibleWorkloadScenarioCatalog.Hash(ReproducibleWorkloadScenarioCatalog.Serialize(new
        {
            WorkloadId,
            scenario.ScenarioId,
            InputFingerprint = scenario.ComputeInputFingerprint(),
            Operations = operations,
            ObservableResults = actualObservations
        }));
        if (!StringComparer.Ordinal.Equals(resultDigest, ExpectedResultDigest))
            throw new InvalidOperationException("The outbox observable result digest no longer matches its ratified value.");

        return new RuntimeOutboxDrainWorkloadResult(scenario.ComputeInputFingerprint(), resultDigest, operations, actualObservations);
    }

    private static ReproducibleWorkloadScenario ValidateScenario()
    {
        if (Scenario.Version != "1.1.0" || Scenario.ScenarioId != "runtime-post-commit-outbox-drain" ||
            Scenario.Seed != "spec094-outbox-drain-v1.1" || Scenario.ComputeInputFingerprint() != ExpectedInputFingerprint ||
            Scenario.ComputeResultDigest() != ExpectedResultDigest ||
            !ReproducibleWorkloadScenarioCatalog.GoldenVectors.TryGetValue(WorkloadId, out var golden) ||
            golden.InputFingerprint != ExpectedInputFingerprint || golden.ResultDigest != ExpectedResultDigest ||
            OutboxEntryCount != 1024 || DueEntries != 211 || BatchSize != 32 || RetryableEntries != 7 ||
            ConcurrentClaimants != 2 || FixedNowUtc != new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero))
        {
            throw new InvalidOperationException("The outbox scenario no longer matches its frozen v1.1 catalog contract.");
        }
        return Scenario;
    }

    private static async ValueTask SeedAsync(IRuntimeCheckpointCommitStore checkpoints, RuntimePostCommitOutboxItem item, CancellationToken cancellationToken)
    {
        var changes = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [])
            .WithPostCommitOutbox([new RuntimeStateChange<RuntimePostCommitOutboxItem>(item.OutboxItemId, RuntimeStateChangeOperation.Upsert, item, new Dictionary<string, string>())]);
        var commitId = $"outbox-seed:{item.OutboxItemId}";
        var result = await checkpoints.CommitAsync(
            new RuntimeCheckpointCommit(
                commitId,
                new RuntimeCheckpoint($"checkpoint:{item.OutboxItemId}", "runtime-outbox-drain", item.Intent.WorkflowExecutionId, item.RecordedAt, [], new Dictionary<string, string>()),
                changes,
                [],
                new Dictionary<string, string>()),
            Immediate,
            cancellationToken);
        if (result.PendingPostCommitWorkIds.Count != 1 || result.PendingPostCommitWorkIds.Single() != item.OutboxItemId)
            throw new InvalidOperationException("The public checkpoint seed acknowledgement did not expose its one pending outbox identity.");
    }

    private static async ValueTask<IReadOnlyList<RuntimePostCommitOutboxClaim>> ClaimContentionSentinelAsync(
        RuntimeOutboxDrainClients clients,
        CancellationToken cancellationToken)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;
        var stores = new[] { clients.Primary.Claims, clients.Secondary.Claims };
        var attempts = stores.Select(async (store, index) =>
        {
            if (Interlocked.Increment(ref readyCount) == ConcurrentClaimants)
                ready.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest($"outbox-contender-{index}", FixedNowUtc, PrimaryVisibility, 1, ContentionWorkflowExecutionId), cancellationToken);
        }).ToArray();
        await ready.Task.WaitAsync(cancellationToken);
        release.TrySetResult();
        return (await Task.WhenAll(attempts)).SelectMany(claims => claims).ToArray();
    }

    private static async ValueTask<bool> RequireStaleCompletionRejectedAsync(
        IRuntimePostCommitOutboxClaimCompletionStore completions,
        RuntimePostCommitOutboxClaim staleClaim,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            await completions.CompleteClaimAsync(
                new RuntimePostCommitOutboxClaimCompletion(
                    staleClaim,
                    new RuntimePostCommitOutboxDeliveryResult(staleClaim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, recordedAt)),
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        throw new InvalidOperationException("The outbox store accepted a stale completion.");
    }

    private static async ValueTask RequireReopenedStateAsync(
        RuntimeOutboxDrainClient reopened,
        IReadOnlyCollection<RuntimePostCommitOutboxClaim> completedClaims,
        IReadOnlyCollection<RuntimePostCommitOutboxClaim> retryClaims,
        RuntimePostCommitOutboxClaim successorSentinelClaim,
        CancellationToken cancellationToken)
    {
        foreach (var completed in completedClaims)
        {
            var actual = await reopened.Lookup.FindAsync(completed.OutboxItemId, cancellationToken);
            if (IsRetryable(completed.OutboxItemId))
            {
                var retry = retryClaims.Single(claim => claim.OutboxItemId == completed.OutboxItemId);
                RequireCurrentClaim(actual, retry, "reopened retry");
            }
            else if (actual is null || actual.Status != RuntimePostCommitOutboxStatus.Delivered || actual.DeliveryFencingToken != completed.FencingToken || actual.DeliveredAt is null)
            {
                throw new InvalidOperationException("The reopened client did not expose the delivered outbox result.");
            }
        }
        RequireCurrentClaim(await reopened.Lookup.FindAsync(successorSentinelClaim.OutboxItemId, cancellationToken), successorSentinelClaim, "reopened contention");
    }

    private static void RequireExactClaims(
        IReadOnlyCollection<RuntimePostCommitOutboxClaim> claims,
        IReadOnlyList<string> expectedIds,
        string ownerId,
        long expectedFence,
        int expectedLimit)
    {
        if (claims.Count != expectedLimit || claims.Select(claim => claim.OutboxItemId).Distinct(StringComparer.Ordinal).Count() != expectedLimit ||
            !claims.Select(claim => claim.OutboxItemId).SequenceEqual(expectedIds, StringComparer.Ordinal) ||
            claims.Any(claim => claim.OwnerId != ownerId || claim.FencingToken != expectedFence || claim.Item.Status != RuntimePostCommitOutboxStatus.Delivering ||
                claim.Item.DeliveringOwnerId != ownerId || claim.Item.DeliveryFencingToken != expectedFence || claim.VisibleAfter <= claim.ClaimedAt))
        {
            throw new InvalidOperationException("The public outbox claim did not return the exact bounded ordered current ownership result.");
        }
    }

    private static void RequireCurrentClaim(RuntimePostCommitOutboxItem? actual, RuntimePostCommitOutboxClaim expected, string operation)
    {
        if (actual is null || actual.Status != RuntimePostCommitOutboxStatus.Delivering || actual.OutboxItemId != expected.OutboxItemId ||
            actual.DeliveringOwnerId != expected.OwnerId || actual.DeliveryFencingToken != expected.FencingToken || actual.DeliveryVisibleAfter != expected.VisibleAfter)
        {
            throw new InvalidOperationException($"The public outbox reread did not expose the expected current claim after {operation}.");
        }
    }

    private static void RequireIndependentClients(RuntimeOutboxDrainClients? clients)
    {
        if (clients is null || clients.Primary is null || clients.Secondary is null || !IsDistinctClient(clients.Primary, clients.Secondary))
            throw new InvalidOperationException("The outbox workload adapter must open two distinct public-store clients over shared backing.");
    }

    private static bool IsDistinctClient(RuntimeOutboxDrainClient first, RuntimeOutboxDrainClient second) =>
        !ReferenceEquals(first, second) && !ReferenceEquals(first.Checkpoints, second.Checkpoints) && !ReferenceEquals(first.Claims, second.Claims) &&
        !ReferenceEquals(first.Completions, second.Completions) && !ReferenceEquals(first.Lookup, second.Lookup);

    private static IReadOnlyList<RuntimePostCommitOutboxItem> CreateSeedItems() =>
        Enumerable.Range(0, OutboxEntryCount).Select(index =>
        {
            var due = index < DueEntries;
            var id = due ? DueId(index) : $"outbox-not-due-{index - DueEntries:D4}";
            var workflowExecutionId = index < BatchSize ? PrimaryWorkflowExecutionId : index == BatchSize ? ContentionWorkflowExecutionId : OtherWorkflowExecutionId;
            var recordedAt = FixedNowUtc;
            var intent = new RuntimePostCommitIntent($"intent-{id}", workflowExecutionId, IntentKind, recordedAt, $"activity-{index:D4}", $"key-{id}", null);
            return new RuntimePostCommitOutboxItem(
                id,
                intent,
                RuntimePostCommitOutboxStatus.Pending,
                recordedAt,
                due ? FixedNowUtc : FixedNowUtc.AddDays(1),
                index < RetryableEntries ? new RuntimePostCommitRetryPolicy(2, RetryDelay) : RuntimePostCommitRetryPolicy.None);
        }).ToArray();

    private static bool IsDue(string outboxItemId) => outboxItemId.StartsWith("outbox-due-", StringComparison.Ordinal);
    private static bool IsRetryable(string outboxItemId) => ExpectedDueIds(RetryableEntries).Contains(outboxItemId, StringComparer.Ordinal);
    private static IReadOnlyList<string> ExpectedDueIds(int count) => Enumerable.Range(0, count).Select(DueId).ToArray();
    private static string DueId(int index) => $"outbox-due-{index:D4}";
    private static int Int(string name) => (int)Scenario.Parameters[name];
    private static bool ObservationsMatch(IReadOnlyDictionary<string, object> actual, IReadOnlyDictionary<string, object> expected) =>
        actual.Count == expected.Count && actual.All(pair => expected.TryGetValue(pair.Key, out var value) && JsonSerializer.Serialize(pair.Value, CanonicalJson) == JsonSerializer.Serialize(value, CanonicalJson));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>Creates distinct public runtime-store clients over one adapter-owned backing.</summary>
public interface IRuntimeOutboxDrainWorkloadAdapter
{
    ValueTask<RuntimeOutboxDrainClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default);
    ValueTask<RuntimeOutboxDrainClient> ReopenClientAsync(CancellationToken cancellationToken = default);
}

/// <summary>Two independently created public clients sharing the adapter-selected backing.</summary>
public sealed record RuntimeOutboxDrainClients(RuntimeOutboxDrainClient Primary, RuntimeOutboxDrainClient Secondary);

/// <summary>The public runtime-store contracts required by the outbox-drain correctness baseline.</summary>
public sealed record RuntimeOutboxDrainClient(
    IRuntimeCheckpointCommitStore Checkpoints,
    IRuntimePostCommitOutboxClaimStore Claims,
    IRuntimePostCommitOutboxClaimCompletionStore Completions,
    IPostCommitOutboxLookupStore Lookup);

public sealed record RuntimeOutboxDrainWorkloadResult(
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, object> ObservableResults);
