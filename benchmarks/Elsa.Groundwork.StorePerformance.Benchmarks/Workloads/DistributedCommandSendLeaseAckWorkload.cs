using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>Executes the catalog-owned command-transport correctness baseline through public contracts only.</summary>
public sealed class DistributedCommandSendLeaseAckWorkload
{
    private static readonly ReproducibleWorkloadScenario Scenario = ReproducibleWorkloadScenarioCatalog.Get(WorkloadId);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public const string WorkloadId = "command-send-lease-ack";
    public const string ExpectedInputFingerprint = "a108e41c890af94ee37d610817e2c4d6339451cbfbbd0e33e0bd794d0d1af5b1";
    public const string ExpectedResultDigest = "86439fbc13d29102d02615ee98a5beb53e008e673f6523681e3ee2d926d3389f";
    public static int WorkflowCount => Int("workflowCount");
    public static int CommandsPerWorkflow => Int("commandsPerWorkflow");
    public static int BatchSize => Int("batchSize");
    public static int ConcurrentSenders => Int("concurrentSenders");
    public static int ConcurrentLeasers => Int("concurrentLeasers");
    public static DateTimeOffset FixedNowUtc => DateTimeOffset.Parse((string)Scenario.Parameters["fixedNowUtc"], System.Globalization.CultureInfo.InvariantCulture);
    private static TimeSpan Visibility => TimeSpan.FromSeconds(Int("visibilityTimeoutSeconds"));

    public async ValueTask<DistributedCommandSendLeaseAckResult> ExecuteAsync(ICommandTransportWorkloadAdapter adapter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ValidateScenario();
        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        if (clients is null || ReferenceEquals(clients.Primary, clients.Secondary))
            throw new InvalidOperationException("The command workload adapter must open two distinct public transport clients.");

        var operations = new List<string>();
        var sentIdentities = await SeedAsync(clients, cancellationToken);
        operations.Add("seed-command-streams");

        // The two synchronized sends replace two ordinary sends in a dedicated non-primary stream; they never perturb the primary golden batch.
        await RequireDedicatedConcurrentSendsAsync(clients, sentIdentities, cancellationToken);
        if (sentIdentities.Count != WorkflowCount * CommandsPerWorkflow)
            throw new InvalidOperationException("The public command send acknowledgements did not expose one unique transport identity per seeded command.");
        operations.Add("send-concurrent-commands");

        var first = RequireLeaseWave(
            await LeaseWaveAsync(clients, FixedNowUtc, "lease-first", cancellationToken),
            expectedToken: 1,
            FixedNowUtc);
        operations.Add("lease-visible-bounded-batch");

        var redeliveryNow = FixedNowUtc.Add(Visibility).AddSeconds(1);
        operations.Add("advance-past-visibility-expiry");
        var successors = RequireLeaseWave(
            await LeaseWaveAsync(clients, redeliveryNow, "lease-successor", cancellationToken),
            expectedToken: 2,
            redeliveryNow);
        if (!successors.Select(tuple => tuple.Item.TransportItemId).SequenceEqual(first.Select(tuple => tuple.Item.TransportItemId), StringComparer.Ordinal))
            throw new InvalidOperationException("The successor command leases did not redeliver the exact expired first-generation batch.");
        operations.Add("re-lease-current-batch");

        foreach (var tuple in first)
            if (await tuple.Client.AckAsync(PrimaryWorkflowId, tuple.Item.TransportItemId, tuple.Item.LeasedByOwnerId!, tuple.Item.LeaseToken!.Value, redeliveryNow, cancellationToken))
                throw new InvalidOperationException("The command transport accepted a stale acknowledgement.");
        for (var index = 0; index < successors.Count; index++)
        {
            var successor = successors[index];
            var firstGenerationToken = first[index].Item.LeaseToken!.Value;
            if (await successor.Client.AckAsync(
                    PrimaryWorkflowId,
                    successor.Item.TransportItemId,
                    successor.Item.LeasedByOwnerId!,
                    firstGenerationToken,
                    redeliveryNow,
                    cancellationToken))
            {
                throw new InvalidOperationException("The command transport accepted a stale token from its current lease owner.");
            }
        }
        operations.Add("attempt-stale-acknowledgement");

        foreach (var tuple in successors)
            if (!await tuple.Client.AckAsync(PrimaryWorkflowId, tuple.Item.TransportItemId, tuple.Item.LeasedByOwnerId!, tuple.Item.LeaseToken!.Value, redeliveryNow, cancellationToken))
                throw new InvalidOperationException("The command transport rejected a current acknowledgement.");
        operations.Add("acknowledge-current-batch");

        var reopened = await adapter.ReopenClientAsync(cancellationToken);
        if (reopened is null || ReferenceEquals(reopened, clients.Primary) || ReferenceEquals(reopened, clients.Secondary))
            throw new InvalidOperationException("The command workload adapter must reopen a distinct public transport client.");
        var visible = await reopened.ListPendingExecutionIdsAsync(redeliveryNow, WorkflowCount, cancellationToken);
        var expectedVisible = Enumerable.Range(0, WorkflowCount).Select(WorkflowId).ToArray();
        if (!visible.SequenceEqual(expectedVisible, StringComparer.Ordinal))
            throw new InvalidOperationException("The reopened command transport did not expose the exact visible workflow list.");
        var pending = 0;
        for (var workflow = 0; workflow < WorkflowCount; workflow++)
        {
            var count = await reopened.CountPendingAsync(WorkflowId(workflow), cancellationToken);
            var expectedCount = workflow == 0 ? CommandsPerWorkflow - BatchSize : CommandsPerWorkflow;
            if (count != expectedCount)
                throw new InvalidOperationException("The reopened command transport did not expose the exact per-workflow pending count.");
            pending += count;
        }
        if (pending != WorkflowCount * CommandsPerWorkflow - BatchSize)
            throw new InvalidOperationException("The reopened command transport did not expose the exact pending count.");
        operations.Add("reopen-and-count-pending");

        var observations = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["acknowledgedCount"] = successors.Count,
            ["leasedIdentityDigest"] = Hash(JsonSerializer.Serialize(first.Select(tuple => tuple.Item.Envelope.EnvelopeId), Json)),
            ["pendingAfterReopen"] = pending,
            ["redeliveredCount"] = successors.Count,
            ["staleAcknowledgementRejected"] = true
        };
        if (!Matches(observations, Scenario.CreateExpectedObservations()) || !operations.SequenceEqual(Scenario.OperationSequence, StringComparer.Ordinal))
            throw new InvalidOperationException("The command workload observations no longer match the frozen catalog contract.");
        var digest = ReproducibleWorkloadScenarioCatalog.Hash(ReproducibleWorkloadScenarioCatalog.Serialize(new { WorkloadId, Scenario.ScenarioId, InputFingerprint = Scenario.ComputeInputFingerprint(), Operations = operations, ObservableResults = observations }));
        if (digest != ExpectedResultDigest)
            throw new InvalidOperationException("The command workload result digest no longer matches its ratified value.");
        return new(Scenario.ComputeInputFingerprint(), digest, operations, observations);
    }

    private static async ValueTask<HashSet<string>> SeedAsync(CommandTransportClients clients, CancellationToken cancellationToken)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var workflow in Enumerable.Range(0, WorkflowCount))
            foreach (var command in Enumerable.Range(0, CommandsPerWorkflow))
            {
                if (workflow == 1 && command is 62 or 63)
                    continue;
                var item = await clients.Primary.SendAsync(WorkflowId(workflow), Envelope(WorkflowId(workflow), command), FixedNowUtc, cancellationToken);
                RequireSentItem(item, WorkflowId(workflow), command, command + 1);
                if (!identities.Add(item.TransportItemId))
                    throw new InvalidOperationException("The public command send acknowledgement reused a transport identity.");
            }
        return identities;
    }

    private static async ValueTask RequireDedicatedConcurrentSendsAsync(
        CommandTransportClients clients,
        ISet<string> identities,
        CancellationToken cancellationToken)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;
        var calls = new[] { (clients.Primary, 62), (clients.Secondary, 63) }.Select(async tuple =>
        {
            if (Interlocked.Increment(ref readyCount) == ConcurrentSenders) ready.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            var workflowId = WorkflowId(1);
            var item = await tuple.Item1.SendAsync(workflowId, Envelope(workflowId, tuple.Item2), FixedNowUtc, cancellationToken);
            RequireSentItem(item, workflowId, tuple.Item2);
            return item;
        }).ToArray();
        await ready.Task.WaitAsync(cancellationToken);
        release.TrySetResult();
        var sent = await Task.WhenAll(calls);
        if (!sent.Select(item => item.Sequence).Order().SequenceEqual([63L, 64L]) ||
            sent.Select(item => item.Envelope.EnvelopeId).Order(StringComparer.Ordinal).SequenceEqual([CommandId(62), CommandId(63)], StringComparer.Ordinal) is false)
        {
            throw new InvalidOperationException("The synchronized public sends did not append the exact dedicated-stream command pair.");
        }
        foreach (var item in sent)
            if (!identities.Add(item.TransportItemId))
                throw new InvalidOperationException("The synchronized public sends reused a transport identity.");
    }

    private static void RequireSentItem(
        ExecutionCommandTransportItem item,
        string workflowId,
        int command,
        long? expectedSequence = null)
    {
        if (string.IsNullOrWhiteSpace(item.TransportItemId) || item.WorkflowExecutionId != workflowId ||
            !SameEnvelope(item.Envelope, Envelope(workflowId, command)) ||
            item.Sequence <= 0 || expectedSequence is not null && item.Sequence != expectedSequence ||
            item.EnqueuedAt != FixedNowUtc || item.DeliveryAttemptCount != 0 ||
            item.LeasedByOwnerId is not null || item.LeaseExpiresAt is not null || item.LeaseToken is not null)
        {
            throw new InvalidOperationException("The public send acknowledgement did not preserve command identity and sequence.");
        }
    }

    private static async ValueTask<IReadOnlyList<LeaseAttempt>> LeaseWaveAsync(CommandTransportClients clients, DateTimeOffset now, string prefix, CancellationToken cancellationToken)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0; var take = BatchSize / ConcurrentLeasers;
        var callers = new[] { clients.Primary, clients.Secondary }.Select(async (client, index) =>
        {
            if (Interlocked.Increment(ref readyCount) == ConcurrentLeasers) ready.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            var owner = $"{prefix}-{index}";
            var items = await client.LeaseAsync(PrimaryWorkflowId, owner, now, Visibility, take, cancellationToken);
            return new LeaseAttempt(client, owner, items);
        }).ToArray();
        await ready.Task.WaitAsync(cancellationToken);
        release.TrySetResult();
        return await Task.WhenAll(callers);
    }

    private static IReadOnlyList<LeaseTuple> RequireLeaseWave(
        IReadOnlyList<LeaseAttempt> attempts,
        long expectedToken,
        DateTimeOffset now)
    {
        var take = BatchSize / ConcurrentLeasers;
        if (attempts.Count != ConcurrentLeasers || attempts.Any(attempt => attempt.Items.Count != take) ||
            attempts.Any(attempt => !attempt.Items.Select(item => item.Sequence).SequenceEqual(attempt.Items.Select(item => item.Sequence).Order())))
        {
            throw new InvalidOperationException("Each synchronized command leaser must return its exact bounded ordered share.");
        }
        var leases = attempts
            .SelectMany(attempt => attempt.Items.Select(item => new LeaseTuple(attempt.Client, attempt.Owner, item)))
            .OrderBy(tuple => tuple.Item.Sequence)
            .ToArray();
        if (leases.Length != BatchSize || leases.Select(x => x.Item.TransportItemId).Distinct(StringComparer.Ordinal).Count() != BatchSize ||
            !leases.Select(x => x.Item.Sequence).SequenceEqual(Enumerable.Range(1, BatchSize).Select(index => (long)index)) ||
            !leases.Select(x => x.Item.Envelope.EnvelopeId).SequenceEqual(Enumerable.Range(0, BatchSize).Select(CommandId), StringComparer.Ordinal) ||
            leases.Any(x => x.Item.WorkflowExecutionId != PrimaryWorkflowId || x.Item.LeasedByOwnerId != x.Owner ||
                            x.Item.LeaseToken != expectedToken || x.Item.DeliveryAttemptCount != expectedToken ||
                            x.Item.LeaseExpiresAt != now.Add(Visibility) || x.Item.EnqueuedAt != FixedNowUtc ||
                            !SameEnvelope(x.Item.Envelope, Envelope(PrimaryWorkflowId, checked((int)x.Item.Sequence - 1)))))
        {
            throw new InvalidOperationException("The public command lease did not return the exact bounded ordered current batch.");
        }
        return leases;
    }

    private static void ValidateScenario()
    {
        if (Scenario.Version != "1.1.0" || Scenario.ScenarioId != "distributed-command-send-lease-ack" || Scenario.Seed != "spec094-command-send-lease-ack-v1.1" ||
            Scenario.ComputeInputFingerprint() != ExpectedInputFingerprint || Scenario.ComputeResultDigest() != ExpectedResultDigest ||
            WorkflowCount != 128 || CommandsPerWorkflow != 64 || BatchSize != 16 ||
            ConcurrentSenders != 2 || ConcurrentLeasers != 2 || Visibility != TimeSpan.FromSeconds(30) ||
            FixedNowUtc != new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero) ||
            !StringComparer.Ordinal.Equals(Scenario.Parameters["timedSetup"], "excluded"))
            throw new InvalidOperationException("The command transport scenario no longer matches its frozen v1.1 catalog contract.");
    }
    private static WorkflowExecutionCommandEnvelope Envelope(string workflowId, int command) => new(CommandId(command), workflowId,
        new WorkflowExecutionCommand($"command-{command:D4}", workflowId, WorkflowExecutionCommandKind.RunSchedulerWork, FixedNowUtc, null, new Dictionary<string, string>()),
        $"idempotency-{workflowId}-{command:D4}", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, FixedNowUtc);
    private static string WorkflowId(int index) => $"command-workflow-{index:D4}";
    private static string PrimaryWorkflowId => WorkflowId(0);
    private static string CommandId(int index) => $"command-{index:D4}";
    private static bool SameEnvelope(WorkflowExecutionCommandEnvelope actual, WorkflowExecutionCommandEnvelope expected) =>
        actual.EnvelopeId == expected.EnvelopeId && actual.WorkflowExecutionId == expected.WorkflowExecutionId &&
        actual.IdempotencyKey == expected.IdempotencyKey && actual.DeliveryMode == expected.DeliveryMode &&
        actual.Sequence == expected.Sequence && actual.EnqueuedAt == expected.EnqueuedAt &&
        actual.Partition.Value == expected.Partition.Value && SameCommand(actual.Command, expected.Command) &&
        actual.Metadata.Count == expected.Metadata.Count &&
        actual.Metadata.All(pair => expected.Metadata.TryGetValue(pair.Key, out var value) && value == pair.Value);
    private static bool SameCommand(WorkflowExecutionCommand actual, WorkflowExecutionCommand expected) =>
        actual.CommandId == expected.CommandId && actual.WorkflowExecutionId == expected.WorkflowExecutionId &&
        actual.Kind == expected.Kind && actual.EnqueuedAt == expected.EnqueuedAt &&
        (actual.Payload is null ? expected.Payload is null :
            expected.Payload is not null && JsonElement.DeepEquals(actual.Payload.Value, expected.Payload.Value)) &&
        actual.Metadata.Count == expected.Metadata.Count &&
        actual.Metadata.All(pair => expected.Metadata.TryGetValue(pair.Key, out var value) && value == pair.Value);
    private static int Int(string name) => (int)Scenario.Parameters[name];
    private static bool Matches(IReadOnlyDictionary<string, object> left, IReadOnlyDictionary<string, object> right) => left.Count == right.Count && left.All(p => right.TryGetValue(p.Key, out var value) && JsonSerializer.Serialize(p.Value, Json) == JsonSerializer.Serialize(value, Json));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private sealed record LeaseTuple(IExecutionCommandTransport Client, string Owner, ExecutionCommandTransportItem Item);
    private sealed record LeaseAttempt(IExecutionCommandTransport Client, string Owner, IReadOnlyList<ExecutionCommandTransportItem> Items);
}

public interface ICommandTransportWorkloadAdapter
{
    ValueTask<CommandTransportClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default);
    ValueTask<IExecutionCommandTransport> ReopenClientAsync(CancellationToken cancellationToken = default);
}
public sealed record CommandTransportClients(IExecutionCommandTransport Primary, IExecutionCommandTransport Secondary);
public sealed record DistributedCommandSendLeaseAckResult(string InputFingerprint, string ResultDigest, IReadOnlyList<string> ObservableOperations, IReadOnlyDictionary<string, object> ObservableResults);
