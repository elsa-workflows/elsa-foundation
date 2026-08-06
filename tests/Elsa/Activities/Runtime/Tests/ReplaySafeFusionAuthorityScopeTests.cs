using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Path-A source-domain guardrail: the durable ScheduleActivity dispatch remains the recovery authority while the
/// shipped D2 pump enters nested D1 spans. The future authority contract is deliberately read by reflection so this
/// RED test compiles before that contract exists.
/// </summary>
public sealed class ReplaySafeFusionAuthorityScopeTests
{
    private const string OuterNodeId = "node-flowchart";
    private const string FirstSuccessorNodeId = "node-first";
    private const string NestedSuccessorNodeId = "node-nested";
    private static readonly string[] PreparationNames =
    [
        RuntimeCheckpointNames.ActivityScheduled,
        RuntimeCheckpointNames.ActivityStarted,
        RuntimeCheckpointNames.ActivityAttemptClaimed
    ];

    [Fact]
    public async Task Nested_D2_to_D1_preparations_inherit_the_outer_durable_schedule_authority()
    {
        var observations = new AuthorityObservationSink();
        var boundaryCrash = new ArmedPostCommitBoundaryCrash();
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithCoalescing()
            .ConfigureServices(services =>
            {
                services.AddSingleton(new RuntimeReplaySafeFusionOptions
                {
                    Enabled = true,
                    FuseCompletionCascade = true
                });
                services.AddSingleton(observations);
                services.AddScoped<IRuntimeCheckpointCommitEnricher, AmbientAuthorityObservingEnricher>();
                DecorateCheckpointStoreWithBoundaryCrash(services, boundaryCrash);
            })
            .Build(Enumerable.Range(0, 12).Select(index => $"actexec-{index}"));

        var outerScheduleWorkItem = await StageOuterLeafScheduleAsync(harness, boundaryCrash);
        var diagnostics = harness.Services.GetRequiredService<RuntimeSchedulerDispatchDiagnostics>();
        observations.Clear();
        diagnostics.Reset();
        boundaryCrash.Disarm();

        await ResolveResumptionService(harness.Services).SweepAsync(new RuntimeResumptionSweepRequest());
        var workflowState = await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId);

        Assert.Equal(WorkflowExecutionStatus.Completed, workflowState?.Status);
        Assert.True(diagnostics.FusedSpans >= 2,
            $"Expected the outer leaf and its nested ReplaySafe successor to fuse, saw {diagnostics.FusedSpans} spans.");
        Assert.True(diagnostics.InlineCascadeDispatches > 0,
            $"Expected the real D2 completion pump to dispatch nested work inline, saw {diagnostics.InlineCascadeDispatches} dispatches.");

        var relevant = observations.Snapshot()
            .Where(item => item.ExecutableNodeId is FirstSuccessorNodeId or NestedSuccessorNodeId)
            .Where(item => PreparationNames.Contains(item.CheckpointName, StringComparer.Ordinal))
            .ToArray();
        foreach (var nodeId in new[] { FirstSuccessorNodeId, NestedSuccessorNodeId })
        foreach (var checkpointName in PreparationNames)
            Assert.Single(relevant, item =>
                StringComparer.Ordinal.Equals(item.ExecutableNodeId, nodeId) &&
                StringComparer.Ordinal.Equals(item.CheckpointName, checkpointName));

        // Intentional RED boundary: all real fusion/preparation assertions above must remain non-vacuous before the
        // missing production accessor is reported. No test-owned dispatch or fusion surrogate can satisfy this.
        Assert.True(observations.AccessorContractFound,
            "Missing Elsa.Workflows.Runtime.Core.Contracts.IRuntimeCheckpointRecoveryAuthorityAccessor.");
        Assert.True(observations.AccessorRegistrationFound,
            "IRuntimeCheckpointRecoveryAuthorityAccessor exists but is not registered in the active runtime scope.");
        Assert.Null(observations.ReflectionFailure);

        var outerScheduled = Assert.Single(relevant, item =>
            item.ExecutableNodeId == FirstSuccessorNodeId && item.CheckpointName == RuntimeCheckpointNames.ActivityScheduled);
        var outerAuthority = Assert.IsType<AuthoritySnapshot>(outerScheduled.Authority);
        Assert.Equal(1, outerAuthority.Version);
        Assert.Equal("runtime.scheduler-work", outerAuthority.Kind);
        Assert.Equal(WorkflowExecutionHarness.WorkflowExecutionId, outerAuthority.WorkflowExecutionId);
        Assert.Equal(outerScheduleWorkItem.WorkItemId, outerScheduled.SchedulerWorkItemId);
        Assert.Equal(outerScheduleWorkItem.WorkItemId, outerAuthority.WorkItemId);
        Assert.Matches("^sha256:[0-9a-f]{64}$", outerAuthority.Fingerprint);

        foreach (var observation in relevant)
        {
            var authority = Assert.IsType<AuthoritySnapshot>(observation.Authority);
            Assert.Equal(outerAuthority, authority);
        }
    }

    private static void DecorateCheckpointStoreWithBoundaryCrash(
        IServiceCollection services,
        ArmedPostCommitBoundaryCrash boundaryCrash)
    {
        var descriptor = services.Last(item => item.ServiceType == typeof(IRuntimeCheckpointCommitStore));
        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(
            typeof(IRuntimeCheckpointCommitStore),
            serviceProvider => new PostCommitBoundaryCrashStore(
                (IRuntimeCheckpointCommitStore)(descriptor.ImplementationInstance
                    ?? descriptor.ImplementationFactory?.Invoke(serviceProvider)
                    ?? ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType!)),
                boundaryCrash),
            descriptor.Lifetime));
    }

    private static async Task<RuntimeSchedulerWorkItem> StageOuterLeafScheduleAsync(
        WorkflowExecutionHarness harness,
        ArmedPostCommitBoundaryCrash boundaryCrash)
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => harness.RunAsync(BuildStraightLineExecutable()));

        for (var pass = 0; pass < 8; pass++)
        {
            await DeliverPendingOutboxAsync(harness.Services);
            if (await FindOuterLeafScheduleAsync(harness.Services) is { } staged)
                return staged;

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                ResolveResumptionService(harness.Services)
                    .SweepAsync(new RuntimeResumptionSweepRequest()).AsTask());
        }

        boundaryCrash.Disarm();
        throw new InvalidOperationException("Failed to stage node-first as the next durable ScheduleActivity source.");
    }

    private static async Task DeliverPendingOutboxAsync(IServiceProvider services) =>
        await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(
            new RuntimePostCommitOutboxProcessRequest(
                limit: 100,
                workflowExecutionId: WorkflowExecutionHarness.WorkflowExecutionId));

    private static async Task<RuntimeSchedulerWorkItem?> FindOuterLeafScheduleAsync(IServiceProvider services)
    {
        var pending = await services.GetRequiredService<IWorkflowSchedulerWorkQueue>()
            .ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionHarness.WorkflowExecutionId));
        return pending.SingleOrDefault(item =>
            item.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity &&
            item.Payload is { } payload &&
            StringComparer.Ordinal.Equals(
                payload.Deserialize<RuntimeScheduleActivityCommandPayload>()?.ExecutableNodeId,
                FirstSuccessorNodeId));
    }

    private static RuntimeResumptionService ResolveResumptionService(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>(),
            provider.GetRequiredService<IWorkflowSchedulerWorkQueue>(),
            provider.GetRequiredService<IRuntimeRecoveryScanner>(),
            provider.GetRequiredService<IWorkflowExecutionActorProvider>(),
            provider.GetRequiredService<IRuntimeExecutionIdGenerator>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IWorkflowExecutionStateStore>());

    private static WorkflowExecutable BuildStraightLineExecutable()
    {
        var first = WorkflowExecutionHarness.NewReplaySafeProbeNode(FirstSuccessorNodeId);
        var nested = WorkflowExecutionHarness.NewReplaySafeProbeNode(NestedSuccessorNodeId);
        var root = new ExecutableNode(
            executableNodeId: OuterNodeId,
            authoredActivityId: "authored-flowchart",
            activityType: typeof(FlowchartActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "elsa.flowchart",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [first, nested])],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(
                    connections:
                    [
                        new FlowchartConnection(
                            new FlowchartEndpoint(FirstSuccessorNodeId),
                            new FlowchartEndpoint(NestedSuccessorNodeId))
                    ],
                    startNodeId: FirstSuccessorNodeId))));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private sealed class AmbientAuthorityObservingEnricher(
        IServiceProvider services,
        AuthorityObservationSink sink) : IRuntimeCheckpointCommitEnricher
    {
        private const string AccessorContractName =
            "Elsa.Workflows.Runtime.Core.Contracts.IRuntimeCheckpointRecoveryAuthorityAccessor";

        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(
            RuntimeCheckpointCommit commit,
            CancellationToken cancellationToken = default)
        {
            var contract = typeof(IRuntimeCheckpointCommitEnricher).Assembly.GetType(AccessorContractName, throwOnError: false);
            sink.NoteContract(contract is not null);

            object? authority = null;
            if (contract is not null)
            {
                var accessor = services.GetService(contract);
                sink.NoteRegistration(accessor is not null);
                if (accessor is not null)
                {
                    try
                    {
                        var current = contract.GetProperty("Current")?.GetValue(accessor);
                        if (current is not null)
                        {
                            authority = new AuthoritySnapshot(
                                Read<int>(current, "Version"),
                                Read<string>(current, "Kind"),
                                Read<string>(current, "WorkflowExecutionId"),
                                Read<string>(current, "WorkItemId"),
                                Read<string>(current, "Fingerprint"));
                        }
                    }
                    catch (Exception exception)
                    {
                        sink.NoteReflectionFailure(exception);
                    }
                }
            }

            commit.Checkpoint.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableNodeId, out var executableNodeId);
            commit.Checkpoint.Metadata.TryGetValue(RuntimeMetadataKeys.SchedulerWorkItemId, out var schedulerWorkItemId);
            sink.Add(new AuthorityObservation(
                commit.Checkpoint.Name,
                executableNodeId,
                schedulerWorkItemId,
                authority as AuthoritySnapshot));
            return ValueTask.FromResult(commit);
        }

        private static T Read<T>(object value, string propertyName) =>
            (T)(value.GetType().GetProperty(propertyName)?.GetValue(value)
                ?? throw new InvalidOperationException($"Recovery authority is missing '{propertyName}'."));
    }

    private sealed class AuthorityObservationSink
    {
        private readonly object _gate = new();
        private readonly List<AuthorityObservation> _observations = [];

        public bool AccessorContractFound { get; private set; }
        public bool AccessorRegistrationFound { get; private set; }
        public Exception? ReflectionFailure { get; private set; }

        public void NoteContract(bool found)
        {
            lock (_gate)
                AccessorContractFound |= found;
        }

        public void NoteRegistration(bool found)
        {
            lock (_gate)
                AccessorRegistrationFound |= found;
        }

        public void NoteReflectionFailure(Exception exception)
        {
            lock (_gate)
                ReflectionFailure ??= exception;
        }

        public void Add(AuthorityObservation observation)
        {
            lock (_gate)
                _observations.Add(observation);
        }

        public void Clear()
        {
            lock (_gate)
            {
                _observations.Clear();
                AccessorContractFound = false;
                AccessorRegistrationFound = false;
                ReflectionFailure = null;
            }
        }

        public AuthorityObservation[] Snapshot()
        {
            lock (_gate)
                return _observations.ToArray();
        }
    }

    private sealed record AuthorityObservation(
        string CheckpointName,
        string? ExecutableNodeId,
        string? SchedulerWorkItemId,
        AuthoritySnapshot? Authority);

    private sealed record AuthoritySnapshot(
        int Version,
        string Kind,
        string WorkflowExecutionId,
        string WorkItemId,
        string Fingerprint);

    private sealed class ArmedPostCommitBoundaryCrash
    {
        private int _armed = 1;

        public bool IsArmed => Volatile.Read(ref _armed) == 1;

        public void Disarm() => Interlocked.Exchange(ref _armed, 0);
    }

    private sealed class PostCommitBoundaryCrashStore(
        IRuntimeCheckpointCommitStore inner,
        ArmedPostCommitBoundaryCrash boundaryCrash) :
        IRuntimeCheckpointCommitStore,
        IRuntimeCheckpointPreparedLedgerStore
    {
        private IRuntimeCheckpointPreparedLedgerStore PreparedLedger =>
            inner as IRuntimeCheckpointPreparedLedgerStore
            ?? throw new InvalidOperationException("The boundary wrapper requires the durable prepared-ledger capability.");

        public ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(
            RuntimeCheckpointPrepareRequest request,
            CancellationToken cancellationToken = default) =>
            inner.PrepareAsync(request, cancellationToken);

        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedAsync(
            RuntimeCheckpointPreparationToken token,
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.CommitPreparedAsync(token, commit, decision, cancellationToken);
            if (boundaryCrash.IsArmed &&
                decision.Mode == RuntimeCheckpointPersistenceMode.Immediate &&
                commit.StateChanges.PostCommitOutbox.Count > 0)
            {
                throw new OperationCanceledException(
                    $"Stopped after honest post-commit boundary '{commit.CommitId}' to stage its durable continuation.");
            }

            return result;
        }

        public ValueTask<RuntimeCheckpointPreparedPage> PagePreparedAsync(
            RuntimeCheckpointPreparedQuery query,
            CancellationToken cancellationToken = default) =>
            PreparedLedger.PagePreparedAsync(query, cancellationToken);

        public ValueTask<RuntimeCheckpointPreparedFoldResult> CommitPreparedFoldAsync(
            RuntimeCheckpointPreparedFoldRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException();
        }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default) =>
            inner.CommitAsync(commit, decision, cancellationToken);
    }
}
