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
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Resumption;
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
    private readonly ScriptedChildTerminalCommitFailure _childTerminalCommitFailure = new(ParentWorkflowExecutionId);
    private readonly SwitchableRetryPolicy _retryPolicy = new();
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
                services.AddScoped<IRuntimeCheckpointCommitEnricher>(_ => _childTerminalCommitFailure);
                services.AddSingleton<IRuntimeDomainRetryPolicy>(_retryPolicy);
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
        _childTerminalCommitFailure.Refuse();

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
        Assert.Contains(ScriptedChildTerminalCommitFailure.RefusalMessage, incident.Message, StringComparison.Ordinal);
        // The child really started, so its start was delivered rather than failed.
        Assert.Equal(0, _outboxFailures);
        Assert.True(_childTerminalCommitFailure.Failures > 0);
    }

    /// <summary>
    /// The direction that could pass for success: a failure that is not a rule refusal must not fault the child. A healthy
    /// child whose terminal commit fails transiently during its start, after admission, is retried by the domain retry
    /// policy, completes, and its start stays delivered.
    /// </summary>
    [Fact]
    public async Task A_child_whose_terminal_checkpoint_fails_transiently_is_not_faulted_and_completes()
    {
        _childTerminalCommitFailure.FailTransientlyOnce();
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
        Assert.Equal(1, _childTerminalCommitFailure.Failures);
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
        _childTerminalCommitFailure.RefuseIncludingTheFault();

        var parent = await RunParentAsync(childFaults: false, _ => _outboxFailures > 0, "see its child's start fail");

        Assert.False(parent.WorkflowState?.Status.IsTerminal());
        var dispatch = Assert.Single(await ListDispatchesAsync());
        Assert.Equal(WorkflowDispatchStatus.Started, dispatch.Status);
        Assert.False((await _harness.ReadRunAsync(dispatch.ChildWorkflowExecutionId)).WorkflowState?.Status.IsTerminal());
        Assert.DoesNotContain(
            await ListBlockingIncidentsAsync(dispatch.ChildWorkflowExecutionId),
            candidate => candidate.FailureType == CheckpointRuleViolationWorkflowFaulter.IncidentFailureType);
    }

    private Task<WorkflowExecutionRun> RunParentAsync(bool childFaults) =>
        RunParentAsync(childFaults, run => run.WorkflowState?.Status.IsTerminal() == true, "reach a terminal status");

    /// <summary>
    /// Starts the parent, then delivers due post-commit work until <paramref name="reached"/> holds. Delivery retries are
    /// real-time, so this polls against a deadline rather than a fixed number of sweeps, and reports where the chain stopped
    /// when it never gets there.
    /// </summary>
    private async Task<WorkflowExecutionRun> RunParentAsync(bool childFaults, Func<WorkflowExecutionRun, bool> reached, string expectation)
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

        var deadline = Stopwatch.StartNew();
        while (true)
        {
            _outboxFailures += (await _harness.SweepAsync()).OutboxFailedCount;
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
    /// fault the runtime commits for a refusal, which carries none of that content. A transient failure stands in for an
    /// infrastructure fault.
    /// </summary>
    private sealed class ScriptedChildTerminalCommitFailure(string parentWorkflowExecutionId) : IRuntimeCheckpointCommitEnricher
    {
        public const string RefusalMessage = "A test checkpoint rule refuses the child's terminal checkpoint.";
        private Func<Exception>? _failure;
        private bool _includingTheFault;
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

        public void FailTransientlyOnce() => (_failure, _remaining) = (() => new InvalidOperationException("A transient checkpoint store outage."), 1);

        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        {
            var endsChild = commit.StateChanges.WorkflowExecution?.State is { } state &&
                            state.ParentWorkflowExecutionId == parentWorkflowExecutionId &&
                            state.Status.IsTerminal() &&
                            (_includingTheFault ||
                             commit.Checkpoint.Metadata.GetValueOrDefault(RuntimeMetadataKeys.CheckpointReason) != CheckpointRuleViolationWorkflowFaulter.IncidentFailureType);
            if (_failure is null || !endsChild || Interlocked.Decrement(ref _remaining) < 0)
                return ValueTask.FromResult(commit);

            Interlocked.Increment(ref _failures);
            throw _failure();
        }
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
