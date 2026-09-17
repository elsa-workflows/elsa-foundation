using System.Diagnostics;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DispatchWorkflowActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// A parent that waits on a dispatched child, run end to end on each runtime store. It mirrors cases B and C of
/// <c>e2e-tests/composition/Test-DispatchWorkflowOutcomes.ps1</c> in-process: the parent is
/// <c>Sequence[DispatchWorkflow(WaitForCompletion), probe]</c>, and the probe running proves the parent resumed past the
/// wait. Each store runs every case through its own subclass.
/// </summary>
/// <remarks>
/// The in-memory DispatchWorkflow tests cannot catch a rule only a durable store applied: waiting on a child was broken on
/// EF because its checkpoint store refused the child's terminal checkpoint, which carries the parent's resume intent. Neither
/// case failed loudly. A completing child's start was acknowledged as delivered once the refused commit left its drain
/// faulted; a faulting child's refusal was retried as transient until exhaustion, where the existing child made the store
/// record the start as delivered rather than failed. Either way the parent waited forever.
/// </remarks>
public abstract class DispatchWorkflowStoreContractTests : IAsyncLifetime
{
    private const string ParentWorkflowExecutionId = "wfexec-parent";
    private const string DispatchNodeId = "node-dispatch";
    private const string AfterDispatchNodeId = "node-after";
    private const string ChildNodeId = "node-child";
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(60);
    private readonly ScriptedChildCommitFailure _childCommitFailure = new(ParentWorkflowExecutionId);
    private readonly SwitchableRetryPolicy _retryPolicy = new();
    private readonly ScriptedRecordingFailure _recordingFailure = new();
    private readonly OffsetClock _clock = new();
    private WorkflowExecutionHarness _harness = null!;
    private int _outboxFailures;

    /// <summary>Replaces the default in-memory runtime stores with the store under test.</summary>
    protected abstract void ConfigureStore(IServiceCollection services);

    public async Task InitializeAsync()
    {
        _harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .WithFeature(services => new DispatchWorkflowRuntimeFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                ConfigureStore(services);
                services.AddScoped<IRuntimeCheckpointCommitEnricher>(_ => _childCommitFailure);
                services.AddSingleton<IRuntimeDomainRetryPolicy>(_retryPolicy);
                services.AddSingleton<TimeProvider>(_clock);
                DecorateOutboxStore(services, _recordingFailure);
            })
            .Build(Enumerable.Range(1, 16).Select(ordinal => $"actexec-{ordinal}"));

        foreach (var initializer in _harness.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        _harness.InitializeActivityTypes();
    }

    public virtual async Task DisposeAsync() => await _harness.DisposeAsync();

    [Theory]
    [InlineData(false, WorkflowExecutionStatus.Completed, "Completed")]
    [InlineData(true, WorkflowExecutionStatus.Faulted, "Faulted")]
    public async Task A_parent_waiting_on_its_child_resumes_with_the_child_outcome(
        bool childFaults,
        WorkflowExecutionStatus expectedChildStatus,
        string expectedDispatchOutcome)
    {
        var parent = await RunParentAsync(childFaults);

        // A faulted child routes to an ordinary outcome: the parent itself still completes and runs the step after.
        parent.AssertWorkflowCompleted();
        parent.AssertOutcomes(DispatchNodeId, expectedDispatchOutcome);
        parent.AssertCompleted(AfterDispatchNodeId);
        var dispatch = Assert.Single(await ListDispatchesAsync());
        Assert.Equal(expectedChildStatus, (await _harness.ReadRunAsync(dispatch.ChildWorkflowExecutionId)).WorkflowState?.Status);
    }

    /// <summary>
    /// #1780: a rule refuses every checkpoint that would end the child. Its handler commit (a completing child) and its
    /// incident strategy's fault (a faulting child) reach the drain in different shapes, and both used to leave the child
    /// at its last accepted checkpoint with the parent waiting forever. The child now ends Faulted with an incident
    /// recording the refusal, and the parent resumes through the ordinary child-faulted path.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_child_whose_terminal_checkpoint_a_rule_refuses_is_faulted_and_its_parent_resumes(bool childFaults)
    {
        _childCommitFailure.Refuse();

        var parent = await RunParentAsync(childFaults);

        parent.AssertWorkflowCompleted();
        parent.AssertOutcomes(DispatchNodeId, DispatchWorkflowOutcomes.Faulted);
        parent.AssertCompleted(AfterDispatchNodeId);
        var dispatch = Assert.Single(await ListDispatchesAsync());
        Assert.Equal(WorkflowDispatchStatus.Faulted, dispatch.Status);
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await _harness.ReadRunAsync(dispatch.ChildWorkflowExecutionId)).WorkflowState?.Status);
        var incident = Assert.Single(
            await ListBlockingIncidentsAsync(dispatch.ChildWorkflowExecutionId),
            candidate => candidate.FailureType == CheckpointRuleViolationWorkflowFaulter.IncidentFailureType);
        Assert.Equal(CheckpointRuleViolationWorkflowFaulter.IncidentId(dispatch.ChildWorkflowExecutionId), incident.IncidentId);
        Assert.Equal(IncidentResolutionActionKinds.FaultWorkflow, incident.ResolutionOutcome?.ActionKind);
        Assert.Equal(IncidentResolutionSystemSources.CheckpointRuleViolation, incident.ResolutionOutcome?.SystemSource);
        Assert.Contains(ScriptedChildCommitFailure.RefusalMessage, incident.Message, StringComparison.Ordinal);
        // The child really started, so its start was delivered rather than failed.
        Assert.Equal(0, _outboxFailures);
        Assert.True(_childCommitFailure.Failures > 0);
    }

    /// <summary>
    /// A rule refuses the child's first commit, so no child execution exists for the runtime to fault. That start is a
    /// failed delivery, not an accepted one: it fails final on its first attempt, dead-letters with its delivery incident,
    /// and the parent resumes through <c>DispatchFailed</c> instead of waiting forever.
    /// </summary>
    [Fact]
    public async Task A_child_whose_first_checkpoint_a_rule_refuses_fails_its_start_and_its_parent_resumes()
    {
        _childCommitFailure.RefuseFirst();

        var parent = await RunParentAsync(childFaults: false);

        parent.AssertWorkflowCompleted();
        parent.AssertOutcomes(DispatchNodeId, DispatchWorkflowOutcomes.DispatchFailed);
        parent.AssertCompleted(AfterDispatchNodeId);
        var dispatch = Assert.Single(await ListDispatchesAsync());
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, dispatch.Status);
        Assert.NotNull(WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(dispatch));
        Assert.NotNull(WorkflowDispatchLifecycle.ReadDeliveryIncidentId(dispatch));
        Assert.Null((await _harness.ReadRunAsync(dispatch.ChildWorkflowExecutionId)).WorkflowState);
        Assert.Equal(1, _outboxFailures);
        Assert.True(_childCommitFailure.Failures > 0);
    }

    /// <summary>
    /// The direction that could pass for success: a failure that is not a rule refusal must not fault the child. A healthy
    /// child whose terminal commit fails transiently during its start, after admission, is retried by the domain retry
    /// policy, completes, and its start stays delivered.
    /// </summary>
    [Fact]
    public async Task A_child_whose_terminal_checkpoint_fails_transiently_is_not_faulted_and_completes()
    {
        _childCommitFailure.FailTransientlyOnce();
        _retryPolicy.RetryNow = true;

        var parent = await RunParentAsync(childFaults: false);

        parent.AssertWorkflowCompleted();
        parent.AssertOutcomes(DispatchNodeId, DispatchWorkflowOutcomes.Completed);
        var dispatch = Assert.Single(await ListDispatchesAsync());
        Assert.Equal(WorkflowDispatchStatus.Completed, dispatch.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await _harness.ReadRunAsync(dispatch.ChildWorkflowExecutionId)).WorkflowState?.Status);
        Assert.DoesNotContain(
            await ListBlockingIncidentsAsync(dispatch.ChildWorkflowExecutionId),
            candidate => candidate.FailureType == CheckpointRuleViolationWorkflowFaulter.IncidentFailureType);
        Assert.Equal(0, _outboxFailures);
        Assert.Equal(1, _childCommitFailure.Failures);
        Assert.Equal(1, _retryPolicy.Retries);
    }

    /// <summary>
    /// The other direction that could pass for success: when the rule refuses the fault commit too, the child cannot be
    /// faulted, and its refusal must not be absorbed into an accepted start. The start fails final instead, visibly, and
    /// the existing child still resolves it as delivered, so the parent keeps waiting.
    /// </summary>
    [Fact]
    public async Task A_child_that_cannot_be_faulted_fails_its_start_instead_of_passing_for_delivered()
    {
        _childCommitFailure.RefuseIncludingTheFault();

        var parent = await RunParentAsync(childFaults: false, _ => _outboxFailures > 0, "see its child's start fail");

        Assert.False(parent.WorkflowState?.Status.IsTerminal());
        var dispatch = Assert.Single(await ListDispatchesAsync());
        Assert.Equal(WorkflowDispatchStatus.Started, dispatch.Status);
        Assert.False((await _harness.ReadRunAsync(dispatch.ChildWorkflowExecutionId)).WorkflowState?.Status.IsTerminal());
        Assert.DoesNotContain(
            await ListBlockingIncidentsAsync(dispatch.ChildWorkflowExecutionId),
            candidate => candidate.FailureType == CheckpointRuleViolationWorkflowFaulter.IncidentFailureType);
    }

    /// <summary>
    /// #1799 path 1. A rule refuses the child's first commit, so the start must fail. Recording that failure fails, which
    /// leaves the claimed outbox item Delivering; claim expiry then redelivers the same deterministic start. The refusal
    /// must survive that redelivery: the dispatch must still reach DispatchFailed and the parent must still resume.
    /// </summary>
    [Fact]
    public async Task A_refused_first_start_whose_recording_fails_still_fails_after_claim_expiry()
    {
        _childCommitFailure.RefuseFirst();
        _recordingFailure.FailNextCompletion();

        // Sweep until the delivery failure could not be recorded, which strands the claim.
        await RunParentAsync(
            childFaults: false,
            _ => _recordingFailure.Failures > 0,
            "fail to record its child start's delivery result");

        var claimed = Assert.IsType<RuntimePostCommitOutboxItem>(await FindStartOutboxItemAsync());
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, claimed.Status);
        Assert.Equal(WorkflowDispatchStatus.Started, Assert.Single(await ListDispatchesAsync()).Status);

        // Past the processor's one-minute claim visibility timeout, so the same deterministic start is redelivered.
        _clock.Advance(TimeSpan.FromSeconds(61));
        _outboxFailures += (await _harness.SweepAsync()).OutboxFailedCount;

        // The agent answers Duplicate because it consumed the key before the refusal was reported, so nothing about the
        // redelivery itself carries the refusal. The start fails all the same, because no child exists to prove otherwise.
        var redelivered = Assert.IsType<RuntimePostCommitOutboxItem>(await FindStartOutboxItemAsync());
        var dispatch = Assert.Single(await ListDispatchesAsync());
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, redelivered.Status);
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, dispatch.Status);
        Assert.NotNull(WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(dispatch));
        Assert.NotNull(WorkflowDispatchLifecycle.ReadDeliveryIncidentId(dispatch));
        Assert.Null((await _harness.ReadRunAsync(dispatch.ChildWorkflowExecutionId)).WorkflowState);

        // Which is the point: the parent resumes instead of waiting forever.
        var parent = await SweepUntilAsync(run => run.WorkflowState?.Status.IsTerminal() == true, "reach a terminal status");
        parent.AssertWorkflowCompleted();
        parent.AssertOutcomes(DispatchNodeId, DispatchWorkflowOutcomes.DispatchFailed);
        parent.AssertCompleted(AfterDispatchNodeId);
    }

    private async Task<RuntimePostCommitOutboxItem?> FindStartOutboxItemAsync()
    {
        var outboxItemId = Assert.IsType<string>(_recordingFailure.LastClaimedStartItemId);
        await using var scope = _harness.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>().FindAsync(outboxItemId);
    }

    private Task<WorkflowExecutionRun> RunParentAsync(bool childFaults) =>
        RunParentAsync(childFaults, run => run.WorkflowState?.Status.IsTerminal() == true, "reach a terminal status");

    /// <summary>
    /// Starts the parent, then delivers due post-commit work until <paramref name="reached"/> holds. Delivery retries are
    /// real-time, so this polls against a deadline rather than a fixed number of sweeps, and reports where the chain stopped
    /// when it never gets there.
    /// </summary>
    private async Task<WorkflowExecutionRun> RunParentAsync(
        bool childFaults,
        Func<WorkflowExecutionRun, bool> reached,
        string expectation)
    {
        var child = NewChildExecutable(childFaults);
        var childReference = await _harness.PublishAsync(child, "source-child");
        var parentReference = await _harness.PublishAsync(NewParentExecutable(child.Identity, childReference), "source-parent");

        // Published at the root, which runs in the default persistence scope; the parent (and so its child) must run there too.
        await _harness.StartPublishedAsync(
            parentReference,
            ParentWorkflowExecutionId,
            correlationId: "correlation-parent",
            partition: new WorkflowExecutionPartition(PersistenceScope.DefaultValue));

        return await SweepUntilAsync(reached, expectation);
    }

    private async Task<WorkflowExecutionRun> SweepUntilAsync(Func<WorkflowExecutionRun, bool> reached, string expectation)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                _outboxFailures += (await _harness.SweepAsync()).OutboxFailedCount;
            }
            catch (OutboxProcessingException)
            {
                // The delivery failure could not be recorded, so the claimed item stays Delivering until claim expiry.
                _recordingFailure.Observe();
            }

            var run = await _harness.ReadRunAsync(ParentWorkflowExecutionId);
            if (reached(run))
                return run;
            if (deadline.Elapsed > TerminalTimeout)
                throw new TimeoutException($"The parent did not {expectation} within {TerminalTimeout}. {await DescribeAsync(run)}");
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private async Task<string> DescribeAsync(WorkflowExecutionRun parent)
    {
        var dispatches = await ListDispatchesAsync();
        var activities = string.Join(", ", parent.ActivityStates.Select(state => $"{state.Execution.ExecutableNodeId}={state.Status}"));
        return $"Parent: {parent.WorkflowState?.Status}; activities: {activities}; dispatches: {string.Join(", ", dispatches.Select(dispatch => dispatch.Status))}.";
    }

    /// <summary>EF stores are scoped, so each read resolves its store in a scope of its own.</summary>
    private async Task<IReadOnlyCollection<WorkflowDispatchRecord>> ListDispatchesAsync()
    {
        await using var scope = _harness.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowDispatchStore>().ListAsync(ParentWorkflowExecutionId);
    }

    private async Task<IReadOnlyCollection<IncidentState>> ListBlockingIncidentsAsync(string workflowExecutionId)
    {
        await using var scope = _harness.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(workflowExecutionId);
    }

    private static WorkflowExecutable NewParentExecutable(WorkflowExecutableIdentity childIdentity, WorkflowExecutableSourceReference childReference)
    {
        var pin = new DispatchWorkflowPin(childIdentity, WorkflowExecutableSourceProvenance.From(childReference));
        var dispatch = Leaf(
            DispatchNodeId,
            typeof(DispatchWorkflowActivity),
            new Dictionary<string, object?>
            {
                [nameof(DispatchWorkflowActivity.WorkflowDefinitionId)] = childIdentity.DefinitionId,
                [nameof(DispatchWorkflowActivity.WaitForCompletion)] = true
            },
            new Dictionary<string, string>
            {
                [DispatchWorkflowConstants.PinnedTargetMetadataKey] = JsonSerializer.Serialize(pin, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            });
        var after = WorkflowExecutionHarness.NewProbeNode(AfterDispatchNodeId);
        var root = new ExecutableNode(
            executableNodeId: "node-sequence",
            authoredActivityId: "authored-node-sequence",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [dispatch, after])],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = new[] { DispatchNodeId, AfterDispatchNodeId } })));

        // The resume-target map the publish compiler emits for a waiting dispatch node.
        var resumeTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(DispatchNodeId, DispatchWorkflowConstants.CompletionResumeTargetId);
        return new WorkflowExecutable(
            identity: Identity("parent"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                [resumeTargetId] = new(resumeTargetId, DispatchNodeId, "OnChildCompletedAsync", new Dictionary<string, string>(), DispatchWorkflowConstants.CompletionResumeTargetId)
            },
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: [],
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowExecutable NewChildExecutable(bool faults) =>
        new(
            identity: Identity("child"),
            rootActivity: faults ? WorkflowExecutionHarness.NewFaultingNode(ChildNodeId) : WorkflowExecutionHarness.NewProbeNode(ChildNodeId),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            // DispatchWorkflow refuses a child without a supported input contract; an empty one is the minimum.
            inputContract: new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []),
            dependencies: [],
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private static WorkflowExecutableIdentity Identity(string key) =>
        new($"artifact-{key}", $"definition-{key}", $"version-{key}", "1.0.0", $"sha256:{key}");

    /// <summary>A leaf CLR activity with literal inputs; the harness pins the real contract from the activity type.</summary>
    private static ExecutableNode Leaf(
        string nodeId,
        Type activityType,
        IReadOnlyDictionary<string, object?> inputs,
        IReadOnlyDictionary<string, string> metadata) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: inputs.ToDictionary(
                input => input.Key,
                input =>
                {
                    var type = new ValueTypeDescriptor(input.Value is bool ? "Boolean" : "String");
                    return new RuntimeInputBinding(
                        input.Key,
                        type,
                        ValueProtectionPolicy.InstanceInline,
                        RuntimeInputBindingSource.Literal,
                        literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(input.Value), ValueProtectionPolicy.InstanceInline));
                }),
            metadata: metadata);

    /// <summary>
    /// Fails the commits that would end the child, as the last enricher, so it sees each commit as a store receives it.
    /// A refusal stands in for a checkpoint rule the terminal content breaks; unless told otherwise it lets through the
    /// fault the runtime commits for a refusal, which carries none of that content. Refusing the first commit refuses
    /// every commit that carries the child's state, so none is ever accepted. A transient failure stands in for an
    /// infrastructure fault.
    /// </summary>
    private sealed class ScriptedChildCommitFailure(string parentWorkflowExecutionId) : IRuntimeCheckpointCommitEnricher
    {
        public const string RefusalMessage = "A test checkpoint rule refuses the child's checkpoint.";
        private Func<Exception>? _failure;
        private bool _includingTheFault;
        private bool _firstCommit;
        private int _remaining;
        private int _failures;

        public int Order => int.MaxValue;

        public int Failures => _failures;

        public void Refuse() => (_failure, _remaining) = (() => new RuntimeCheckpointCommitValidationException(RefusalMessage), int.MaxValue);

        public void RefuseIncludingTheFault()
        {
            Refuse();
            _includingTheFault = true;
        }

        public void RefuseFirst()
        {
            RefuseIncludingTheFault();
            _firstCommit = true;
        }

        public void FailTransientlyOnce() => (_failure, _remaining) = (() => new InvalidOperationException("A transient checkpoint store outage."), 1);

        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        {
            var endsChild = commit.StateChanges.WorkflowExecution?.State is { } state &&
                            state.ParentWorkflowExecutionId == parentWorkflowExecutionId &&
                            (_firstCommit || state.Status.IsTerminal()) &&
                            (_includingTheFault ||
                             commit.Checkpoint.Metadata.GetValueOrDefault(RuntimeMetadataKeys.CheckpointReason) != CheckpointRuleViolationWorkflowFaulter.IncidentFailureType);
            if (_failure is null || !endsChild || Interlocked.Decrement(ref _remaining) < 0)
                return ValueTask.FromResult(commit);

            Interlocked.Increment(ref _failures);
            throw _failure();
        }
    }

    /// <summary>
    /// Wraps whichever store the backend registered so a scripted completion failure models the process failing to record
    /// a delivery result. Implements the whole outbox contract family because the processor resolves one service and casts.
    /// </summary>
    private static void DecorateOutboxStore(IServiceCollection services, ScriptedRecordingFailure failure)
    {
        var descriptor = services.Last(candidate => candidate.ServiceType == typeof(IRuntimePostCommitOutboxStore));
        var factory = descriptor.ImplementationFactory
            ?? throw new InvalidOperationException("The outbox store registration is expected to be a factory.");
        services.Remove(descriptor);
        services.Add(ServiceDescriptor.Describe(
            typeof(IRuntimePostCommitOutboxStore),
            provider => new FailingRecordingOutboxStore((IRuntimePostCommitOutboxStore)factory(provider), failure),
            descriptor.Lifetime));
    }

    private sealed class ScriptedRecordingFailure
    {
        private int _armed;
        private int _failures;

        public int Failures => _failures;

        /// <summary>The outbox item id of the most recently claimed child-start intent.</summary>
        public string? LastClaimedStartItemId { get; private set; }

        public void ObserveClaims(IEnumerable<RuntimePostCommitOutboxClaim> claims)
        {
            foreach (var claim in claims)
            {
                if (StringComparer.Ordinal.Equals(claim.Item.Intent.Kind, DispatchWorkflowConstants.StartChildIntentKind))
                    LastClaimedStartItemId = claim.OutboxItemId;
            }
        }

        public void FailNextCompletion() => Interlocked.Exchange(ref _armed, 1);

        /// <summary>Records that the sweep surfaced the recording failure, so the driver can move past claim expiry.</summary>
        public void Observe() => Interlocked.CompareExchange(ref _failures, 1, 0);

        public void ThrowIfArmed()
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
                throw new InvalidOperationException("A test outage refuses to record the delivery result.");
        }
    }

    private sealed class FailingRecordingOutboxStore(IRuntimePostCommitOutboxStore inner, ScriptedRecordingFailure failure) :
        IRuntimePostCommitOutboxStore,
        IRuntimePostCommitOutboxClaimStore,
        IRuntimePostCommitOutboxClaimCompletionStore
    {
        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(
            RuntimePostCommitOutboxQuery query,
            CancellationToken cancellationToken = default) =>
            inner.GetDeliverableAsync(query, cancellationToken);

        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(
            RuntimePostCommitOutboxDeliveryResult result,
            CancellationToken cancellationToken = default) =>
            inner.RecordDeliveryResultAsync(result, cancellationToken);

        public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
            RuntimePostCommitOutboxClaimRequest request,
            CancellationToken cancellationToken = default)
        {
            var claims = await ((IRuntimePostCommitOutboxClaimStore)inner).ClaimAsync(request, cancellationToken);
            failure.ObserveClaims(claims);
            return claims;
        }

        public ValueTask RecordDeliveryResultAsync(
            RuntimePostCommitOutboxClaim claim,
            RuntimePostCommitOutboxDeliveryResult result,
            CancellationToken cancellationToken = default) =>
            ((IRuntimePostCommitOutboxClaimStore)inner).RecordDeliveryResultAsync(claim, result, cancellationToken);

        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> CompleteClaimAsync(
            RuntimePostCommitOutboxClaimCompletion completion,
            CancellationToken cancellationToken = default)
        {
            failure.ThrowIfArmed();
            return ((IRuntimePostCommitOutboxClaimCompletionStore)inner).CompleteClaimAsync(completion, cancellationToken);
        }
    }

    /// <summary>Wall time plus a test-controlled offset, so a case can jump past a visibility timeout without freezing time.</summary>
    private sealed class OffsetClock : TimeProvider
    {
        private long _offsetTicks;

        public void Advance(TimeSpan amount) => Interlocked.Add(ref _offsetTicks, amount.Ticks);

        public override DateTimeOffset GetUtcNow() =>
            System.GetUtcNow().AddTicks(Interlocked.Read(ref _offsetTicks));
    }

    /// <summary>The default policy, which never retries, until a case asks for a faulted work item to be retried at once.</summary>
    private sealed class SwitchableRetryPolicy : IRuntimeDomainRetryPolicy
    {
        private readonly NoopRuntimeDomainRetryPolicy _default = new();
        private int _retries;

        public bool RetryNow { get; set; }

        public int Retries => _retries;

        public RuntimeDomainRetryDecision Decide(RuntimeDomainRetryRequest request)
        {
            if (!RetryNow)
                return _default.Decide(request);

            Interlocked.Increment(ref _retries);
            return new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryNow, delay: null, reason: "The test retries a faulted work item at once.");
        }
    }
}
