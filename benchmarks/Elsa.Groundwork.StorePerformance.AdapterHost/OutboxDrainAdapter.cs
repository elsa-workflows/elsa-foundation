using System.Collections.Concurrent;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>Real public-store leaf for the frozen post-commit outbox baseline.</summary>
internal sealed class OutboxDrainAdapter : IBenchmarkAdapter, IRuntimeOutboxDrainWorkloadAdapter
{
    private const string CorrectnessScope = "outbox-correctness";
    private const string MeasuredScope = "outbox-measured";
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Visibility = TimeSpan.FromMinutes(1);
    private static readonly RuntimeCheckpointPersistenceDecision Immediate =
        new(RuntimeCheckpointPersistenceMode.Immediate);

    internal static readonly string[] OperationIds =
    [
        "seed-due-and-not-due-outbox-entries",
        "claim-due-batch",
        "record-delivered-and-retryable-results",
        "reclaim-after-visibility-expiry",
        "attempt-stale-completion"
    ];

    private readonly RuntimeAdapterInfrastructure _runtime;
    private string _activeScope = CorrectnessScope;

    private OutboxDrainAdapter(RuntimeAdapterInfrastructure runtime) => _runtime = runtime;

    public IReadOnlyList<IBenchmarkOperation> Operations { get; private set; } = [];

    public static async ValueTask<IBenchmarkAdapter> CreateAsync(
        AdapterContext context,
        CancellationToken cancellationToken) =>
        new OutboxDrainAdapter(await RuntimeAdapterInfrastructure.OpenAsync(context, cancellationToken));

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await _runtime.PrepareAsync(cancellationToken);
        _activeScope = MeasuredScope;
        var measured = await OpenMeasuredClientAsync(cancellationToken);
        var completions = new ConcurrentDictionary<long, RuntimePostCommitOutboxClaim>();
        var reclaims = new ConcurrentDictionary<long, RuntimePostCommitOutboxClaim>();
        var stale = new ConcurrentDictionary<long, RuntimePostCommitOutboxClaim>();

        Operations =
        [
            new BenchmarkOperation(OperationIds[0], (invocation, token) =>
                SeedAsync(measured.Checkpoints, Item("seed", invocation), token)),
            new BenchmarkOperation(
                OperationIds[1],
                async (invocation, token) =>
                {
                    var claims = await measured.Claims.ClaimAsync(
                        new RuntimePostCommitOutboxClaimRequest(
                            "bench-claim",
                            FixedNow,
                            Visibility,
                            1,
                            WorkflowId("claim", invocation)),
                        token);
                    if (claims.Count != 1)
                        throw new PerformanceContractException("The measured outbox claim did not return its prepared due item.");
                },
                (invocation, token) => SeedAsync(measured.Checkpoints, Item("claim", invocation), token)),
            new BenchmarkOperation(
                OperationIds[2],
                async (invocation, token) =>
                {
                    var claim = completions[invocation];
                    await measured.Completions.CompleteClaimAsync(
                        new RuntimePostCommitOutboxClaimCompletion(
                            claim,
                            new RuntimePostCommitOutboxDeliveryResult(
                                claim.OutboxItemId,
                                RuntimePostCommitOutboxStatus.Delivered,
                                FixedNow.AddSeconds(1))),
                        token);
                },
                (invocation, token) => PrepareClaimAsync(measured, "complete", invocation, completions, token)),
            new BenchmarkOperation(
                OperationIds[3],
                async (invocation, token) =>
                {
                    var original = reclaims[invocation];
                    var claims = await measured.Claims.ClaimAsync(
                        new RuntimePostCommitOutboxClaimRequest(
                            "bench-reclaim",
                            FixedNow.Add(Visibility).AddSeconds(1),
                            Visibility,
                            1,
                            original.Item.Intent.WorkflowExecutionId),
                        token);
                    if (claims.Count != 1 || claims.Single().FencingToken <= original.FencingToken)
                        throw new PerformanceContractException("The measured expired outbox claim was not reclaimed.");
                },
                (invocation, token) => PrepareClaimAsync(measured, "reclaim", invocation, reclaims, token)),
            new BenchmarkOperation(
                OperationIds[4],
                async (invocation, token) =>
                {
                    var original = stale[invocation];
                    try
                    {
                        await measured.Completions.CompleteClaimAsync(
                            new RuntimePostCommitOutboxClaimCompletion(
                                original,
                                new RuntimePostCommitOutboxDeliveryResult(
                                    original.OutboxItemId,
                                    RuntimePostCommitOutboxStatus.Delivered,
                                    FixedNow.Add(Visibility).AddSeconds(2))),
                            token);
                    }
                    catch (RuntimePostCommitOutboxStaleClaimException)
                    {
                        return;
                    }
                    throw new PerformanceContractException("The measured outbox accepted a stale completion.");
                },
                (invocation, token) => PrepareStaleClaimAsync(measured, invocation, stale, token))
        ];
    }

    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        _activeScope = CorrectnessScope;
        var result = await new RuntimeOutboxDrainWorkload().ExecuteAsync(this, cancellationToken);
        return _runtime.Correctness(result.ResultDigest);
    }

    public async ValueTask<RuntimeOutboxDrainClients> OpenIndependentClientsAsync(
        CancellationToken cancellationToken = default) =>
        new(await OpenClientAsync(cancellationToken), await OpenClientAsync(cancellationToken));

    public ValueTask<RuntimeOutboxDrainClient> ReopenClientAsync(
        CancellationToken cancellationToken = default) =>
        OpenClientAsync(cancellationToken);

    public ValueTask DisposeAsync() => _runtime.DisposeAsync();

    private async ValueTask<RuntimeOutboxDrainClient> OpenClientAsync(CancellationToken cancellationToken)
    {
        var measured = await OpenMeasuredClientAsync(cancellationToken);
        return measured.Public;
    }

    private async ValueTask<MeasuredOutboxClient> OpenMeasuredClientAsync(CancellationToken cancellationToken)
    {
        var lease = await _runtime.OpenClientAsync(
            _activeScope,
            services => new MeasuredOutboxClient(
                services.GetRequiredService<IRuntimeCheckpointCommitStore>(),
                services.GetRequiredService<IRuntimePostCommitOutboxClaimStore>(),
                services.GetRequiredService<IRuntimePostCommitOutboxClaimCompletionStore>(),
                services.GetRequiredService<IPostCommitOutboxLookupStore>()),
            cancellationToken);
        return lease.Client;
    }

    private static async Task PrepareClaimAsync(
        MeasuredOutboxClient client,
        string operation,
        long invocation,
        ConcurrentDictionary<long, RuntimePostCommitOutboxClaim> claims,
        CancellationToken cancellationToken)
    {
        var item = Item(operation, invocation);
        await SeedAsync(client.Checkpoints, item, cancellationToken);
        var claimed = await client.Claims.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest(
                $"bench-{operation}",
                FixedNow,
                Visibility,
                1,
                item.Intent.WorkflowExecutionId),
            cancellationToken);
        if (claimed.Count != 1)
            throw new PerformanceContractException($"Could not prepare the outbox '{operation}' fixture.");
        claims[invocation] = claimed.Single();
    }

    private static async Task PrepareStaleClaimAsync(
        MeasuredOutboxClient client,
        long invocation,
        ConcurrentDictionary<long, RuntimePostCommitOutboxClaim> claims,
        CancellationToken cancellationToken)
    {
        await PrepareClaimAsync(client, "stale", invocation, claims, cancellationToken);
        var original = claims[invocation];
        var successor = await client.Claims.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest(
                "bench-stale-successor",
                FixedNow.Add(Visibility).AddSeconds(1),
                Visibility,
                1,
                original.Item.Intent.WorkflowExecutionId),
            cancellationToken);
        if (successor.Count != 1)
            throw new PerformanceContractException("Could not prepare the stale outbox completion fixture.");
    }

    private static RuntimePostCommitOutboxItem Item(string operation, long invocation)
    {
        var key = IdentityKey(invocation);
        var id = $"bench-{operation}-outbox-{key}";
        var workflow = WorkflowId(operation, invocation);
        return new RuntimePostCommitOutboxItem(
            id,
            new RuntimePostCommitIntent(
                $"bench-{operation}-intent-{key}",
                workflow,
                "runtime-outbox-drain",
                FixedNow,
                null,
                id,
                null),
            RuntimePostCommitOutboxStatus.Pending,
            FixedNow,
            FixedNow,
            RuntimePostCommitRetryPolicy.None);
    }

    private static string WorkflowId(string operation, long invocation) =>
        $"bench-{operation}-workflow-{IdentityKey(invocation)}";

    internal static string IdentityKey(long invocation) => invocation < 0 ? $"w{-invocation}" : $"m{invocation}";

    private static async Task SeedAsync(
        IRuntimeCheckpointCommitStore checkpoints,
        RuntimePostCommitOutboxItem item,
        CancellationToken cancellationToken)
    {
        var changes = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [])
            .WithPostCommitOutbox(
            [
                new RuntimeStateChange<RuntimePostCommitOutboxItem>(
                    item.OutboxItemId,
                    RuntimeStateChangeOperation.Upsert,
                    item,
                    new Dictionary<string, string>())
            ]);
        await checkpoints.CommitAsync(
            new RuntimeCheckpointCommit(
                $"bench-seed:{item.OutboxItemId}",
                new RuntimeCheckpoint(
                    $"bench-checkpoint:{item.OutboxItemId}",
                    "runtime-outbox-drain",
                    item.Intent.WorkflowExecutionId,
                    item.RecordedAt,
                    [],
                    new Dictionary<string, string>()),
                changes,
                [],
                new Dictionary<string, string>()),
            Immediate,
            cancellationToken);
    }

    private sealed record MeasuredOutboxClient(
        IRuntimeCheckpointCommitStore Checkpoints,
        IRuntimePostCommitOutboxClaimStore Claims,
        IRuntimePostCommitOutboxClaimCompletionStore Completions,
        IPostCommitOutboxLookupStore Lookup)
    {
        public RuntimeOutboxDrainClient Public => new(Checkpoints, Claims, Completions, Lookup);
    }
}
