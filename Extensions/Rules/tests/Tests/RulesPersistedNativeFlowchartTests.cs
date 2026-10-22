using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Runtime;
using Elsa.Rules.Workflows.Design;
using Elsa.Rules.Workflows.Runtime;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Rules.Tests;

/// <summary>
/// Exercises one persisted native Rules continuation inside real Sequence/Flowchart execution. The executable is
/// assembled by the test and passed through the registered Rules metadata enricher; this is runtime evidence, not
/// the full workflow compiler/publication endpoint or package-provenance acceptance.
/// </summary>
public sealed class RulesPersistedNativeFlowchartTests : IAsyncLifetime
{
    private const string RulesNodeId = "rules";
    private const string HoldNodeId = "hold";
    private const string FlowchartNodeId = "flowchart";
    private const string SequenceNodeId = "sequence";
    private const string ForkNodeId = "fork";
    private const string JoinNodeId = "join";
    private const string FinalNodeId = "final";
    private const string EventName = "rules-native-release";
    private const string SourceReferenceId = "rules-native-persisted-source";
    private const string ProgramJson = "{\"formatVersion\":1,\"languageId\":\"elsa.rules.finite-positive\",\"languageVersion\":1,\"vocabulary\":[\"derived\",\"seed\"],\"rules\":[{\"id\":\"derive\",\"premises\":[\"seed\"],\"conclusion\":\"derived\"}]}";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"elsa-rules-native-flowchart-{Guid.NewGuid():N}.db");
    private readonly string _workflowExecutionId = $"rules-native-{Guid.NewGuid():N}";
    private readonly FinalCounter _finalCounter = new();
    private WorkflowExecutionHarness _harness = null!;
    private WorkflowExecutable _executable = null!;
    private RuleSemanticPin _implementation = null!;

    public async Task InitializeAsync()
    {
        // This semantic identity is intentionally synthetic. It lets the real finite evaluator execute the
        // retained artifact, but it is not evidence that the loaded assemblies came from a package host.
        _implementation = RuleTestData.Invocation().ExpectedImplementation;
        var root = BuildGraph();
        root = await EnrichThroughRegisteredEventPipelineAsync(root, _implementation);

        var hasher = new WorkflowExecutableHasher();
        var artifactHash = hasher.ComputeHash(root);
        var identity = new WorkflowExecutableIdentity(
            hasher.CreateArtifactId("rules-native-", artifactHash),
            "rules-native-definition",
            "rules-native-version",
            "1.0.0",
            artifactHash);
        _executable = new WorkflowExecutable(
            identity,
            root,
            ResumeTargets(),
            WorkflowExecutionHarness.Timestamp,
            new Dictionary<string, string>(StringComparer.Ordinal),
            IncidentStrategyBuiltIns.FaultReference);

        _harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithFeature(services => new RulesRuntimeFeature { MaxConcurrentAttempts = 1, PollIntervalMilliseconds = 10 }.ConfigureServices(services))
            .WithFeature(services => new RulesWorkflowsRuntimeFeature { MaxConcurrentDeliveries = 1, PollIntervalMilliseconds = 10 }.ConfigureServices(services))
            .WithFeature(services => new RulesRuntimeEntityFrameworkCoreFeature
            {
                ConnectionString = $"Data Source={_databasePath};Pooling=False"
            }.ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton(_finalCounter);
                services.AddSingleton<IRuleImplementationIdentitySource>(new TestImplementationIdentitySource(_implementation));
                services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = $"Data Source={_databasePath};Pooling=False",
                    RecoveryContinuationSigningKey = "rules-native-test-recovery-key-32-bytes",
                    HierarchyCursorSigningKey = "rules-native-test-hierarchy-key-32-bytes"
                }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
            })
            .WithPersistenceMode(PersistenceMode.Durable)
            .Build(identity, _workflowExecutionId, Enumerable.Range(1, 64).Select(index => $"{_workflowExecutionId}-activity-{index}"));

        foreach (var initializer in _harness.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        _harness.InitializeActivityTypes();
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();

        foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
            if (File.Exists(file))
                File.Delete(file);
    }

    [Fact]
    public async Task Rules_terminal_and_native_completion_do_not_release_parallel_join_until_sibling_resumes()
    {
        var reference = await _harness.PublishAsync(_executable, SourceReferenceId);
        await CopyPublicationIntoPartitionAsync(reference);

        var start = await _harness.StartPublishedAsync(reference, _workflowExecutionId,
            tenantId: WorkflowExecutionHarness.Partition.Value, partition: WorkflowExecutionHarness.Partition);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, start.CommandDispatch.Status);

        // The Start intent is committed with the native suspension. Sweep delivery is a separate generation.
        await SweepPartitionAsync();

        var initialActivities = await ActivityStatesAsync();
        var rulesActivity = StateFor(initialActivities, RulesNodeId);
        var holdActivity = StateFor(initialActivities, HoldNodeId);
        Assert.Equal(ActivityExecutionStatus.Suspended, rulesActivity.Status);
        Assert.Equal(ActivityExecutionStatus.Suspended, holdActivity.Status);
        Assert.Equal(0, _finalCounter.Count);

        var request = RulesNativeBoundary.ReadState(rulesActivity);
        var identity = new RuleRunIdentity(request.Scope, request.RunId);
        Assert.Equal(WorkflowExecutionHarness.Partition.Value, identity.Scope.TenantId);
        Assert.Equal(WorkflowExecutionHarness.Partition.Value, identity.Scope.PersistencePartition);

        // Real store admission precedes model work; each direct RuleExecution step is one durable claim.
        var modelTerminal = await DriveRulesToTerminalAsync(identity);
        Assert.Equal(RuleTerminalReadDisposition.Found, modelTerminal.Disposition);
        Assert.Equal(RuleTerminalKind.Succeeded, modelTerminal.Result!.Kind);
        Assert.Equal(new[] { "derived", "seed" }, modelTerminal.Result.ResultFacts
            .Select(fact => fact.Atom).Order(StringComparer.Ordinal));
        var beforeNativeDelivery = await ActivityStatesAsync();
        Assert.Equal(ActivityExecutionStatus.Suspended, StateFor(beforeNativeDelivery, RulesNodeId).Status);
        Assert.Equal(ActivityExecutionStatus.Suspended, StateFor(beforeNativeDelivery, HoldNodeId).Status);
        Assert.Equal(WorkflowExecutionStatus.Running, (await WorkflowStateAsync())!.Status);
        AssertNoCompletedJoinOrFinal(beforeNativeDelivery);
        Assert.Equal(0, _finalCounter.Count);

        // The real delivery executor dispatches through the persisted bookmark and independently proves consumption.
        await using (var scope = _harness.Services.CreateAsyncScope())
        {
            var delivery = scope.ServiceProvider.GetRequiredService<RulesNativeDeliveryExecution>();
            var acknowledged = await delivery.ExecuteOneAsync("native-test-delivery");
            Assert.True(acknowledged, "Native delivery was not acknowledged. Persisted activities: " +
                string.Join(", ", (await ActivityStatesAsync()).Select(state =>
                    $"{state.Execution.ExecutableNodeId}={state.Status}, result schema={state.Completion?.Result.Type.SchemaVersion?.ToString() ?? "none"}")));
        }

        var afterRules = await ActivityStatesAsync();
        var completedRules = StateFor(afterRules, RulesNodeId);
        var stillWaiting = StateFor(afterRules, HoldNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, completedRules.Status);
        Assert.Equal(ActivityExecutionStatus.Suspended, stillWaiting.Status);
        Assert.Contains(completedRules.TriggerDeliveries!, item => item.Status == ActivityTriggerDeliveryStatus.Consumed);
        Assert.Equal(WorkflowExecutionStatus.Running, (await WorkflowStateAsync())!.Status);
        AssertNoCompletedJoinOrFinal(afterRules);
        Assert.Equal(0, _finalCounter.Count);

        using (var scope = _harness.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IRuleRunStore>();
            Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery,
                (await store.ClaimDeliveryAsync(new("native-test-after-ack", [1]))).Disposition);
        }

        var holdBookmarkId = Assert.Single(stillWaiting.BookmarkIds);
        await using (var scope = await CreatePartitionScopeAsync())
        {
            var bookmark = await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>()
                .FindAsync(_workflowExecutionId, holdBookmarkId);
            Assert.NotNull(bookmark);
            Assert.Equal(HoldNodeId, bookmark.ExecutableNodeId);

            var resumed = await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(
                new BookmarkResumeDispatchRequest(
                    _workflowExecutionId,
                    bookmark.StimulusType,
                    bookmark.StimulusHash,
                    JsonSerializer.SerializeToElement("release"),
                    idempotencyKey: "rules-native-release",
                    requestedBy: "rules-persisted-native-test", metadata: null, payloadType: Assert.Single(stillWaiting.TriggerRegistrations!).PayloadType, providerId: "rules-persisted-native-test",
                    bookmarkId: bookmark.BookmarkId, testRunDelivery: null));
            Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, resumed.Status);
        }

        var finalActivities = await ActivityStatesAsync();
        var completedHold = StateFor(finalActivities, HoldNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, completedHold.Status);
        Assert.Equal(ActivityExecutionStatus.Completed, StateFor(finalActivities, JoinNodeId).Status);
        Assert.Equal(ActivityExecutionStatus.Completed, StateFor(finalActivities, FinalNodeId).Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await WorkflowStateAsync())!.Status);
        Assert.Equal(1, _finalCounter.Count);
    }

    private async Task SweepPartitionAsync()
    {
        await using var scope = await CreatePartitionScopeAsync();
        var resumption = scope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>();
        for (var pass = 0; pass < 8; pass++)
            if ((await resumption.SweepAsync(new RuntimeResumptionSweepRequest())).OutboxAttemptedCount == 0)
                return;
        throw new InvalidOperationException("Native post-commit intents did not settle within eight sweeps.");
    }

    private async Task<RuleTerminalReadResult> DriveRulesToTerminalAsync(RuleRunIdentity identity)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            RuleTerminalReadResult terminal;
            using (var scope = _harness.Services.CreateScope())
            {
                var reader = scope.ServiceProvider.GetRequiredService<IRuleRunReader>();
                Assert.Equal(RuleRunReadDisposition.Found, (await reader.FindAsync(identity)).Disposition);
                terminal = await reader.ReadTerminalAsync(identity);
                if (terminal.Disposition == RuleTerminalReadDisposition.Found)
                    return terminal;

                var execution = scope.ServiceProvider.GetRequiredService<RuleExecution>();
                Assert.True(await execution.ExecuteOneAsync("native-test-computation", CancellationToken.None),
                    "The persisted Rules invocation stopped making progress before a terminal result was stored.");
            }
        }

        using var finalScope = _harness.Services.CreateScope();
        var finalRead = await finalScope.ServiceProvider.GetRequiredService<IRuleRunReader>().ReadTerminalAsync(identity);
        Assert.Equal(RuleTerminalReadDisposition.Found, finalRead.Disposition);
        return finalRead;
    }

    private ExecutableNode BuildGraph()
    {
        var rulesContract = ClrActivityContractTestBuilder.BuildContract(typeof(RunRules));
        var rulesBindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
        {
            [nameof(RunRules.Program)] = Literal(rulesContract.Inputs[nameof(RunRules.Program)], ProgramJson),
            [nameof(RunRules.InitialFacts)] = Literal(rulesContract.Inputs[nameof(RunRules.InitialFacts)], new[] { "seed" }),
            [nameof(RunRules.MaxRounds)] = Literal(rulesContract.Inputs[nameof(RunRules.MaxRounds)], 4)
        };
        var rules = Node(RulesNodeId, typeof(RunRules), rulesContract, rulesBindings);

        var holdContract = ClrActivityContractTestBuilder.BuildContract(typeof(Event));
        var holdBindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
        {
            [nameof(Event.EventName)] = Literal(holdContract.Inputs[nameof(Event.EventName)], EventName),
            [nameof(Event.CanStartWorkflow)] = Literal(holdContract.Inputs[nameof(Event.CanStartWorkflow)], false),
            [nameof(Event.CorrelationId)] = Literal(holdContract.Inputs[nameof(Event.CorrelationId)], "")
        };
        var hold = Node(HoldNodeId, typeof(Event), holdContract, holdBindings);
        var fork = Node<JoinPassActivity>(ForkNodeId);
        var join = Node<JoinPassActivity>(JoinNodeId);
        var flowchartContract = ClrActivityContractTestBuilder.BuildContract(typeof(FlowchartActivity));
        var flowchart = new ExecutableNode(
            FlowchartNodeId, $"authored-{FlowchartNodeId}", typeof(FlowchartActivity).FullName!, "1.0.0",
            WellKnownRuntimeActivityConsumers.ClrActivity, flowchartContract.DescriptorPayload,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(StringComparer.Ordinal),
            [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [fork, rules, hold, join])],
            new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(
                    [Connection(ForkNodeId, RulesNodeId), Connection(ForkNodeId, HoldNodeId),
                     Connection(RulesNodeId, JoinNodeId), Connection(HoldNodeId, JoinNodeId)],
                    ForkNodeId,
                    new Dictionary<string, FlowchartNodeMetadata>(StringComparer.Ordinal)
                    {
                        [ForkNodeId] = new(FlowchartPolicyKinds.ParallelFork)
                    }))),
            activityContract: flowchartContract);

        var final = Node<FinalCounterActivity>(FinalNodeId);
        var sequenceContract = ClrActivityContractTestBuilder.BuildContract(typeof(SequenceActivity));
        return new ExecutableNode(
            SequenceNodeId, $"authored-{SequenceNodeId}", typeof(SequenceActivity).FullName!, "1.0.0",
            WellKnownRuntimeActivityConsumers.ClrActivity, sequenceContract.DescriptorPayload,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(StringComparer.Ordinal),
            [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [flowchart, final])],
            new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceExecutableStructure([FlowchartNodeId, FinalNodeId]))),
            activityContract: sequenceContract);
    }

    private static ExecutableNode Node<TActivity>(string nodeId) where TActivity : IActivity
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(TActivity));
        return Node(nodeId, typeof(TActivity), contract, new Dictionary<string, RuntimeInputBinding>());
    }

    private static ExecutableNode Node(string nodeId, Type activityType, ActivityContract contract,
        IReadOnlyDictionary<string, RuntimeInputBinding> bindings) =>
        new(nodeId, $"authored-{nodeId}", activityType.FullName!, "1.0.0",
            WellKnownRuntimeActivityConsumers.ClrActivity, contract.DescriptorPayload, bindings,
            new Dictionary<string, string>(StringComparer.Ordinal), activityContract: contract);

    private static RuntimeInputBinding Literal(ActivityInputContract input, object value) =>
        new(input.Key, input.Type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(input.Type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));

    private static IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> ResumeTargets()
    {
        var rulesTarget = new WorkflowExecutableResumeTarget(
            WorkflowExecutableResumeTarget.ComposeScopedId(RulesNodeId, RulesNativeBoundary.ResumeTargetKey),
            RulesNodeId, "ResumeAsync", new Dictionary<string, string>(StringComparer.Ordinal), RulesNativeBoundary.ResumeTargetKey);
        var holdTarget = new WorkflowExecutableResumeTarget(
            WorkflowExecutableResumeTarget.ComposeScopedId(HoldNodeId, Event.ResumeTargetId),
            HoldNodeId, "ResumeAsync", new Dictionary<string, string>(StringComparer.Ordinal), Event.ResumeTargetId);
        return new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
        {
            [rulesTarget.ResumeTargetId] = rulesTarget,
            [holdTarget.ResumeTargetId] = holdTarget
        };
    }

    private async Task CopyPublicationIntoPartitionAsync(WorkflowExecutableSourceReference reference)
    {
        WorkflowExecutable? saved;
        await using (var source = await _harness.Services.GetRequiredService<IPersistenceOperationScopeFactory>()
                         .CreateAsync(new PersistenceScope(PersistenceScope.DefaultValue)))
            saved = await source.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>()
                .FindAsync(reference.ArtifactId);

        Assert.Equal(_executable.Identity.ArtifactHash, new WorkflowExecutableHasher().ComputeHash(Assert.IsType<WorkflowExecutable>(saved).RootActivity));
        await using var target = await CreatePartitionScopeAsync();
        await target.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>()
            .SaveAsync(Assert.IsType<WorkflowExecutable>(saved));
        await target.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(reference);
    }

    private async ValueTask<PersistenceOperationScope> CreatePartitionScopeAsync() =>
        await _harness.Services.GetRequiredService<IPersistenceOperationScopeFactory>()
            .CreateAsync(new PersistenceScope(WorkflowExecutionHarness.Partition.Value));

    private async Task<IReadOnlyCollection<ActivityExecutionState>> ActivityStatesAsync()
    {
        await using var scope = await CreatePartitionScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>()
            .ListAllAsync(_workflowExecutionId);
    }

    private async Task<WorkflowExecutionState?> WorkflowStateAsync()
    {
        await using var scope = await CreatePartitionScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>()
            .FindAsync(_workflowExecutionId);
    }

    private static async Task<ExecutableNode> EnrichThroughRegisteredEventPipelineAsync(
        ExecutableNode root,
        RuleSemanticPin implementation)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRuleImplementationIdentitySource>(new TestImplementationIdentitySource(implementation));
        new EventsFeature().ConfigureServices(services);
        new WorkflowsPublishingFeature().ConfigureServices(services);
        new RulesRuntimeFeature().ConfigureServices(services);
        new RulesWorkflowsDesignFeature().ConfigureServices(services);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var enricher = scope.ServiceProvider.GetRequiredService<IExecutableNodeMetadataEnricher>();
        var request = new WorkflowExecutableCompileRequest(
            "rules-native-version", WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionHarness.Timestamp, WorkflowExecutionHarness.Timestamp, null, "rules-native-");
        var source = new WorkflowExecutableCompileSource(
            "rules-native-definition", "rules-native-version", "1.0.0", WorkflowDefinitionState.Empty,
            "WorkflowDefinitionVersion", "rules-native-version", "1.0.0");
        var enrichment = await enricher.EnrichCompilationAsync(request, source, root);
        Assert.True(enrichment.RootActivity.DescendantsAndSelf()
            .Single(node => node.ExecutableNodeId == RulesNodeId).Metadata.ContainsKey(RunRules.PinMetadataKey));
        return enrichment.RootActivity;
    }

    private static FlowchartConnection Connection(string source, string target) =>
        new(new FlowchartEndpoint(source), new FlowchartEndpoint(target));

    private static void AssertNoCompletedJoinOrFinal(IReadOnlyCollection<ActivityExecutionState> states)
    {
        Assert.DoesNotContain(states, item => item.Execution.ExecutableNodeId == JoinNodeId && item.Status == ActivityExecutionStatus.Completed);
        Assert.DoesNotContain(states, item => item.Execution.ExecutableNodeId == FinalNodeId && item.Status == ActivityExecutionStatus.Completed);
    }

    private static ActivityExecutionState StateFor(IEnumerable<ActivityExecutionState> states, string nodeId) =>
        Assert.Single(states, item => item.Execution.ExecutableNodeId == nodeId);

    private sealed class TestImplementationIdentitySource(RuleSemanticPin implementation) : IRuleImplementationIdentitySource
    {
        public ValueTask<RuleSemanticPin?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<RuleSemanticPin?>(implementation);
        }
    }

    public sealed class FinalCounter
    {
        private int _count;
        internal int Count => Volatile.Read(ref _count);
        internal void Increment() => Interlocked.Increment(ref _count);
    }

    public sealed class JoinPassActivity : Activity<ActivityUnit>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    public sealed class FinalCounterActivity(FinalCounter counter) : Activity<ActivityUnit>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            counter.Increment();
            return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
        }
    }
}
