using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>Executes the catalog-owned scheduler queue correctness baseline through public runtime contracts only.</summary>
public sealed class RuntimeQueueDrainWorkload
{
    private static readonly ReproducibleWorkloadScenario Scenario = ReproducibleWorkloadScenarioCatalog.Get(WorkloadId);
    private static readonly JsonSerializerOptions CanonicalJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly TimeSpan PrimaryVisibility = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SuccessorVisibility = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);

    public const string WorkloadId = "queue-drain";
    public const string ExpectedInputFingerprint = "15f2d5f9dc8d5814a1613156b7c686e59a150a35bd7e51787a145b6d7230d5e2";
    public const string ExpectedResultDigest = "7db639fdbfddc02973a7275d7c0e8835872b62449ca160e97e8086c0ca46eba4";

    public static string ScenarioId => Scenario.ScenarioId;
    public static string Version => Scenario.Version;
    public static string Seed => Scenario.Seed;
    public static int WorkflowCount => Int("workflowCount");
    public static int WorkItemsPerWorkflow => Int("workItemsPerWorkflow");
    public static int BatchSize => Int("batchSize");
    public static int RetryableItems => Int("retryableItems");
    public static int PoisonItems => Int("poisonItems");
    public static int ConcurrentClaimants => Int("concurrentClaimants");

    public async ValueTask<RuntimeQueueDrainWorkloadResult> ExecuteAsync(
        IRuntimeQueueDrainWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        RequireIndependentClients(clients);

        var operations = new List<string>();
        foreach (var item in CreateSeedItems())
        {
            var stored = await clients.Primary.Queue.EnqueueAsync(item, cancellationToken);
            if (!SameItem(stored, item))
                throw new InvalidOperationException("The public queue enqueue acknowledgement did not preserve the seeded work identity.");
        }
        await RequireExactSeedStateAsync(clients.Secondary.Queue, cancellationToken);
        operations.Add("enqueue-work-items");

        var workflowIds = await clients.Primary.Queue.ListPendingWorkflowExecutionIdsAsync(BatchSize, cancellationToken);
        var expectedWorkflowIds = Enumerable.Range(0, BatchSize).Select(WorkflowId).ToArray();
        if (!workflowIds.SequenceEqual(expectedWorkflowIds, StringComparer.Ordinal))
            throw new InvalidOperationException("The public queue did not return the exact bounded deterministic workflow-id page.");

        var contentionWinners = await ClaimContentionAsync(clients, expectedWorkflowIds, FixedNowUtc, PrimaryVisibility, "queue-contender", cancellationToken);
        var initialClaims = contentionWinners.Select(winner => winner.Claim).ToArray();
        RequireExactClaims(contentionWinners, expectedWorkflowIds, "queue-contender", expectedFence: 1, FixedNowUtc, PrimaryVisibility);
        await RequireExactContentionClaimsAsync(contentionWinners, FixedNowUtc, cancellationToken);
        operations.Add("claim-bounded-batch");

        var normalWinners = contentionWinners.Take(BatchSize - RetryableItems - PoisonItems).ToArray();
        var retryWinners = contentionWinners.Skip(normalWinners.Length).Take(RetryableItems).ToArray();
        var poisonWinners = contentionWinners.Skip(normalWinners.Length + retryWinners.Length).ToArray();
        foreach (var winner in normalWinners)
            RequireApplied(await winner.WinningClient.Queue.CompleteClaimAsync(winner.Claim, cancellationToken), "complete current scheduler claim");
        operations.Add("complete-current-claims");

        var successorNow = FixedNowUtc.Add(PrimaryVisibility).AddSeconds(1);
        var successors = new List<RuntimeSchedulerWorkClaim>();
        foreach (var winner in retryWinners)
        {
            var claim = winner.Claim;
            var successor = await winner.LosingClient.Queue.ClaimAsync(
                new RuntimeSchedulerWorkClaimRequest(claim.Item.WorkflowExecutionId, "queue-successor", successorNow, SuccessorVisibility),
                cancellationToken);
            if (successor is null)
                throw new InvalidOperationException("An expired scheduler claim was not reclaimable by its successor.");
            RequireExactClaim(successor, claim.Item, "queue-successor", claim.FencingToken + 1, successorNow, SuccessorVisibility);
            if (successor.Revision <= claim.Revision)
                throw new InvalidOperationException("The successor scheduler claim did not advance its public revision.");
            successors.Add(successor);
        }
        operations.Add("retry-expired-claim");

        foreach (var winner in poisonWinners)
        {
            var claim = winner.Claim;
            RequireApplied(await winner.WinningClient.Queue.CompleteClaimAsync(claim, cancellationToken), "acknowledge poison scheduler claim");
            var record = CreatePoisonRecord(claim.Item, successorNow.AddSeconds(1));
            var stored = await winner.WinningClient.Poison.RecordAsync(record, cancellationToken);
            RequirePoisonRecord(stored, record);
            RequirePoisonRecord(
                await clients.Primary.Poison.FindAsync(record.WorkflowExecutionId, record.WorkItemId, cancellationToken),
                record);
        }
        var poisonRecords = await clients.Primary.Poison.ListAsync(WorkflowId(BatchSize - PoisonItems), cancellationToken);
        if (poisonRecords.Count != 1)
            throw new InvalidOperationException("The public poison-store list did not expose the recorded poison state.");
        RequirePoisonRecord(poisonRecords.Single(), CreatePoisonRecord(poisonWinners[0].Claim.Item, successorNow.AddSeconds(1)));
        operations.Add("record-and-read-poison-state");

        foreach (var winner in retryWinners)
        {
            var result = await winner.WinningClient.Queue.CompleteClaimAsync(winner.Claim, cancellationToken);
            if (result.Status != RuntimeSchedulerWorkClaimTransitionStatus.Stale)
                throw new InvalidOperationException("The scheduler queue accepted a stale acknowledgement.");
        }
        await RequireExactActiveClaimsAsync(clients.Secondary.Claims, successors, successorNow, "stale acknowledgement", cancellationToken);
        operations.Add("attempt-stale-acknowledgement");

        var reopened = await adapter.ReopenClientAsync(cancellationToken);
        if (reopened is null || !IsDistinctClient(reopened, clients.Primary) || !IsDistinctClient(reopened, clients.Secondary))
            throw new InvalidOperationException("The queue workload adapter must reopen a genuinely distinct public-store client.");
        await RequireReopenedStateAsync(
            reopened,
            normalWinners.Select(winner => winner.Claim).ToArray(),
            poisonWinners.Select(winner => winner.Claim).ToArray(),
            successors,
            successorNow,
            cancellationToken);

        if (!operations.SequenceEqual(scenario.OperationSequence, StringComparer.Ordinal))
            throw new InvalidOperationException("The queue workload operation order no longer matches the catalog contract.");

        var actualObservations = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["claimedIdentityDigest"] = Hash(JsonSerializer.Serialize(initialClaims.Select(claim => claim.Item.WorkItemId), CanonicalJson)),
            ["completedItemCount"] = normalWinners.Length,
            ["currentOwnerCount"] = initialClaims.Length,
            ["poisonItemCount"] = poisonWinners.Length,
            ["retryableItemCount"] = retryWinners.Length,
            ["staleAcknowledgementRejected"] = true
        };
        if (!ObservationsMatch(actualObservations, scenario.CreateExpectedObservations()))
            throw new InvalidOperationException("The queue observable results no longer match the catalog contract.");

        var resultDigest = ReproducibleWorkloadScenarioCatalog.Hash(ReproducibleWorkloadScenarioCatalog.Serialize(new
        {
            WorkloadId,
            scenario.ScenarioId,
            InputFingerprint = scenario.ComputeInputFingerprint(),
            Operations = operations,
            ObservableResults = actualObservations
        }));
        if (!StringComparer.Ordinal.Equals(resultDigest, ExpectedResultDigest))
            throw new InvalidOperationException("The queue observable result digest no longer matches its ratified value.");

        return new RuntimeQueueDrainWorkloadResult(scenario.ComputeInputFingerprint(), resultDigest, operations, actualObservations);
    }

    /// <summary>
    /// Prepares the six catalog-owned public queue and poison-store phases for process measurement.
    /// Fixture writes and claim setup happen before the timing callback; each returned operation invokes
    /// only the bounded public runtime-store route named by its frozen phase.
    /// </summary>
    public async ValueTask<IReadOnlyList<IRuntimeQueueDrainWorkloadOperation>> PrepareMeasuredOperationsAsync(
        IRuntimeQueueDrainWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        if (scenario.OperationSequence.Count != 6)
            throw new InvalidOperationException("The queue scenario must expose exactly six timed operation phases.");

        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        RequireIndependentClients(clients);

        var primary = clients.Primary;
        var secondary = clients.Secondary;
        var enqueueItems = new Dictionary<long, RuntimeSchedulerWorkItem>();
        var claimWorkflowIds = new Dictionary<long, IReadOnlyList<string>>();
        var completeBatches = new Dictionary<long, IReadOnlyList<RuntimeSchedulerWorkClaim>>();
        var retryClaims = new Dictionary<long, RuntimeSchedulerWorkClaim>();
        var poisonClaims = new Dictionary<long, RuntimeSchedulerWorkClaim>();
        var staleClaims = new Dictionary<long, (RuntimeSchedulerWorkClaim Initial, RuntimeSchedulerWorkClaim Successor)>();
        IReadOnlyList<RuntimeSchedulerWorkClaim>? previousClaimBatch = null;

        return
        [
            new RuntimeQueueDrainWorkloadOperation(
                scenario.OperationSequence[0],
                (invocation, _) =>
                {
                    var key = OperationIdentity(invocation);
                    var workflowExecutionId = $"z-queue-enqueue-{key}";
                    enqueueItems[invocation] = CreateOperationItem(workflowExecutionId, $"{workflowExecutionId}-item");
                    return ValueTask.CompletedTask;
                },
                async (invocation, token) =>
                {
                    if (!enqueueItems.TryGetValue(invocation, out var item))
                        throw new InvalidOperationException("The enqueue-work-items operation was invoked without its prepared item.");
                    var stored = await primary.Queue.EnqueueAsync(item, token);
                    if (!SameItem(stored, item))
                        throw new InvalidOperationException("The measured queue enqueue did not preserve the prepared work identity.");
                }),
            new RuntimeQueueDrainWorkloadOperation(
                scenario.OperationSequence[1],
                async (invocation, token) =>
                {
                    if (previousClaimBatch is not null)
                    {
                        foreach (var claim in previousClaimBatch)
                            RequireApplied(await primary.Queue.CompleteClaimAsync(claim, token), "clean the previous measured claim batch");
                    }

                    var items = Enumerable.Range(0, BatchSize)
                        .Select(index =>
                        {
                            var workflowExecutionId = $"a-queue-claim-{OperationIdentity(invocation)}-{index:D2}";
                            return CreateOperationItem(workflowExecutionId, $"{workflowExecutionId}-item");
                        })
                        .ToArray();
                    foreach (var item in items)
                        RequireSameItem(await primary.Queue.EnqueueAsync(item, token), item, "measured claim fixture");
                    claimWorkflowIds[invocation] = items.Select(item => item.WorkflowExecutionId).ToArray();
                },
                async (invocation, token) =>
                {
                    if (!claimWorkflowIds.TryGetValue(invocation, out var expectedIds))
                        throw new InvalidOperationException("The claim-bounded-batch operation was invoked without its prepared workflow identities.");
                    var workflowIds = await primary.Queue.ListPendingWorkflowExecutionIdsAsync(BatchSize, token);
                    if (!workflowIds.SequenceEqual(expectedIds, StringComparer.Ordinal))
                        throw new InvalidOperationException("The measured claim fixture did not produce the exact bounded workflow-id page.");

                    var claims = new List<RuntimeSchedulerWorkClaim>(BatchSize);
                    foreach (var workflowExecutionId in workflowIds)
                    {
                        var claim = await primary.Queue.ClaimAsync(
                            new RuntimeSchedulerWorkClaimRequest(workflowExecutionId, "measured-claim", FixedNowUtc, PrimaryVisibility),
                            token);
                        if (claim is null)
                            throw new InvalidOperationException("The measured claim operation could not claim its prepared work item.");
                        claims.Add(claim);
                    }
                    previousClaimBatch = claims;
                }),
            new RuntimeQueueDrainWorkloadOperation(
                scenario.OperationSequence[2],
                async (invocation, token) =>
                {
                    var claims = new List<RuntimeSchedulerWorkClaim>(BatchSize);
                    foreach (var index in Enumerable.Range(0, BatchSize))
                    {
                        var workflowExecutionId = $"b-queue-complete-{OperationIdentity(invocation)}-{index:D2}";
                        var item = CreateOperationItem(workflowExecutionId, $"{workflowExecutionId}-item");
                        RequireSameItem(await primary.Queue.EnqueueAsync(item, token), item, "measured completion fixture");
                        var claim = await primary.Queue.ClaimAsync(
                            new RuntimeSchedulerWorkClaimRequest(workflowExecutionId, "measured-complete-setup", FixedNowUtc, PrimaryVisibility),
                            token);
                        if (claim is null)
                            throw new InvalidOperationException("The measured completion fixture could not claim its seeded work item.");
                        claims.Add(claim);
                    }
                    completeBatches[invocation] = claims;
                },
                async (invocation, token) =>
                {
                    if (!completeBatches.TryGetValue(invocation, out var claims))
                        throw new InvalidOperationException("The complete-current-claims operation was invoked without its prepared claims.");
                    foreach (var claim in claims)
                        RequireApplied(await primary.Queue.CompleteClaimAsync(claim, token), "complete the measured scheduler claim");
                }),
            new RuntimeQueueDrainWorkloadOperation(
                scenario.OperationSequence[3],
                async (invocation, token) =>
                {
                    var workflowExecutionId = $"c-queue-retry-{OperationIdentity(invocation)}";
                    var item = CreateOperationItem(workflowExecutionId, $"{workflowExecutionId}-item");
                    RequireSameItem(await primary.Queue.EnqueueAsync(item, token), item, "measured retry fixture");
                    var initial = await primary.Queue.ClaimAsync(
                        new RuntimeSchedulerWorkClaimRequest(workflowExecutionId, "measured-retry-setup", FixedNowUtc, PrimaryVisibility),
                        token);
                    if (initial is null)
                        throw new InvalidOperationException("The measured retry fixture could not claim its seeded work item.");
                    retryClaims[invocation] = initial;
                },
                async (invocation, token) =>
                {
                    if (!retryClaims.TryGetValue(invocation, out var initial))
                        throw new InvalidOperationException("The retry-expired-claim operation was invoked without its prepared claim.");
                    var successor = await secondary.Queue.ClaimAsync(
                        new RuntimeSchedulerWorkClaimRequest(initial.Item.WorkflowExecutionId, "measured-retry", FixedNowUtc.Add(PrimaryVisibility).AddSeconds(1), SuccessorVisibility),
                        token);
                    if (successor is null || successor.FencingToken <= initial.FencingToken || successor.Revision <= initial.Revision)
                        throw new InvalidOperationException("The measured retry did not advance the public scheduler claim.");
                    await RequireActiveClaimAsync(secondary.Claims, successor, "measured retry", token);
                }),
            new RuntimeQueueDrainWorkloadOperation(
                scenario.OperationSequence[4],
                async (invocation, token) =>
                {
                    var workflowExecutionId = $"d-queue-poison-{OperationIdentity(invocation)}";
                    var item = CreateOperationItem(workflowExecutionId, $"{workflowExecutionId}-item");
                    RequireSameItem(await primary.Queue.EnqueueAsync(item, token), item, "measured poison fixture");
                    var claim = await primary.Queue.ClaimAsync(
                        new RuntimeSchedulerWorkClaimRequest(workflowExecutionId, "measured-poison-setup", FixedNowUtc, PrimaryVisibility),
                        token);
                    if (claim is null)
                        throw new InvalidOperationException("The measured poison fixture could not claim its seeded work item.");
                    poisonClaims[invocation] = claim;
                },
                async (invocation, token) =>
                {
                    if (!poisonClaims.TryGetValue(invocation, out var claim))
                        throw new InvalidOperationException("The record-and-read-poison-state operation was invoked without its prepared claim.");
                    RequireApplied(await primary.Queue.CompleteClaimAsync(claim, token), "complete the measured poison claim");
                    var record = CreatePoisonRecord(claim.Item, FixedNowUtc.AddMinutes(1));
                    RequirePoisonRecord(await primary.Poison.RecordAsync(record, token), record);
                    RequirePoisonRecord(await primary.Poison.FindAsync(record.WorkflowExecutionId, record.WorkItemId, token), record);
                    var records = await primary.Poison.ListAsync(record.WorkflowExecutionId, token);
                    if (records.Count != 1)
                        throw new InvalidOperationException("The measured poison operation did not expose exactly one recorded poison state.");
                    RequirePoisonRecord(records.Single(), record);
                }),
            new RuntimeQueueDrainWorkloadOperation(
                scenario.OperationSequence[5],
                async (invocation, token) =>
                {
                    var workflowExecutionId = $"e-queue-stale-{OperationIdentity(invocation)}";
                    var item = CreateOperationItem(workflowExecutionId, $"{workflowExecutionId}-item");
                    RequireSameItem(await primary.Queue.EnqueueAsync(item, token), item, "measured stale-acknowledgement fixture");
                    var initial = await primary.Queue.ClaimAsync(
                        new RuntimeSchedulerWorkClaimRequest(workflowExecutionId, "measured-stale-setup", FixedNowUtc, PrimaryVisibility),
                        token);
                    if (initial is null)
                        throw new InvalidOperationException("The measured stale-acknowledgement fixture could not claim its seeded work item.");
                    staleClaims[invocation] = (initial, await RequireSuccessorAsync(secondary.Queue, initial, token));
                },
                async (invocation, token) =>
                {
                    if (!staleClaims.TryGetValue(invocation, out var claims))
                        throw new InvalidOperationException("The stale-acknowledgement operation was invoked without its prepared claims.");
                    var result = await primary.Queue.CompleteClaimAsync(claims.Initial, token);
                    if (result.Status != RuntimeSchedulerWorkClaimTransitionStatus.Stale)
                        throw new InvalidOperationException("The measured queue accepted a stale acknowledgement.");
                    await RequireActiveClaimAsync(secondary.Claims, claims.Successor, "measured stale acknowledgement", token);
                })
        ];
    }

    private static ReproducibleWorkloadScenario ValidateScenario()
    {
        if (Scenario.Version != "1.1.0" || Scenario.ScenarioId != "runtime-scheduler-queue-drain" ||
            Scenario.Seed != "spec094-queue-drain-v1.1" || Scenario.ComputeInputFingerprint() != ExpectedInputFingerprint ||
            Scenario.ComputeResultDigest() != ExpectedResultDigest ||
            !ReproducibleWorkloadScenarioCatalog.GoldenVectors.TryGetValue(WorkloadId, out var golden) ||
            golden.InputFingerprint != ExpectedInputFingerprint || golden.ResultDigest != ExpectedResultDigest ||
            WorkflowCount != 128 || WorkItemsPerWorkflow != 32 || BatchSize != 16 || RetryableItems != 5 ||
            PoisonItems != 3 || ConcurrentClaimants != 2)
        {
            throw new InvalidOperationException("The queue scenario no longer matches its frozen v1.1 catalog contract.");
        }
        return Scenario;
    }

    private static async ValueTask<IReadOnlyList<QueueContentionWinner>> ClaimContentionAsync(
        RuntimeQueueDrainClients clients,
        IReadOnlyList<string> workflowIds,
        DateTimeOffset now,
        TimeSpan visibility,
        string ownerPrefix,
        CancellationToken cancellationToken)
    {
        var claims = new List<QueueContentionWinner>();
        foreach (var workflowId in workflowIds)
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var readyCount = 0;
            var contenders = new[] { clients.Primary, clients.Secondary }
                .Select(async (client, index) =>
                {
                    if (Interlocked.Increment(ref readyCount) == ConcurrentClaimants)
                        ready.TrySetResult();
                    await release.Task.WaitAsync(cancellationToken);
                    var requestedOwner = $"{ownerPrefix}-{index}";
                    var claim = await client.Queue.ClaimAsync(
                        new RuntimeSchedulerWorkClaimRequest(workflowId, requestedOwner, now, visibility),
                        cancellationToken);
                    return (Client: client, RequestedOwner: requestedOwner, Claim: claim);
                }).ToArray();
            await ready.Task.WaitAsync(cancellationToken);
            release.TrySetResult();
            var attempts = await Task.WhenAll(contenders);
            var winners = attempts.Where(attempt => attempt.Claim is not null).ToArray();
            if (winners.Length != 1)
                throw new InvalidOperationException("Concurrent scheduler claimants did not converge to one current owner.");
            var winner = winners.Single();
            if (winner.Claim!.OwnerId != winner.RequestedOwner)
                throw new InvalidOperationException("The scheduler queue granted a claim under an owner other than the requesting contender.");
            var losingClient = ReferenceEquals(winner.Client, clients.Primary) ? clients.Secondary : clients.Primary;
            claims.Add(new QueueContentionWinner(winner.Claim, winner.Client, losingClient));
        }
        return claims;
    }

    private static async ValueTask RequireReopenedStateAsync(
        RuntimeQueueDrainClient reopened,
        IReadOnlyCollection<RuntimeSchedulerWorkClaim> normalClaims,
        IReadOnlyCollection<RuntimeSchedulerWorkClaim> poisonClaims,
        IReadOnlyCollection<RuntimeSchedulerWorkClaim> successors,
        DateTimeOffset successorNow,
        CancellationToken cancellationToken)
    {
        foreach (var claim in normalClaims.Concat(poisonClaims))
        {
            var page = await reopened.Queue.ListAsync(new RuntimeSchedulerWorkQuery(claim.Item.WorkflowExecutionId, 1), cancellationToken);
            if (page.Items.Count != 1 || page.NextContinuationToken is null || page.Items[0].WorkItemId != NextWorkItemId(claim.Item.WorkflowExecutionId))
                throw new InvalidOperationException("The reopened queue did not expose the expected advanced FIFO head.");
        }
        foreach (var successor in successors)
        {
            var page = await reopened.Queue.ListAsync(new RuntimeSchedulerWorkQuery(successor.Item.WorkflowExecutionId, 1), cancellationToken);
            if (page.Items.Count != 1 || page.Items[0].WorkItemId != successor.Item.WorkItemId)
                throw new InvalidOperationException("The reopened queue did not retain the successor-owned FIFO head.");
        }
        await RequireExactActiveClaimsAsync(reopened.Claims, successors, successorNow, "reopen", cancellationToken);
        foreach (var claim in poisonClaims)
            RequirePoisonRecord(await reopened.Poison.FindAsync(claim.Item.WorkflowExecutionId, claim.Item.WorkItemId, cancellationToken), CreatePoisonRecord(claim.Item, successorNow.AddSeconds(1)));
    }

    private static async ValueTask RequireExactSeedStateAsync(
        IWorkflowSchedulerWorkQueue queue,
        CancellationToken cancellationToken)
    {
        for (var workflow = 0; workflow < WorkflowCount; workflow++)
        {
            var expected = CreateSeedItems(workflow);
            var page = await queue.ListAsync(
                new RuntimeSchedulerWorkQuery(WorkflowId(workflow), WorkItemsPerWorkflow),
                cancellationToken);
            if (page.NextContinuationToken is not null || page.Items.Count != expected.Count ||
                !page.Items.Zip(expected).All(pair => SameItem(pair.First, pair.Second)))
            {
                throw new InvalidOperationException("The independently opened public queue client did not expose the exact seeded FIFO state.");
            }
        }
    }

    private static async ValueTask RequireExactActiveClaimsAsync(
        IWorkflowSchedulerWorkClaimInspection inspection,
        IReadOnlyCollection<RuntimeSchedulerWorkClaim> expected,
        DateTimeOffset now,
        string operation,
        CancellationToken cancellationToken)
    {
        foreach (var claim in expected)
        {
            var active = await inspection.ListActiveClaimsAsync(claim.Item.WorkflowExecutionId, now, cancellationToken);
            if (active.Count != 1)
                throw new InvalidOperationException($"The public queue claim inspection did not expose one current successor after {operation}.");
            var actual = active.Single();
            RequireExactClaim(actual, claim.Item, claim.OwnerId, claim.FencingToken, claim.ClaimedAt, claim.VisibleAfter - claim.ClaimedAt);
            if (actual.Revision != claim.Revision)
                throw new InvalidOperationException($"The public queue claim inspection did not preserve the exact successor revision after {operation}.");
        }
    }

    private static async ValueTask RequireExactContentionClaimsAsync(
        IReadOnlyCollection<QueueContentionWinner> winners,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var winner in winners)
        {
            await RequireExactActiveClaimsAsync(
                winner.LosingClient.Claims,
                [winner.Claim],
                now,
                "contention",
                cancellationToken);
        }
    }

    private static void RequireExactClaims(
        IReadOnlyList<QueueContentionWinner> winners,
        IReadOnlyList<string> workflowIds,
        string ownerPrefix,
        long expectedFence,
        DateTimeOffset expectedNow,
        TimeSpan expectedVisibility)
    {
        if (winners.Count != workflowIds.Count || winners.Select(winner => winner.Claim.Item.WorkItemId).Distinct(StringComparer.Ordinal).Count() != workflowIds.Count)
            throw new InvalidOperationException("The public queue claim did not return one bounded current ownership result per workflow.");
        for (var index = 0; index < workflowIds.Count; index++)
        {
            var claim = winners[index].Claim;
            if (!(claim.OwnerId is "queue-contender-0" or "queue-contender-1"))
                throw new InvalidOperationException("The public queue contention claim exposed an undeclared owner.");
            RequireExactClaim(claim, CreateSeedItems(index)[0], claim.OwnerId, expectedFence, expectedNow, expectedVisibility);
        }
    }

    private static void RequireExactClaim(
        RuntimeSchedulerWorkClaim claim,
        RuntimeSchedulerWorkItem expectedItem,
        string ownerId,
        long expectedFence,
        DateTimeOffset expectedNow,
        TimeSpan expectedVisibility)
    {
        if (!SameItem(claim.Item, expectedItem) || claim.OwnerId != ownerId ||
            claim.FencingToken != expectedFence || claim.Revision <= 0 || claim.ClaimedAt != expectedNow ||
            claim.VisibleAfter != expectedNow.Add(expectedVisibility))
        {
            throw new InvalidOperationException("The public queue claim did not expose the exact bounded ordered current ownership state.");
        }
    }

    private static void RequireApplied(RuntimeSchedulerWorkClaimTransitionResult result, string operation)
    {
        if (result.Status != RuntimeSchedulerWorkClaimTransitionStatus.Succeeded)
            throw new InvalidOperationException($"The queue did not {operation}.");
    }

    private static RuntimeSchedulerPoisonRecord CreatePoisonRecord(RuntimeSchedulerWorkItem item, DateTimeOffset recordedAt) =>
        new(item.WorkflowExecutionId, item.WorkItemId, item.CommandKind, "queue-drain-handler",
            new RuntimeFaultInfo("System.InvalidOperationException", "frozen queue poison", "queue-drain"), 1,
            RuntimeSchedulerPoisonDisposition.Poisoned, recordedAt, recordedAt,
            metadata: new Dictionary<string, string> { ["workload"] = WorkloadId, ["retryRelationship"] = "acknowledged-current-claim" });

    private static RuntimeSchedulerWorkItem CreateOperationItem(string workflowExecutionId, string workItemId) =>
        new(workItemId, workflowExecutionId, $"command-{workItemId}", WorkflowExecutionCommandKind.RunSchedulerWork,
            $"envelope-{workItemId}", $"{workflowExecutionId}:{workItemId}", FixedNowUtc, FixedNowUtc, 0);

    private static void RequireSameItem(RuntimeSchedulerWorkItem actual, RuntimeSchedulerWorkItem expected, string operation)
    {
        if (!SameItem(actual, expected))
            throw new InvalidOperationException($"The {operation} did not preserve the prepared work identity.");
    }

    private static async ValueTask<RuntimeSchedulerWorkClaim> RequireSuccessorAsync(
        IWorkflowSchedulerWorkQueue queue,
        RuntimeSchedulerWorkClaim initial,
        CancellationToken cancellationToken)
    {
        var successorNow = FixedNowUtc.Add(PrimaryVisibility).AddSeconds(1);
        var successor = await queue.ClaimAsync(
            new RuntimeSchedulerWorkClaimRequest(initial.Item.WorkflowExecutionId, "measured-successor-setup", successorNow, SuccessorVisibility),
            cancellationToken);
        if (successor is null || successor.FencingToken <= initial.FencingToken || successor.Revision <= initial.Revision)
            throw new InvalidOperationException("The measured stale-acknowledgement fixture could not create a successor claim.");
        return successor;
    }

    private static async ValueTask RequireActiveClaimAsync(
        IWorkflowSchedulerWorkClaimInspection inspection,
        RuntimeSchedulerWorkClaim expected,
        string operation,
        CancellationToken cancellationToken)
    {
        var active = await inspection.ListActiveClaimsAsync(expected.Item.WorkflowExecutionId, expected.ClaimedAt, cancellationToken);
        var actual = active.Count == 1 ? active.Single() : null;
        if (actual is null || !SameClaim(actual, expected))
            throw new InvalidOperationException($"The {operation} did not preserve its current successor claim.");
    }

    private static bool SameClaim(RuntimeSchedulerWorkClaim first, RuntimeSchedulerWorkClaim second) =>
        SameItem(first.Item, second.Item) && first.OwnerId == second.OwnerId &&
        first.FencingToken == second.FencingToken && first.Revision == second.Revision &&
        first.ClaimedAt == second.ClaimedAt && first.VisibleAfter == second.VisibleAfter;

    private static void RequirePoisonRecord(RuntimeSchedulerPoisonRecord? actual, RuntimeSchedulerPoisonRecord expected)
    {
        if (actual is null || actual.WorkflowExecutionId != expected.WorkflowExecutionId || actual.WorkItemId != expected.WorkItemId ||
            actual.CommandKind != expected.CommandKind || actual.HandlerName != expected.HandlerName || actual.FailureCount != expected.FailureCount ||
            actual.Disposition != expected.Disposition || actual.FirstFailedAt != expected.FirstFailedAt || actual.LastFailedAt != expected.LastFailedAt ||
            actual.NextRetryAt != expected.NextRetryAt || actual.Fault != expected.Fault || actual.InnerFault != expected.InnerFault ||
            actual.Metadata.Count != expected.Metadata.Count ||
            actual.Metadata.Any(pair => !expected.Metadata.TryGetValue(pair.Key, out var value) || value != pair.Value))
        {
            throw new InvalidOperationException("The public poison store did not expose the exact recorded poison relationship.");
        }
    }

    private static void RequireIndependentClients(RuntimeQueueDrainClients? clients)
    {
        if (clients is null || clients.Primary is null || clients.Secondary is null || !IsDistinctClient(clients.Primary, clients.Secondary))
            throw new InvalidOperationException("The queue workload adapter must open two distinct public-store clients over shared backing.");
    }

    private static bool IsDistinctClient(RuntimeQueueDrainClient first, RuntimeQueueDrainClient second) =>
        !ReferenceEquals(first, second) && !ReferenceEquals(first.Queue, second.Queue) && !ReferenceEquals(first.Poison, second.Poison) && !ReferenceEquals(first.Claims, second.Claims);

    private static IReadOnlyList<RuntimeSchedulerWorkItem> CreateSeedItems() =>
        Enumerable.Range(0, WorkflowCount).SelectMany(CreateSeedItems).ToArray();

    private static IReadOnlyList<RuntimeSchedulerWorkItem> CreateSeedItems(int workflow) =>
        Enumerable.Range(0, WorkItemsPerWorkflow).Select(offset =>
        {
            var id = offset == 0 ? WorkItemId(workflow) : $"scheduler-work-next-{workflow:D4}-{offset:D2}";
            return new RuntimeSchedulerWorkItem(id, WorkflowId(workflow), $"command-{workflow:D4}-{offset:D2}", WorkflowExecutionCommandKind.RunSchedulerWork,
                $"envelope-{workflow:D4}-{offset:D2}", $"{WorkflowId(workflow)}:{id}", FixedNowUtc, FixedNowUtc, offset);
        }).ToArray();

    private static bool SameItem(RuntimeSchedulerWorkItem first, RuntimeSchedulerWorkItem second) =>
        first.WorkItemId == second.WorkItemId && first.WorkflowExecutionId == second.WorkflowExecutionId &&
        first.CommandId == second.CommandId && first.CommandKind == second.CommandKind &&
        first.EnvelopeId == second.EnvelopeId && first.IdempotencyKey == second.IdempotencyKey &&
        first.EnqueuedAt == second.EnqueuedAt && first.RecordedAt == second.RecordedAt &&
        first.Sequence == second.Sequence && SamePayload(first.Payload, second.Payload) &&
        first.CommandMetadata.Count == second.CommandMetadata.Count &&
        first.CommandMetadata.All(pair => second.CommandMetadata.TryGetValue(pair.Key, out var value) && value == pair.Value) &&
        first.EnvelopeMetadata.Count == second.EnvelopeMetadata.Count &&
        first.EnvelopeMetadata.All(pair => second.EnvelopeMetadata.TryGetValue(pair.Key, out var value) && value == pair.Value) &&
        first.ExecutionScopeId == second.ExecutionScopeId && first.Attempt == second.Attempt;
    private static bool SamePayload(JsonElement? first, JsonElement? second) =>
        first is null ? second is null : second is not null && JsonElement.DeepEquals(first.Value, second.Value);
    private static string WorkflowId(int index) => $"scheduler-workflow-{index:D4}";
    private static string WorkItemId(int index) => $"scheduler-work-{index:D4}";
    private static string NextWorkItemId(string workflowExecutionId) => $"scheduler-work-next-{workflowExecutionId[^4..]}-01";
    private static string OperationIdentity(long invocation) => invocation < 0 ? $"w{-invocation}" : $"m{invocation}";
    private static int Int(string name) => (int)Scenario.Parameters[name];
    private static bool ObservationsMatch(IReadOnlyDictionary<string, object> actual, IReadOnlyDictionary<string, object> expected) =>
        actual.Count == expected.Count && actual.All(pair => expected.TryGetValue(pair.Key, out var value) && JsonSerializer.Serialize(pair.Value, CanonicalJson) == JsonSerializer.Serialize(value, CanonicalJson));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record QueueContentionWinner(
        RuntimeSchedulerWorkClaim Claim,
        RuntimeQueueDrainClient WinningClient,
        RuntimeQueueDrainClient LosingClient);
}

/// <summary>A benchmark-owned bounded public queue phase.</summary>
public interface IRuntimeQueueDrainWorkloadOperation
{
    string Id { get; }
    ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default);
    ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default);
}

internal sealed class RuntimeQueueDrainWorkloadOperation(
    string id,
    Func<long, CancellationToken, ValueTask> prepare,
    Func<long, CancellationToken, ValueTask> invoke) : IRuntimeQueueDrainWorkloadOperation
{
    public string Id { get; } = id;

    public ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default) =>
        prepare(invocation, cancellationToken);

    public ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default) =>
        invoke(invocation, cancellationToken);
}

/// <summary>Creates distinct public runtime-store clients over one adapter-owned backing.</summary>
public interface IRuntimeQueueDrainWorkloadAdapter
{
    ValueTask<RuntimeQueueDrainClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default);
    ValueTask<RuntimeQueueDrainClient> ReopenClientAsync(CancellationToken cancellationToken = default);
}

/// <summary>Two independently created public clients sharing the adapter-selected backing.</summary>
public sealed record RuntimeQueueDrainClients(RuntimeQueueDrainClient Primary, RuntimeQueueDrainClient Secondary);

/// <summary>The public runtime-store contracts required by the queue-drain correctness baseline.</summary>
public sealed record RuntimeQueueDrainClient(
    IWorkflowSchedulerWorkQueue Queue,
    IWorkflowSchedulerPoisonStore Poison,
    IWorkflowSchedulerWorkClaimInspection Claims);

public sealed record RuntimeQueueDrainWorkloadResult(
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, object> ObservableResults);
