using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>Executes the catalog-owned checkpoint bundle correctness baseline through public runtime stores only.</summary>
public sealed class RuntimeCheckpointCommitWorkload
{
    private static readonly ReproducibleWorkloadScenario Scenario = ReproducibleWorkloadScenarioCatalog.Get(WorkloadId);
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    public const string WorkloadId = "checkpoint-commit";
    public const string ExpectedInputFingerprint = "ee4cef346ca64739bbe7cfc84ee3f74e6acefec582f537c685991ca73c62ce13";
    public const string ExpectedResultDigest = "ebb92b59a7a331e863c813f7110272093be6a78794a9cc7a0d914103ab4c9c62";
    public const string ExpectedCommittedBundleIdentityDigest = "20bf6656047558018d39aab671014991d49d250b493ee1e27eae3db4dd431168";
    public static string ScenarioId => Scenario.ScenarioId;
    public static string Version => Scenario.Version;
    public static string Seed => Scenario.Seed;
    public static int ExecutionCount => Int("executionCount");
    public static int CheckpointCount => Int("checkpointCount");
    public static int ActivityChangesPerCheckpoint => Int("activityChangesPerCheckpoint");
    public static int DurableValueChangesPerCheckpoint => Int("durableValueChangesPerCheckpoint");
    public static int OutboxEntriesPerCheckpoint => Int("outboxEntriesPerCheckpoint");
    public static int ConcurrentFenceContenders => Int("concurrentFenceContenders");
    public static int PayloadBytes => Int("payloadBytes");

    public async ValueTask<RuntimeCheckpointCommitWorkloadResult> ExecuteAsync(
        IRuntimeCheckpointCommitWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        RequireIndependentClients(clients);

        var operations = new List<string>();
        var executable = CreateExecutable();
        await clients.Primary.Executables.SaveAsync(executable, cancellationToken);
        RequireExecutable(await clients.Primary.Executables.FindAsync(executable.Identity.ArtifactId, cancellationToken), executable);
        RequireExecutable(await clients.Secondary.Executables.FindAsync(executable.Identity.ArtifactId, cancellationToken), executable);
        // IRuntimeCheckpointCommitStore owns the executable root-write lease around its atomic root mutation.
        // Acquiring a second caller-side lease here would duplicate that provider responsibility and can race it.

        var leases = new Dictionary<string, RuntimeExecutionLease>(StringComparer.Ordinal);
        foreach (var executionId in ExecutionIds())
        {
            var lease = await clients.Primary.Ownership.AcquireAsync(executionId, cancellationToken);
            RequireCurrentLease(lease, executionId);
            await clients.Primary.Ownership.EnsureCurrentAsync(executionId, lease.FencingToken, cancellationToken);
            leases.Add(executionId, lease);
        }
        if (leases.Count != ExecutionCount)
            throw new InvalidOperationException("The checkpoint workload could not acquire the required current execution fences.");
        operations.Add("seed-fenced-executions");

        var bundles = new Dictionary<string, ExpectedBundle>(StringComparer.Ordinal);
        var acknowledgedCommitIds = new List<string>();
        for (var index = 0; index < CheckpointCount; index++)
        {
            var executionId = ExecutionIdFor(index);
            var bundle = CreateBundle(index, leases[executionId].ToFence(), executable.Identity);
            await RequireHeartbeatAsync(clients.Primary.Ownership, leases[executionId], cancellationToken);
            var acknowledgement = await clients.Primary.Checkpoints.CommitAsync(bundle.Commit, Immediate, cancellationToken);
            acknowledgedCommitIds.Add(RequireAcknowledgement(acknowledgement, bundle, "accepted checkpoint"));
            bundles.Add(bundle.Commit.CommitId, bundle);
        }
        if (acknowledgedCommitIds.Count != CheckpointCount || acknowledgedCommitIds.Distinct(StringComparer.Ordinal).Count() != CheckpointCount)
            throw new InvalidOperationException("Checkpoint acknowledgements did not expose one distinct accepted bundle identity per commit.");
        operations.Add("commit-checkpoint-bundle");

        var primaryRead = await ReadBundlesAsync(clients.Primary, bundles.Values, cancellationToken);
        var secondaryRead = await ReadBundlesAsync(clients.Secondary, bundles.Values, cancellationToken);
        if (!primaryRead.Equals(secondaryRead))
            throw new InvalidOperationException("Independent checkpoint clients did not expose the same committed bundle.");

        var replay = bundles[CheckpointId(0)];
        var replayAcknowledgement = await clients.Secondary.Checkpoints.CommitAsync(replay.Commit, Immediate, cancellationToken);
        RequireAcknowledgement(replayAcknowledgement, replay, "equivalent replay");
        var replayRead = await ReadExecutionAsync(clients.Secondary, replay.ExecutionId, BundlesForExecution(bundles.Values, replay.ExecutionId), cancellationToken);
        var replayCreatedDuplicateWork = replayRead.OutboxCount != CheckpointsPerExecution * OutboxEntriesPerCheckpoint ||
                                         !replayRead.OutboxIds.SequenceEqual(OutboxIdsForExecution(bundles.Values, replay.ExecutionId), StringComparer.Ordinal);
        if (replayCreatedDuplicateWork)
            throw new InvalidOperationException("Equivalent replay created duplicate or altered post-commit work.");
        await RequireConflictingReplayRejectionAsync(clients.Secondary.Checkpoints, replay.Commit, cancellationToken);
        operations.Add("replay-equivalent-commit");

        var staleExecutionId = ExecutionIdFor(0);
        var staleFence = leases[staleExecutionId].ToFence();
        var successor = await clients.Secondary.Ownership.AcquireAsync(staleExecutionId, cancellationToken);
        RequireCurrentLease(successor, staleExecutionId);
        await clients.Secondary.Ownership.EnsureCurrentAsync(staleExecutionId, successor.FencingToken, cancellationToken);
        if (successor.FencingToken <= staleFence.FencingToken)
            throw new InvalidOperationException("Ownership supersession did not issue a newer fencing token.");
        var staleBundles = new[]
        {
            CreateBundle(CheckpointCount, staleFence, executable.Identity, "stale-a"),
            CreateBundle(CheckpointCount + 1, staleFence, executable.Identity, "stale-b")
        };
        var staleFenceRejected = await RequireConcurrentStaleRejectionAsync(clients, staleBundles, cancellationToken);
        foreach (var staleBundle in staleBundles)
            staleFenceRejected &= await RequireStaleAsync(clients.Primary.Checkpoints, staleBundle.Commit, cancellationToken);
        if (!staleFenceRejected)
            throw new InvalidOperationException("The checkpoint store did not reject every stale fencing attempt.");
        await ReadExecutionAsync(clients.Primary, staleExecutionId, BundlesForExecution(bundles.Values, staleExecutionId), cancellationToken);
        operations.Add("attempt-stale-fence-commit");

        var reopened = await adapter.ReopenClientAsync(cancellationToken);
        if (reopened is null || !IsDistinctClient(reopened, clients.Primary) || !IsDistinctClient(reopened, clients.Secondary))
            throw new InvalidOperationException("The checkpoint workload adapter must reopen a genuinely distinct public-store client.");
        RequireExecutable(await reopened.Executables.FindAsync(executable.Identity.ArtifactId, cancellationToken), executable);
        var reopenedRead = await ReadBundlesAsync(reopened, bundles.Values, cancellationToken);
        var reopenedBundleMatched = reopenedRead.Equals(primaryRead);
        if (!reopenedBundleMatched)
            throw new InvalidOperationException("The reopened checkpoint client did not expose the committed bundle.");
        operations.Add("reopen-and-read-committed-bundle");

        if (!operations.SequenceEqual(scenario.OperationSequence, StringComparer.Ordinal))
            throw new InvalidOperationException("The checkpoint workload operation order no longer matches the catalog contract.");

        var actualObservations = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["acceptedCheckpointCount"] = acknowledgedCommitIds.Count,
            ["activityChangeCount"] = primaryRead.ActivityCount,
            ["committedBundleIdentityDigest"] = Hash(JsonSerializer.Serialize(acknowledgedCommitIds.OrderBy(id => id, StringComparer.Ordinal), CanonicalJsonOptions)),
            ["durableValueChangeCount"] = primaryRead.DurableValueCount,
            ["outboxEntryCount"] = primaryRead.OutboxCount,
            ["replayCreatedDuplicateWork"] = replayCreatedDuplicateWork,
            ["reopenedBundleMatched"] = reopenedBundleMatched,
            ["staleFenceRejected"] = staleFenceRejected
        };
        if (!ObservationsMatch(actualObservations, scenario.CreateExpectedObservations()))
            throw new InvalidOperationException("The checkpoint observable results no longer match the catalog contract.");
        if (!StringComparer.Ordinal.Equals((string)actualObservations["committedBundleIdentityDigest"], ExpectedCommittedBundleIdentityDigest))
            throw new InvalidOperationException("The checkpoint bundle identity digest no longer matches its frozen value.");

        var resultDigest = ReproducibleWorkloadScenarioCatalog.Hash(ReproducibleWorkloadScenarioCatalog.Serialize(new
        {
            WorkloadId,
            scenario.ScenarioId,
            InputFingerprint = scenario.ComputeInputFingerprint(),
            Operations = operations,
            ObservableResults = actualObservations
        }));
        if (!StringComparer.Ordinal.Equals(resultDigest, ExpectedResultDigest))
            throw new InvalidOperationException("The checkpoint observable result digest no longer matches its ratified value.");

        return new RuntimeCheckpointCommitWorkloadResult(scenario.ComputeInputFingerprint(), resultDigest, operations, actualObservations);
    }

    private static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);
    private static int CheckpointsPerExecution => CheckpointCount / ExecutionCount;

    private static ReproducibleWorkloadScenario ValidateScenario()
    {
        if (Scenario.Version != "1.1.0" || Scenario.ScenarioId != "runtime-checkpoint-commit" ||
            Scenario.Seed != "spec094-checkpoint-commit-v1.1" || Scenario.ComputeInputFingerprint() != ExpectedInputFingerprint ||
            Scenario.ComputeResultDigest() != ExpectedResultDigest ||
            !ReproducibleWorkloadScenarioCatalog.GoldenVectors.TryGetValue(WorkloadId, out var golden) ||
            golden.InputFingerprint != ExpectedInputFingerprint || golden.ResultDigest != ExpectedResultDigest ||
            ExecutionCount != 128 || CheckpointCount != 1024 || ActivityChangesPerCheckpoint != 4 ||
            DurableValueChangesPerCheckpoint != 3 || OutboxEntriesPerCheckpoint != 2 ||
            ConcurrentFenceContenders != 2 || PayloadBytes != 512)
            throw new InvalidOperationException("The checkpoint scenario no longer matches its frozen v1.1 catalog contract.");
        return Scenario;
    }

    private static async ValueTask<bool> RequireConcurrentStaleRejectionAsync(
        RuntimeCheckpointCommitClients clients,
        IReadOnlyList<ExpectedBundle> bundles,
        CancellationToken cancellationToken)
    {
        if (bundles.Count != ConcurrentFenceContenders)
            throw new InvalidOperationException("The stale-fence contention wave no longer matches the catalog contender count.");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;
        var stores = new[] { clients.Primary.Checkpoints, clients.Secondary.Checkpoints };
        var attempts = bundles.Select(async (bundle, index) =>
        {
            if (Interlocked.Increment(ref readyCount) == ConcurrentFenceContenders)
                ready.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await RequireStaleAsync(stores[index], bundle.Commit, cancellationToken);
        }).ToArray();
        await ready.Task.WaitAsync(cancellationToken);
        release.TrySetResult();
        var results = await Task.WhenAll(attempts);
        return results.Length == ConcurrentFenceContenders && results.All(rejected => rejected);
    }

    private static async ValueTask<bool> RequireStaleAsync(IRuntimeCheckpointCommitStore store, RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        try
        {
            await store.CommitAsync(commit, Immediate, cancellationToken);
        }
        catch (RuntimeStaleFencingTokenException)
        {
            return true;
        }
        throw new InvalidOperationException("The checkpoint store accepted a stale fencing token.");
    }

    private static async ValueTask RequireHeartbeatAsync(
        IRuntimeExecutionOwnershipService ownership,
        RuntimeExecutionLease lease,
        CancellationToken cancellationToken)
    {
        var heartbeat = await ownership.HeartbeatAsync(lease, cancellationToken);
        if (!heartbeat.Succeeded || heartbeat.CurrentFencingToken != lease.FencingToken)
            throw new InvalidOperationException("The checkpoint workload could not refresh its current execution fence before commit.");
    }

    private static async ValueTask RequireConflictingReplayRejectionAsync(
        IRuntimeCheckpointCommitStore store,
        RuntimeCheckpointCommit accepted,
        CancellationToken cancellationToken)
    {
        var conflict = accepted with
        {
            Metadata = new Dictionary<string, string>(accepted.Metadata, StringComparer.Ordinal)
            {
                ["replay"] = "conflicting"
            }
        };
        try
        {
            await store.CommitAsync(conflict, Immediate, cancellationToken);
        }
        catch (RuntimeCheckpointReplayConflictException)
        {
            return;
        }

        throw new InvalidOperationException("The checkpoint store accepted a conflicting replay for an existing commit identity.");
    }

    private static async ValueTask<BundleRead> ReadBundlesAsync(
        RuntimeCheckpointCommitClient client,
        IEnumerable<ExpectedBundle> bundles,
        CancellationToken cancellationToken)
    {
        var reads = new List<ExecutionRead>();
        foreach (var executionId in ExecutionIds())
            reads.Add(await ReadExecutionAsync(client, executionId, BundlesForExecution(bundles, executionId), cancellationToken));
        return new BundleRead(reads.Sum(read => read.ActivityCount), reads.Sum(read => read.DurableValueCount), reads.Sum(read => read.OutboxCount));
    }

    private static async ValueTask<ExecutionRead> ReadExecutionAsync(
        RuntimeCheckpointCommitClient client,
        string executionId,
        IReadOnlyList<ExpectedBundle> bundles,
        CancellationToken cancellationToken)
    {
        var latest = bundles.OrderBy(bundle => bundle.Index).Last();
        var workflow = await client.WorkflowExecutions.FindAsync(executionId, cancellationToken);
        if (workflow is null || workflow.WorkflowExecutionId != executionId || workflow.UpdatedAt != latest.Commit.Checkpoint.OccurredAt ||
            workflow.PinnedExecutable != latest.Workflow.PinnedExecutable ||
            !workflow.SystemMetadata.OrderBy(pair => pair.Key, StringComparer.Ordinal).SequenceEqual(latest.Workflow.SystemMetadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)))
            throw new InvalidOperationException("A public workflow execution reread did not match its committed checkpoint state.");

        var activities = await ReadPagesAsync(
            token => client.Activities.ListPageAsync(new ActivityExecutionStatePageQuery(executionId, 3, token), cancellationToken),
            activity => activity.Execution.ActivityExecutionId,
            3,
            "activity");
        var expectedActivities = bundles.SelectMany(bundle => bundle.Activities).OrderBy(activity => activity.Execution.ActivityExecutionId, StringComparer.Ordinal).ToArray();
        RequireActivities(activities, expectedActivities);

        var durableValues = await ReadPagesAsync(
            token => client.DurableValues.ListPageAsync(new DurableValueStatePageQuery(executionId, 2, token), cancellationToken),
            value => value.DurableValueId,
            2,
            "durable-value");
        var expectedValues = bundles.SelectMany(bundle => bundle.DurableValues).OrderBy(value => value.DurableValueId, StringComparer.Ordinal).ToArray();
        RequireDurableValues(durableValues, expectedValues);

        var outbox = await client.Outbox.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now.AddDays(1), CheckpointsPerExecution * OutboxEntriesPerCheckpoint, executionId), cancellationToken);
        var expectedOutbox = bundles.SelectMany(bundle => bundle.Outbox).OrderBy(item => item.OutboxItemId, StringComparer.Ordinal).ToArray();
        RequireOutbox(outbox, expectedOutbox);

        return new ExecutionRead(activities.Count, durableValues.Count, outbox.Count, outbox.Select(item => item.OutboxItemId).ToArray());
    }

    private static async ValueTask<List<T>> ReadPagesAsync<T>(
        Func<string?, ValueTask<RuntimeStorePage<T>>> read,
        Func<T, string> identity,
        int requestedLimit,
        string name)
    {
        var results = new List<T>();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;
        do
        {
            var page = await read(token);
            if (page is null || page.Items.Count > requestedLimit ||
                page.Items.Count == 0 && page.NextContinuationToken is not null ||
                page.Items.Any(item => string.IsNullOrWhiteSpace(identity(item))) ||
                page.Items.Select(identity).Distinct(StringComparer.Ordinal).Count() != page.Items.Count ||
                page.NextContinuationToken is not null && !tokens.Add(page.NextContinuationToken))
                throw new InvalidOperationException($"The {name} continuation traversal returned an invalid public page.");
            results.AddRange(page.Items);
            token = page.NextContinuationToken;
        } while (token is not null);
        if (results.Select(identity).Distinct(StringComparer.Ordinal).Count() != results.Count)
            throw new InvalidOperationException($"The {name} continuation traversal returned duplicate identities.");
        return results;
    }

    private static void RequireActivities(IReadOnlyList<ActivityExecutionState> actual, IReadOnlyList<ActivityExecutionState> expected)
    {
        if (actual.Count != expected.Count || !actual.Select(activity => activity.Execution.ActivityExecutionId).SequenceEqual(expected.Select(activity => activity.Execution.ActivityExecutionId), StringComparer.Ordinal) ||
            actual.Zip(expected).Any(pair => pair.First.Execution != pair.Second.Execution || pair.First.Metadata.GetValueOrDefault("payload") != pair.Second.Metadata.GetValueOrDefault("payload")))
            throw new InvalidOperationException("A public activity reread did not match the exact committed activity bundle.");
    }

    private static void RequireDurableValues(IReadOnlyList<DurableValueState> actual, IReadOnlyList<DurableValueState> expected)
    {
        if (actual.Count != expected.Count || !actual.Select(value => value.DurableValueId).SequenceEqual(expected.Select(value => value.DurableValueId), StringComparer.Ordinal) ||
            actual.Zip(expected).Any(pair => pair.First.WorkflowExecutionId != pair.Second.WorkflowExecutionId ||
                !JsonElement.DeepEquals(pair.First.InlineValue!.Value, pair.Second.InlineValue!.Value)))
            throw new InvalidOperationException("A public durable-value reread did not match the exact committed value bundle.");
    }

    private static void RequireOutbox(IReadOnlyCollection<RuntimePostCommitOutboxItem> actual, IReadOnlyList<RuntimePostCommitOutboxItem> expected)
    {
        var returnedItems = actual.ToArray();
        if (returnedItems.Length != expected.Count || returnedItems.Select(item => item.OutboxItemId).Distinct(StringComparer.Ordinal).Count() != expected.Count)
            throw new InvalidOperationException("A public outbox reread did not match the exact committed work cardinality or identities.");
        foreach (var (returned, committed) in returnedItems.Zip(expected))
        {
            if (returned.OutboxItemId != committed.OutboxItemId || returned.Status != committed.Status ||
                returned.RecordedAt != committed.RecordedAt || returned.AvailableAt != committed.AvailableAt ||
                returned.Intent.IntentId != committed.Intent.IntentId || returned.Intent.WorkflowExecutionId != committed.Intent.WorkflowExecutionId ||
                returned.Intent.Kind != committed.Intent.Kind || returned.Intent.IdempotencyKey != committed.Intent.IdempotencyKey ||
                returned.Intent.ActivityExecutionId != committed.Intent.ActivityExecutionId ||
                returned.Intent.Payload.HasValue != committed.Intent.Payload.HasValue ||
                returned.Intent.Payload is { } returnedPayload && committed.Intent.Payload is { } committedPayload && !JsonElement.DeepEquals(returnedPayload, committedPayload))
                throw new InvalidOperationException("A public outbox reread did not match the exact committed continuation work facts.");
        }
    }

    private static ExpectedBundle CreateBundle(int index, RuntimeExecutionFence fence, WorkflowExecutableIdentity executable, string? suffix = null)
    {
        var executionId = ExecutionIdFor(index % CheckpointCount);
        var checkpointId = suffix is null ? CheckpointId(index) : $"checkpoint-stale-{suffix}";
        var payload = Payload(checkpointId);
        var occurredAt = Now.AddSeconds(index);
        var workflow = new WorkflowExecutionState(executionId, executable, WorkflowExecutionStatus.Running, null, Now, Now, occurredAt, null, null, null, "tenant-checkpoint", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["checkpoint"] = checkpointId,
            ["payload"] = payload.GetRawText()
        });
        var activities = Enumerable.Range(0, ActivityChangesPerCheckpoint).Select(activity => new ActivityExecutionState(
            new ActivityExecution($"activity-{checkpointId}-{activity:D2}", executionId, $"node-{activity:D2}", $"authored-{activity:D2}", "Elsa.Checkpoint", "1.0.0"),
            ActivityExecutionStatus.Running, null, 1, occurredAt, occurredAt, null, null, null, null, null,
            ActivitySchedulingProvenance.From(executionId, null, null, null, null, null, null, "checkpoint"), null, [], [], 0, 0,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["payload"] = payload.GetRawText() })).ToArray();
        var values = Enumerable.Range(0, DurableValueChangesPerCheckpoint).Select(value => new DurableValueState(
            $"value-{checkpointId}-{value:D2}", executionId, $"value-{value:D2}", new RuntimeValueTypeDescriptor("json", "checkpoint", null),
            DurableValueLifecycle.Instance, DurableValueStorage.Inline, payload, null, activities[value].Execution.ActivityExecutionId, occurredAt,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["checkpoint"] = checkpointId })).ToArray();
        var intents = Enumerable.Range(0, OutboxEntriesPerCheckpoint).Select(intent => new RuntimePostCommitIntent(
            $"intent-{checkpointId}-{intent:D2}", executionId, "runtime-checkpoint-continuation", occurredAt, activities[intent].Execution.ActivityExecutionId,
            $"intent-{checkpointId}-{intent:D2}", payload)).ToArray();
        var changes = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(executionId, RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()), null,
            activities.Select(activity => new RuntimeStateChange<ActivityExecutionState>(activity.Execution.ActivityExecutionId, RuntimeStateChangeOperation.Upsert, activity, new Dictionary<string, string>())).ToArray(),
            [], values.Select(value => new RuntimeStateChange<DurableValueState>(value.DurableValueId, RuntimeStateChangeOperation.Upsert, value, new Dictionary<string, string>())).ToArray(), [], []);
        var commit = new RuntimeCheckpointCommit(checkpointId, new RuntimeCheckpoint(checkpointId, "runtime-checkpoint-commit", executionId, occurredAt, activities.Select(activity => activity.Execution.ActivityExecutionId).ToArray(), new Dictionary<string, string>()), changes, intents, new Dictionary<string, string>()) { ExpectedFence = fence };
        commit = commit with { StateChanges = commit.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(commit)) };
        return new ExpectedBundle(index, executionId, workflow, activities, values, commit, commit.StateChanges.PostCommitOutbox.Select(change => change.State).ToArray());
    }

    private static WorkflowExecutable CreateExecutable()
    {
        var identity = new WorkflowExecutableIdentity("artifact-checkpoint-workload", "definition-checkpoint-workload", "version-checkpoint-workload", "1.0.0", "sha256:checkpoint-workload");
        return new WorkflowExecutable(identity,
            new ExecutableNode("checkpoint-root", "checkpoint-root", "Elsa.Checkpoint", "1.0.0", "checkpoint", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static JsonElement Payload(string checkpointId)
    {
        var prefix = $"{{\"checkpoint\":\"{checkpointId}\",\"payload\":\"";
        var padding = PayloadBytes - Encoding.UTF8.GetByteCount(prefix) - Encoding.UTF8.GetByteCount("\"}");
        if (padding < 0)
            throw new InvalidOperationException("The checkpoint payload identity exceeds the frozen payload size.");
        var json = prefix + new string('x', padding) + "\"}";
        if (Encoding.UTF8.GetByteCount(json) != PayloadBytes)
            throw new InvalidOperationException("The checkpoint payload no longer has the frozen byte length.");
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string RequireAcknowledgement(RuntimeCheckpointCommitStoreResult? result, ExpectedBundle bundle, string operation)
    {
        var expectedIds = bundle.Outbox.Select(item => item.OutboxItemId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (result is null || !result.PendingPostCommitWorkIds.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(expectedIds, StringComparer.Ordinal))
            throw new InvalidOperationException($"The {operation} acknowledgement did not expose the exact pending public work identities.");
        var prefixes = result.PendingPostCommitWorkIds
            .Select(id => id.Split(':', 2)[0])
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (prefixes.Length != 1 || !StringComparer.Ordinal.Equals(prefixes[0], bundle.Commit.CommitId))
            throw new InvalidOperationException($"The {operation} acknowledgement did not bind its returned work identities to one exact commit.");
        return prefixes[0];
    }

    private static void RequireIndependentClients(RuntimeCheckpointCommitClients? clients)
    {
        if (clients is null || clients.Primary is null || clients.Secondary is null || !IsDistinctClient(clients.Primary, clients.Secondary))
            throw new InvalidOperationException("The checkpoint workload adapter must open two distinct public-store clients over shared backing.");
    }

    private static bool IsDistinctClient(RuntimeCheckpointCommitClient first, RuntimeCheckpointCommitClient second) =>
        !ReferenceEquals(first, second) &&
        !ReferenceEquals(first.Checkpoints, second.Checkpoints) &&
        !ReferenceEquals(first.Ownership, second.Ownership) &&
        !ReferenceEquals(first.Executables, second.Executables) &&
        !ReferenceEquals(first.WorkflowExecutions, second.WorkflowExecutions) &&
        !ReferenceEquals(first.Activities, second.Activities) &&
        !ReferenceEquals(first.DurableValues, second.DurableValues) &&
        !ReferenceEquals(first.Outbox, second.Outbox);

    private static void RequireExecutable(WorkflowExecutable? actual, WorkflowExecutable expected)
    {
        if (actual is null || actual.Identity != expected.Identity || actual.CreatedAt != expected.CreatedAt || actual.RootActivity.ExecutableNodeId != expected.RootActivity.ExecutableNodeId)
            throw new InvalidOperationException("The checkpoint workload executable FindAsync response did not expose the saved immutable executable.");
    }

    private static void RequireCurrentLease(RuntimeExecutionLease lease, string executionId)
    {
        if (!StringComparer.Ordinal.Equals(lease.WorkflowExecutionId, executionId) ||
            string.IsNullOrWhiteSpace(lease.LeaseId) ||
            string.IsNullOrWhiteSpace(lease.OwnerId) ||
            lease.FencingToken <= 0 ||
            lease.IsExpired(DateTimeOffset.UtcNow))
            throw new InvalidOperationException("The checkpoint workload ownership service did not return a current fence for the requested execution.");
    }

    private static IEnumerable<string> ExecutionIds() => Enumerable.Range(0, ExecutionCount).Select(index => ExecutionIdFor(index));
    private static string ExecutionIdFor(int checkpointIndex) => $"execution-{checkpointIndex % ExecutionCount:D4}";
    private static string CheckpointId(int index) => $"checkpoint-{index:D4}";
    private static IReadOnlyList<ExpectedBundle> BundlesForExecution(IEnumerable<ExpectedBundle> bundles, string executionId) => bundles.Where(bundle => bundle.ExecutionId == executionId).OrderBy(bundle => bundle.Index).ToArray();
    private static IReadOnlyList<string> OutboxIdsForExecution(IEnumerable<ExpectedBundle> bundles, string executionId) => BundlesForExecution(bundles, executionId).SelectMany(bundle => bundle.Outbox).Select(item => item.OutboxItemId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
    private static int Int(string name) => (int)Scenario.Parameters[name];
    private static bool ObservationsMatch(IReadOnlyDictionary<string, object> actual, IReadOnlyDictionary<string, object> expected) => actual.Count == expected.Count && actual.All(pair => expected.TryGetValue(pair.Key, out var value) && JsonSerializer.Serialize(pair.Value, CanonicalJsonOptions) == JsonSerializer.Serialize(value, CanonicalJsonOptions));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record ExpectedBundle(int Index, string ExecutionId, WorkflowExecutionState Workflow, IReadOnlyList<ActivityExecutionState> Activities, IReadOnlyList<DurableValueState> DurableValues, RuntimeCheckpointCommit Commit, IReadOnlyList<RuntimePostCommitOutboxItem> Outbox);
    private sealed record ExecutionRead(int ActivityCount, int DurableValueCount, int OutboxCount, IReadOnlyList<string> OutboxIds);
    private sealed record BundleRead(int ActivityCount, int DurableValueCount, int OutboxCount);
}

/// <summary>Creates distinct public runtime-store clients over one adapter-owned backing.</summary>
public interface IRuntimeCheckpointCommitWorkloadAdapter
{
    ValueTask<RuntimeCheckpointCommitClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default);
    ValueTask<RuntimeCheckpointCommitClient> ReopenClientAsync(CancellationToken cancellationToken = default);
}

/// <summary>Two independently created public clients sharing the adapter-selected backing.</summary>
public sealed record RuntimeCheckpointCommitClients(RuntimeCheckpointCommitClient Primary, RuntimeCheckpointCommitClient Secondary);

/// <summary>The public runtime-store contracts required by the checkpoint correctness baseline.</summary>
public sealed record RuntimeCheckpointCommitClient(
    IRuntimeCheckpointCommitStore Checkpoints,
    IRuntimeExecutionOwnershipService Ownership,
    IWorkflowExecutableStore Executables,
    IWorkflowExecutionStateStore WorkflowExecutions,
    IActivityExecutionStateStore Activities,
    IDurableValueStateStore DurableValues,
    IRuntimePostCommitOutboxStore Outbox);

public sealed record RuntimeCheckpointCommitWorkloadResult(
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, object> ObservableResults);
