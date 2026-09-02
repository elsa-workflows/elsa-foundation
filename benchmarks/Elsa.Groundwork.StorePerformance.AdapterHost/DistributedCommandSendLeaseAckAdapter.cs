using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// The Groundwork v2 execution-command transport adapter. The frozen workload owns the correctness
/// scenario; this leaf composes the public <see cref="IExecutionCommandTransport"/> contract over a
/// provider-backed distributed-runtime scope and supplies bounded public transport operations for the
/// measurement harness.
/// </summary>
internal sealed class DistributedCommandSendLeaseAckAdapter(
    RunRequest request,
    string connectionString,
    string outputDirectory)
    : IBenchmarkAdapter, ICommandTransportWorkloadAdapter
{
    internal const string PhysicalForm = "dedicated-command-transport-documents";

    private RuntimeStoreComposition? composition;
    private ProviderProbe.Result? observedProvider;
    private IReadOnlyList<IBenchmarkOperation>? operations;
    private readonly string persistenceScope = PersistenceScopeFor(request);

    public IProviderRoundTripObserver? RoundTripObserver => composition?.Observer;

    public IReadOnlyList<IBenchmarkOperation> Operations =>
        operations ?? throw new PerformanceContractException(
            "The command-send-lease-ack operations were requested before correctness preparation completed.");

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (composition is not null)
            return;

        // Probe before composing the long-lived runtime connection so the correctness evidence records
        // the provider handshake used to admit the actual Groundwork transport store.
        var observed = await ProviderProbe.ReadAsync(request.Provider, connectionString, cancellationToken);
        var created = await RuntimeStoreComposition.CreateAsync(
            request.Provider,
            connectionString,
            persistenceScope,
            cancellationToken,
            includeDistributedRuntimeStores: true);
        observedProvider = observed;
        composition = created;
    }

    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        Require();
        var document = NativePlanEvidenceStaging.PublishInto(outputDirectory, request);
        var observed = observedProvider ?? throw new PerformanceContractException(
            "The command-send-lease-ack adapter has no provider handshake; PrepareAsync must run first.");
        var result = await new DistributedCommandSendLeaseAckWorkload().ExecuteAsync(this, cancellationToken);
        operations = await PrepareMeasuredOperationsAsync(cancellationToken);

        return new CorrectnessEvidence(
            result.ResultDigest,
            observed.Version,
            observed.Topology,
            observed.Configuration,
            new NativePlanEvidence(
                request.NativePlanIdentity,
                request.NativePlanEvidenceReference,
                request.NativePlanContentSha256,
                document.Routes));
    }

    public ValueTask<CommandTransportClients> OpenIndependentClientsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = Require();
        return ValueTask.FromResult(new CommandTransportClients(
            new ScopedCommandTransport(active.CreateCommandTransportClient(), persistenceScope),
            new ScopedCommandTransport(active.CreateCommandTransportClient(), persistenceScope)));
    }

    public ValueTask<IExecutionCommandTransport> ReopenClientAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IExecutionCommandTransport>(
            new ScopedCommandTransport(Require().CreateCommandTransportClient(), persistenceScope));
    }

    public async ValueTask DisposeAsync()
    {
        if (composition is not null)
            await composition.DisposeAsync();
        composition = null;
        observedProvider = null;
        operations = null;
    }

    private RuntimeStoreComposition Require() =>
        composition ?? throw new PerformanceContractException(
            "The command-send-lease-ack adapter has no composed backing; PrepareAsync must run first.");

    /// <summary>
    /// The workload itself is the frozen correctness vector and intentionally does not define measured
    /// operation delegates. These six operations preserve its public transport phases while establishing
    /// any mutable fixture before timing starts. The time-only seed/expiry phases are not timed as provider
    /// operations, matching the placement adapter's treatment of its equivalent phases.
    /// </summary>
    private async ValueTask<IReadOnlyList<IBenchmarkOperation>> PrepareMeasuredOperationsAsync(
        CancellationToken cancellationToken)
    {
        var scenario = ReproducibleWorkloadScenarioCatalog.Get(
            DistributedCommandSendLeaseAckWorkload.WorkloadId);
        var clients = await OpenIndependentClientsAsync(cancellationToken);
        var reopened = await ReopenClientAsync(cancellationToken);
        var visibility = TimeSpan.FromSeconds(30);
        var now = DateTimeOffset.Parse(
            "2026-07-20T10:00:00Z",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        var current = new Dictionary<long, ExecutionCommandTransportItem>();
        var expired = now.Add(visibility).AddSeconds(1);

        return
        [
            Operation(
                scenario.OperationSequence[1],
                (invocation, _) => ValueTask.CompletedTask,
                (invocation, token) => SendPairAsync(clients, MeasuredWorkflow("send", invocation), now, token)),
            Operation(
                scenario.OperationSequence[2],
                (invocation, token) => SeedOneAsync(clients.Primary, MeasuredWorkflow("lease", invocation), now, token),
                async (invocation, token) =>
                {
                    _ = await LeaseOneAsync(clients.Primary, MeasuredWorkflow("lease", invocation), "lease", now, visibility, token);
                }),
            Operation(
                scenario.OperationSequence[4],
                async (invocation, token) =>
                {
                    var workflowId = MeasuredWorkflow("redelivery", invocation);
                    await SeedOneAsync(clients.Primary, workflowId, now, token);
                    current[invocation] = await LeaseOneAsync(clients.Primary, workflowId, "first", now, visibility, token);
                },
                async (invocation, token) =>
                {
                    _ = await LeaseOneAsync(clients.Primary, MeasuredWorkflow("redelivery", invocation), "successor", expired, visibility, token);
                }),
            Operation(
                scenario.OperationSequence[5],
                async (invocation, token) =>
                {
                    var workflowId = MeasuredWorkflow("stale", invocation);
                    await SeedOneAsync(clients.Primary, workflowId, now, token);
                    var first = await LeaseOneAsync(clients.Primary, workflowId, "first", now, visibility, token);
                    _ = await LeaseOneAsync(clients.Primary, workflowId, "successor", expired, visibility, token);
                    current[invocation] = first;
                },
                async (invocation, token) =>
                {
                    var item = current[invocation];
                    _ = await clients.Primary.AckAsync(
                        item.WorkflowExecutionId,
                        item.TransportItemId,
                        item.LeasedByOwnerId!,
                        item.LeaseToken!.Value,
                        expired,
                        token);
                }),
            Operation(
                scenario.OperationSequence[6],
                async (invocation, token) =>
                {
                    var workflowId = MeasuredWorkflow("ack", invocation);
                    await SeedOneAsync(clients.Primary, workflowId, now, token);
                    current[invocation] = await LeaseOneAsync(clients.Primary, workflowId, "current", now, visibility, token);
                },
                async (invocation, token) =>
                {
                    var item = current[invocation];
                    _ = await clients.Primary.AckAsync(
                        item.WorkflowExecutionId,
                        item.TransportItemId,
                        item.LeasedByOwnerId!,
                        item.LeaseToken!.Value,
                        now,
                        token);
                }),
            Operation(
                scenario.OperationSequence[7],
                (invocation, token) => SeedOneAsync(clients.Primary, MeasuredWorkflow("pending", invocation), now, token),
                async (invocation, token) =>
                {
                    var workflowId = MeasuredWorkflow("pending", invocation);
                    _ = await reopened.ListPendingExecutionIdsAsync(now, 1, token);
                    _ = await reopened.CountPendingAsync(workflowId, token);
                })
        ];
    }

    private static IBenchmarkOperation Operation(
        string id,
        Func<long, CancellationToken, ValueTask> prepare,
        Func<long, CancellationToken, ValueTask> invoke) =>
        new TransportOperation(id, prepare, invoke);

    private static async ValueTask SendPairAsync(
        CommandTransportClients clients,
        string workflowId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var first = clients.Primary.SendAsync(workflowId, Envelope(workflowId, "pair-0", now), now, cancellationToken).AsTask();
        var second = clients.Secondary.SendAsync(workflowId, Envelope(workflowId, "pair-1", now), now, cancellationToken).AsTask();
        await Task.WhenAll(first, second);
    }

    private static async ValueTask SeedOneAsync(
        IExecutionCommandTransport client,
        string workflowId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        _ = await client.SendAsync(workflowId, Envelope(workflowId, "single", now), now, cancellationToken);

    private static async ValueTask<ExecutionCommandTransportItem> LeaseOneAsync(
        IExecutionCommandTransport client,
        string workflowId,
        string owner,
        DateTimeOffset now,
        TimeSpan visibility,
        CancellationToken cancellationToken)
    {
        var items = await client.LeaseAsync(workflowId, owner, now, visibility, 1, cancellationToken);
        if (items.Count != 1)
            throw new InvalidOperationException($"The command transport measurement fixture expected one lease for '{workflowId}'.");
        return items[0];
    }

    private static WorkflowExecutionCommandEnvelope Envelope(
        string workflowId,
        string commandId,
        DateTimeOffset now) =>
        new(
            commandId,
            workflowId,
            new WorkflowExecutionCommand(
                commandId,
                workflowId,
                WorkflowExecutionCommandKind.RunSchedulerWork,
                now,
                null,
                new Dictionary<string, string>()),
            $"idempotency-{workflowId}-{commandId}",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            now);

    private static string MeasuredWorkflow(string operation, long invocation) =>
        $"command-measured-{operation}-{invocation switch
        {
            < 0 => $"warmup-{-(invocation + 1)}",
            _ => $"measure-{invocation}"
        }}";

    private static string PersistenceScopeFor(RunRequest request)
    {
        var identity = string.Join(
            '|',
            request.ComparisonCohortId,
            request.MeasurementSetId,
            request.WorkloadId,
            request.WorkloadVersion,
            request.Provider,
            request.ProviderVersion,
            request.ProviderTopology,
            string.Join(';', request.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}")),
            request.Adapter,
            request.PhysicalForm,
            request.Scale,
            request.CommitSha,
            request.HarnessAssemblySha256,
            request.CompositionFingerprint,
            request.HostFingerprintSha256,
            request.Seed,
            request.InputFingerprintSha256,
            request.NativePlanIdentity,
            request.NativePlanEvidenceReference,
            request.NativePlanContentSha256,
            request.ProcessKind,
            request.ProcessIndex);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return $"benchmark-command-{digest}";
    }

    private sealed class TransportOperation(
        string id,
        Func<long, CancellationToken, ValueTask> prepare,
        Func<long, CancellationToken, ValueTask> invoke) : IBenchmarkOperation
    {
        public string Id => id;

        public Task PrepareInvocationAsync(long invocation, CancellationToken cancellationToken) =>
            prepare(invocation, cancellationToken).AsTask();

        public Task InvokeAsync(long invocation, CancellationToken cancellationToken) =>
            invoke(invocation, cancellationToken).AsTask();
    }

    /// <summary>
    /// Groundwork binds a storage scope to the transport envelope's partition. The frozen workload's
    /// partition is intentionally the provider-neutral default, so this adapter translates it to its
    /// process-isolated persistence scope only at the provider boundary and maps responses back losslessly.
    /// </summary>
    private sealed class ScopedCommandTransport(
        IExecutionCommandTransport inner,
        string persistenceScope) : IExecutionCommandTransport
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> partitions = new(StringComparer.Ordinal);

        public async ValueTask<ExecutionCommandTransportItem> SendAsync(
            string workflowExecutionId,
            WorkflowExecutionCommandEnvelope envelope,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            partitions[workflowExecutionId] = envelope.Partition.Value;
            var item = await inner.SendAsync(workflowExecutionId, WithPartition(envelope, persistenceScope), now, cancellationToken);
            return WithPartition(item, envelope.Partition);
        }

        public async ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(
            string workflowExecutionId,
            string ownerId,
            DateTimeOffset now,
            TimeSpan leaseDuration,
            int maxItems,
            CancellationToken cancellationToken = default)
        {
            var items = await inner.LeaseAsync(workflowExecutionId, ownerId, now, leaseDuration, maxItems, cancellationToken);
            var partition = new WorkflowExecutionPartition(partitions.GetValueOrDefault(
                workflowExecutionId,
                WorkflowExecutionPartition.DefaultValue));
            return items.Select(item => WithPartition(item, partition)).ToArray();
        }

        public ValueTask<bool> AckAsync(
            string workflowExecutionId,
            string transportItemId,
            string ownerId,
            long leaseToken,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            inner.AckAsync(workflowExecutionId, transportItemId, ownerId, leaseToken, now, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(
            DateTimeOffset now,
            int maxItems,
            CancellationToken cancellationToken = default) =>
            inner.ListPendingExecutionIdsAsync(now, maxItems, cancellationToken);

        public ValueTask<int> CountPendingAsync(
            string workflowExecutionId,
            CancellationToken cancellationToken = default) =>
            inner.CountPendingAsync(workflowExecutionId, cancellationToken);

        private static WorkflowExecutionCommandEnvelope WithPartition(
            WorkflowExecutionCommandEnvelope envelope,
            string partition) =>
            WithPartition(envelope, new WorkflowExecutionPartition(partition));

        private static WorkflowExecutionCommandEnvelope WithPartition(
            WorkflowExecutionCommandEnvelope envelope,
            WorkflowExecutionPartition partition) =>
            new(
                envelope.EnvelopeId,
                envelope.WorkflowExecutionId,
                envelope.Command,
                envelope.IdempotencyKey,
                envelope.DeliveryMode,
                envelope.EnqueuedAt,
                envelope.Sequence,
                envelope.Metadata,
                partition);

        private static ExecutionCommandTransportItem WithPartition(
            ExecutionCommandTransportItem item,
            WorkflowExecutionPartition partition) =>
            new(
                item.TransportItemId,
                item.WorkflowExecutionId,
                WithPartition(item.Envelope, partition),
                item.Sequence,
                item.EnqueuedAt,
                item.DeliveryAttemptCount,
                item.LeasedByOwnerId,
                item.LeaseExpiresAt);
    }
}
