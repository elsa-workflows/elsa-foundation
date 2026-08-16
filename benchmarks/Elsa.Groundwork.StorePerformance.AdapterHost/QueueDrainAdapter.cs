using System.Collections.Concurrent;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>Real public-store leaf for the frozen scheduler queue baseline.</summary>
internal sealed class QueueDrainAdapter : IBenchmarkAdapter, IRuntimeQueueDrainWorkloadAdapter
{
    private const string CorrectnessScope = "queue-correctness";
    private const string MeasuredScope = "queue-measured";
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Visibility = TimeSpan.FromMinutes(1);

    internal static readonly string[] OperationIds =
    [
        "enqueue-work-items",
        "claim-bounded-batch",
        "complete-current-claims",
        "retry-expired-claim",
        "record-and-read-poison-state",
        "attempt-stale-acknowledgement"
    ];

    private readonly RuntimeAdapterInfrastructure _runtime;
    private string _activeScope = CorrectnessScope;

    private QueueDrainAdapter(RuntimeAdapterInfrastructure runtime) => _runtime = runtime;

    public IReadOnlyList<IBenchmarkOperation> Operations { get; private set; } = [];

    public static async ValueTask<IBenchmarkAdapter> CreateAsync(
        AdapterContext context,
        CancellationToken cancellationToken) =>
        new QueueDrainAdapter(await RuntimeAdapterInfrastructure.OpenAsync(context, cancellationToken));

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await _runtime.PrepareAsync(cancellationToken);
        _activeScope = MeasuredScope;
        var measured = await OpenClientAsync(cancellationToken);
        var completionClaims = new ConcurrentDictionary<long, RuntimeSchedulerWorkClaim>();
        var retryClaims = new ConcurrentDictionary<long, RuntimeSchedulerWorkClaim>();
        var staleClaims = new ConcurrentDictionary<long, RuntimeSchedulerWorkClaim>();

        Operations =
        [
            new BenchmarkOperation(OperationIds[0], (invocation, token) =>
                measured.Queue.EnqueueAsync(Item("enqueue", invocation), token).AsTask()),
            new BenchmarkOperation(
                OperationIds[1],
                async (invocation, token) =>
                {
                    var ids = await measured.Queue.ListPendingWorkflowExecutionIdsAsync(
                        RuntimeQueueDrainWorkload.BatchSize,
                        token);
                    var expected = WorkflowId("claim", invocation);
                    if (ids.Count == 0)
                        throw new PerformanceContractException("The measured bounded queue lookup returned no pending workflow.");
                    var claim = await measured.Queue.ClaimAsync(
                        new RuntimeSchedulerWorkClaimRequest(expected, "bench-claim", FixedNow, Visibility),
                        token);
                    if (claim is null)
                        throw new PerformanceContractException("The measured queue claim did not acquire its prepared FIFO head.");
                },
                (invocation, token) => measured.Queue.EnqueueAsync(Item("claim", invocation), token).AsTask()),
            new BenchmarkOperation(
                OperationIds[2],
                async (invocation, token) =>
                {
                    var result = await measured.Queue.CompleteClaimAsync(completionClaims[invocation], token);
                    if (result.Status != RuntimeSchedulerWorkClaimTransitionStatus.Succeeded)
                        throw new PerformanceContractException("The measured queue completion was not applied.");
                },
                (invocation, token) => PrepareClaimAsync(measured.Queue, "complete", invocation, completionClaims, token)),
            new BenchmarkOperation(
                OperationIds[3],
                async (invocation, token) =>
                {
                    var original = retryClaims[invocation];
                    var successor = await measured.Queue.ClaimAsync(
                        new RuntimeSchedulerWorkClaimRequest(
                            original.Item.WorkflowExecutionId,
                            "bench-successor",
                            FixedNow.Add(Visibility).AddSeconds(1),
                            Visibility),
                        token);
                    if (successor is null || successor.FencingToken <= original.FencingToken)
                        throw new PerformanceContractException("The measured expired queue claim was not reclaimed.");
                },
                (invocation, token) => PrepareClaimAsync(measured.Queue, "retry", invocation, retryClaims, token)),
            new BenchmarkOperation(OperationIds[4], async (invocation, token) =>
            {
                var record = Poison(invocation);
                await measured.Poison.RecordAsync(record, token);
                if (await measured.Poison.FindAsync(record.WorkflowExecutionId, record.WorkItemId, token) is null)
                    throw new PerformanceContractException("The measured poison record could not be read back.");
            }),
            new BenchmarkOperation(
                OperationIds[5],
                async (invocation, token) =>
                {
                    var result = await measured.Queue.CompleteClaimAsync(staleClaims[invocation], token);
                    if (result.Status != RuntimeSchedulerWorkClaimTransitionStatus.Stale)
                        throw new PerformanceContractException("The measured queue accepted a stale acknowledgement.");
                },
                (invocation, token) => PrepareStaleClaimAsync(measured.Queue, invocation, staleClaims, token))
        ];
    }

    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        _activeScope = CorrectnessScope;
        var result = await new RuntimeQueueDrainWorkload().ExecuteAsync(this, cancellationToken);
        return _runtime.Correctness(result.ResultDigest);
    }

    public async ValueTask<RuntimeQueueDrainClients> OpenIndependentClientsAsync(
        CancellationToken cancellationToken = default) =>
        new(await OpenClientAsync(cancellationToken), await OpenClientAsync(cancellationToken));

    public ValueTask<RuntimeQueueDrainClient> ReopenClientAsync(
        CancellationToken cancellationToken = default) =>
        OpenClientAsync(cancellationToken);

    public ValueTask DisposeAsync() => _runtime.DisposeAsync();

    private async ValueTask<RuntimeQueueDrainClient> OpenClientAsync(CancellationToken cancellationToken)
    {
        var lease = await _runtime.OpenClientAsync(
            _activeScope,
            services =>
            {
                var queue = services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
                return new RuntimeQueueDrainClient(
                    queue,
                    services.GetRequiredService<IWorkflowSchedulerPoisonStore>(),
                    queue as IWorkflowSchedulerWorkClaimInspection
                    ?? throw new PerformanceContractException(
                        "The Groundwork scheduler queue does not expose public claim inspection."));
            },
            cancellationToken);
        return lease.Client;
    }

    private static async Task PrepareClaimAsync(
        IWorkflowSchedulerWorkQueue queue,
        string operation,
        long invocation,
        ConcurrentDictionary<long, RuntimeSchedulerWorkClaim> claims,
        CancellationToken cancellationToken)
    {
        var item = Item(operation, invocation);
        await queue.EnqueueAsync(item, cancellationToken);
        var claim = await queue.ClaimAsync(
            new RuntimeSchedulerWorkClaimRequest(item.WorkflowExecutionId, $"bench-{operation}", FixedNow, Visibility),
            cancellationToken)
            ?? throw new PerformanceContractException($"Could not prepare the queue '{operation}' fixture.");
        claims[invocation] = claim;
    }

    private static async Task PrepareStaleClaimAsync(
        IWorkflowSchedulerWorkQueue queue,
        long invocation,
        ConcurrentDictionary<long, RuntimeSchedulerWorkClaim> claims,
        CancellationToken cancellationToken)
    {
        await PrepareClaimAsync(queue, "stale", invocation, claims, cancellationToken);
        var original = claims[invocation];
        _ = await queue.ClaimAsync(
            new RuntimeSchedulerWorkClaimRequest(
                original.Item.WorkflowExecutionId,
                "bench-stale-successor",
                FixedNow.Add(Visibility).AddSeconds(1),
                Visibility),
            cancellationToken)
            ?? throw new PerformanceContractException("Could not prepare the stale queue acknowledgement fixture.");
    }

    private static RuntimeSchedulerWorkItem Item(string operation, long invocation)
    {
        var key = IdentityKey(invocation);
        var workflow = WorkflowId(operation, invocation);
        return new RuntimeSchedulerWorkItem(
            $"bench-{operation}-work-{key}",
            workflow,
            $"bench-{operation}-command-{key}",
            WorkflowExecutionCommandKind.RunSchedulerWork,
            $"bench-{operation}-envelope-{key}",
            $"{workflow}:{key}",
            FixedNow,
            FixedNow,
            0);
    }

    private static RuntimeSchedulerPoisonRecord Poison(long invocation)
    {
        var item = Item("poison", invocation);
        return new RuntimeSchedulerPoisonRecord(
            item.WorkflowExecutionId,
            item.WorkItemId,
            item.CommandKind,
            "benchmark-handler",
            new RuntimeFaultInfo("System.InvalidOperationException", "benchmark poison", "queue-drain"),
            1,
            RuntimeSchedulerPoisonDisposition.Poisoned,
            FixedNow,
            FixedNow);
    }

    private static string WorkflowId(string operation, long invocation) =>
        $"bench-{operation}-workflow-{IdentityKey(invocation)}";

    internal static string IdentityKey(long invocation) => invocation < 0 ? $"w{-invocation}" : $"m{invocation}";
}
