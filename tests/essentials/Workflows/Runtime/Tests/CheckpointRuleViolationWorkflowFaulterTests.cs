using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// #1780: a commit a checkpoint rule refused faults its execution with a commit built only from the last accepted state, so
/// the fault cannot offer the refused content again, and nothing but a rule refusal faults an execution.
/// </summary>
public sealed class CheckpointRuleViolationWorkflowFaulterTests
{
    private const string WorkflowExecutionId = "wfexec-refused";
    private const string ParentWorkflowExecutionId = "wfexec-parent";
    private static readonly WorkflowExecutableIdentity PinnedExecutable =
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");
    private static readonly WorkflowExecutableSourceProvenance PinnedSource =
        new("source-child", "definition", "definition-1", "version-1", "definition-1", "version-1", "1.0.0", "publication-child", "slot-child");
    private static readonly WorkflowTestScope TestScope =
        new("scope-child", Now.AddHours(1), "tenant-child", new WorkflowExecutionPartition("partition-child"));
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeCheckpointCommitValidationException Refusal = new("The checkpoint broke a rule.");
    private readonly InMemoryWorkflowExecutionStateStore _executions = new();
    private readonly InMemoryIncidentStateStore _incidents = new();
    private readonly InMemoryRuntimeCheckpointCommitStore _store;
    private readonly InMemoryWorkflowDispatchStore _dispatches = new();
    private readonly CheckpointRuleViolationWorkflowFaulter _faulter;

    public CheckpointRuleViolationWorkflowFaulterTests()
    {
        _store = new InMemoryRuntimeCheckpointCommitStore(
            _executions,
            incidentStateStore: _incidents,
            rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
        _faulter = NewFaulter(_dispatches);
    }

    private CheckpointRuleViolationWorkflowFaulter NewFaulter(IWorkflowDispatchStore? dispatchStore) =>
        new(
            _executions,
            new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), _store, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            new FakeTimeProvider(Now),
            logger: null,
            dispatchStore);

    public static TheoryData<RuntimeSchedulerDrainResult?, Exception?> Violations => new()
    {
        { DrainResult(checkpointRuleViolation: true), null },
        { DrainResult(), new AggregateException("One or more scheduler drain observers failed.", Refusal) }
    };

    public static TheoryData<RuntimeSchedulerDrainResult?, Exception?> OtherFailures => new()
    {
        { DrainResult(checkpointRuleViolation: false), null },
        { DrainResult(), null },
        { null, new InvalidOperationException("A transient checkpoint store outage.") },
        { null, new OperationCanceledException() }
    };

    [Theory]
    [MemberData(nameof(Violations))]
    public async Task A_refused_commit_faults_the_execution_with_only_its_state_and_an_incident(
        RuntimeSchedulerDrainResult? drainResult,
        Exception? drainFailure)
    {
        await SaveExecutionAsync(WorkflowExecutionStatus.Running);

        await _faulter.FaultIfCheckpointRuleViolatedAsync(WorkflowExecutionId, drainResult, drainFailure);

        var commit = Assert.Single(_store.ListCommits()).Commit;
        var changes = commit.StateChanges;
        Assert.Equal(RuntimeCheckpointNames.WorkflowFaulted, commit.Checkpoint.Name);
        Assert.Equal(WorkflowExecutionStatus.Faulted, changes.WorkflowExecution?.State.Status);
        var incident = Assert.Single(changes.Incidents).State;
        Assert.Equal(CheckpointRuleViolationWorkflowFaulter.IncidentId(WorkflowExecutionId), incident.IncidentId);
        Assert.Equal(IncidentStatus.Blocking, incident.Status);
        Assert.Equal(IncidentResolutionActionKinds.FaultWorkflow, incident.ResolutionOutcome?.ActionKind);
        Assert.Contains(Refusal.Message, incident.Message, StringComparison.Ordinal);
        Assert.Contains("last accepted checkpoint", incident.Message, StringComparison.Ordinal);
        Assert.Null(changes.Scheduler);
        Assert.Empty(changes.ActivityExecutions);
        Assert.Empty(changes.ActivityExecutionInspections);
        Assert.Empty(changes.Bookmarks);
        Assert.Empty(changes.DurableValues);
        Assert.Empty(changes.Operational);
        Assert.Empty(changes.WorkflowDispatches);
        Assert.Empty(changes.PostCommitOutbox);
        Assert.Empty(changes.ConsumedSchedulerWorkItems);
        Assert.Empty(commit.PostCommitIntents);
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await _executions.FindAsync(WorkflowExecutionId))?.Status);
    }

    [Theory]
    [MemberData(nameof(OtherFailures))]
    public async Task A_failure_that_is_not_a_rule_refusal_leaves_the_execution_running(
        RuntimeSchedulerDrainResult? drainResult,
        Exception? drainFailure)
    {
        await SaveExecutionAsync(WorkflowExecutionStatus.Running);

        await _faulter.FaultIfCheckpointRuleViolatedAsync(WorkflowExecutionId, drainResult, drainFailure);

        Assert.Empty(_store.ListCommits());
        Assert.Equal(WorkflowExecutionStatus.Running, (await _executions.FindAsync(WorkflowExecutionId))?.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(WorkflowExecutionStatus.Completed)]
    [InlineData(WorkflowExecutionStatus.Faulted)]
    public async Task An_execution_without_accepted_state_or_already_terminal_is_left_alone(WorkflowExecutionStatus? status)
    {
        if (status is { } saved)
            await SaveExecutionAsync(saved);

        await _faulter.FaultIfCheckpointRuleViolatedAsync(WorkflowExecutionId, DrainResult(checkpointRuleViolation: true), Refusal);

        Assert.Empty(_store.ListCommits());
        Assert.Equal(status, (await _executions.FindAsync(WorkflowExecutionId))?.Status);
    }

    /// <summary>An execution the faulter could not fault is never reported as handled.</summary>
    [Fact]
    public async Task A_fault_commit_that_fails_is_thrown()
    {
        await SaveExecutionAsync(WorkflowExecutionStatus.Running);
        await _incidents.TryAddAsync(new IncidentState(
            CheckpointRuleViolationWorkflowFaulter.IncidentId(WorkflowExecutionId),
            WorkflowExecutionId,
            null,
            null,
            IncidentSeverity.Critical,
            IncidentStatus.Blocking,
            new IncidentResolutionOutcome(IncidentResolutionActionKinds.WaitForIntervention, Now, strategy: null, systemSource: "test"),
            "test",
            "An earlier decision the fault may not overwrite.",
            Now,
            null));

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            _faulter.FaultIfCheckpointRuleViolatedAsync(WorkflowExecutionId, DrainResult(checkpointRuleViolation: true), null).AsTask());

        Assert.Empty(_store.ListCommits());
        Assert.Equal(WorkflowExecutionStatus.Running, (await _executions.FindAsync(WorkflowExecutionId))?.Status);
    }

    /// <summary>
    /// #1799: a refused FIRST commit leaves nothing in the store to fault, which is how a dispatched child's refusal used
    /// to vanish. The child is faulted into existence from its start command instead, carrying every field its dispatch is
    /// matched on, so the ordinary enrichers can project the dispatch and resume the parent. A start that declares no
    /// parent is still left alone: it reports its refusal to its caller and gains nothing from state that never ran.
    /// </summary>
    [Theory]
    [InlineData(ParentWorkflowExecutionId, true)]
    [InlineData(null, false)]
    public async Task A_refused_first_commit_faults_a_dispatched_start_into_existence(
        string? parentWorkflowExecutionId,
        bool expectFaulted)
    {
        await _faulter.FaultIfCheckpointRuleViolatedAsync(
            WorkflowExecutionId,
            DrainResult(checkpointRuleViolation: true),
            null,
            StartEnvelope(parentWorkflowExecutionId));

        var execution = await _executions.FindAsync(WorkflowExecutionId);
        if (!expectFaulted)
        {
            Assert.Empty(_store.ListCommits());
            Assert.Null(execution);
            return;
        }

        Assert.NotNull(execution);
        Assert.Equal(WorkflowExecutionStatus.Faulted, execution.Status);
        Assert.Equal(Now, execution.CompletedAt);

        // The dispatch this child is waited on by is matched on exactly these, so a synthesized child that drifted from
        // its start command would be read as a conflicting child rather than as the one that was dispatched.
        Assert.Equal(PinnedExecutable, execution.PinnedExecutable);
        Assert.Equal(parentWorkflowExecutionId, execution.ParentWorkflowExecutionId);
        Assert.Equal("correlation-child", execution.CorrelationId);
        Assert.Equal("tenant-child", execution.TenantId);
        Assert.Equal(WorkflowRunKind.PublishedRun, execution.RunKind);
        Assert.Equal(new WorkflowExecutionPartition("partition-child"), execution.Partition);
        Assert.Equal(3, execution.DispatchNestingDepth);
        Assert.Equal("system-child", execution.Authority?.SystemIdentity);
        Assert.Equal(PinnedSource, execution.PinnedSource);

        var incident = Assert.Single(await _incidents.ListBlockingAsync(WorkflowExecutionId));
        Assert.Equal(CheckpointRuleViolationWorkflowFaulter.IncidentId(WorkflowExecutionId), incident.IncidentId);
        Assert.Equal(CheckpointRuleViolationWorkflowFaulter.IncidentFailureType, incident.FailureType);

        // A child synthesized from its start command never reached a checkpoint, and the operator-facing text must not
        // claim one. The other arm of that message is asserted by the accepted-state case above.
        Assert.Contains("without ever reaching a checkpoint", incident.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("last accepted checkpoint", incident.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A TestRun child carries a test scope, and the durable child-evidence rule compares it strictly. A synthesized child
    /// that dropped it would make that rule throw inside the claim completion, so the start could never complete at all.
    /// </summary>
    [Fact]
    public async Task A_synthesized_test_run_start_keeps_its_test_scope()
    {
        await _faulter.FaultIfCheckpointRuleViolatedAsync(
            WorkflowExecutionId,
            DrainResult(checkpointRuleViolation: true),
            null,
            StartEnvelope(ParentWorkflowExecutionId, testRun: true));

        var execution = await _executions.FindAsync(WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Faulted, execution?.Status);
        Assert.Equal(WorkflowRunKind.TestRun, execution?.RunKind);
        Assert.Equal(TestScope.ScopeId, execution?.TestScope?.ScopeId);
        Assert.Equal(TestScope.ExpiresAt, execution?.TestScope?.ExpiresAt);
    }

    /// <summary>
    /// Synthesis is worth doing only because the ordinary enrichers then project the child's dispatch, and that projection
    /// needs the dispatch store's additive query capability. Without it a synthesized child would be a terminal execution
    /// nothing can act on, and the durable child-evidence rule would read it as a delivered start, so the refusal keeps the
    /// route it already had.
    /// </summary>
    [Fact]
    public async Task A_refused_first_commit_is_left_alone_when_its_dispatch_cannot_be_projected()
    {
        await NewFaulter(new NonQueryableWorkflowDispatchStore()).FaultIfCheckpointRuleViolatedAsync(
            WorkflowExecutionId,
            DrainResult(checkpointRuleViolation: true),
            null,
            StartEnvelope(ParentWorkflowExecutionId));

        Assert.Empty(_store.ListCommits());
        Assert.Null(await _executions.FindAsync(WorkflowExecutionId));
    }

    /// <summary>
    /// A payload this runtime cannot read cannot stand in for the execution, and must not escape as a deserialization
    /// failure in place of the refusal. Both shapes the payload rejects are covered: one the reader cannot parse, and one
    /// it parses but the payload's own validation refuses.
    /// </summary>
    [Theory]
    // Unparseable.
    [InlineData("{\"PinnedExecutable\":\"not-an-identity\"}")]
    // Parseable, rejected by the payload's own validation.
    [InlineData("{\"RequestedArtifactId\":\"artifact-1\"}")]
    // Rejected on a parameter outside the start work handler's whitelist, which a narrowed catch would let escape.
    [InlineData("{\"PinnedExecutable\":{\"ArtifactId\":\"artifact-1\",\"DefinitionId\":\"definition-1\",\"DefinitionVersionId\":\"version-1\",\"ArtifactVersion\":\"1.0.0\",\"ArtifactHash\":\"sha256:test\"},\"RequestedArtifactId\":\"artifact-1\",\"RunKind\":2,\"TestScope\":{\"ScopeId\":\"scope-child\",\"ExpiresAt\":\"2026-09-17T10:00:00+00:00\",\"TenantId\":\"tenant-child\",\"Partition\":{\"Value\":\"partition-child\"}}}")]
    public async Task A_refused_first_commit_with_an_unreadable_start_payload_is_left_alone(string payloadJson)
    {
        await _faulter.FaultIfCheckpointRuleViolatedAsync(
            WorkflowExecutionId,
            DrainResult(checkpointRuleViolation: true),
            null,
            StartEnvelope(ParentWorkflowExecutionId, payloadElement: JsonDocument.Parse(payloadJson).RootElement));

        Assert.Empty(_store.ListCommits());
        Assert.Null(await _executions.FindAsync(WorkflowExecutionId));
    }

    /// <summary>
    /// A forwarded start reaches the owning node through the durable transport, which re-serializes the whole envelope with
    /// web options while the payload inside it was written with default ones. This pins that the envelope survives that
    /// round trip intact, carrying a payload the faulter can still read, on exactly the path #1799 is about. It does not
    /// pin the producer's own options: the reader is case-sensitive, so a producer that switched to web options would break
    /// synthesis in a way only a test built from the real producer would catch.
    /// </summary>
    [Fact]
    public async Task A_start_that_round_tripped_through_the_command_transport_still_synthesizes_its_child()
    {
        var transportOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var forwarded = JsonSerializer.Deserialize<WorkflowExecutionCommandEnvelope>(
            JsonSerializer.Serialize(StartEnvelope(ParentWorkflowExecutionId), transportOptions),
            transportOptions);

        await _faulter.FaultIfCheckpointRuleViolatedAsync(
            WorkflowExecutionId,
            DrainResult(checkpointRuleViolation: true),
            null,
            forwarded);

        var execution = await _executions.FindAsync(WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Faulted, execution?.Status);
        Assert.Equal(ParentWorkflowExecutionId, execution?.ParentWorkflowExecutionId);
        Assert.Equal(3, execution?.DispatchNestingDepth);
    }

    /// <summary>Only this execution's own start can stand in for it; anything else leaves the refusal alone.</summary>
    [Theory]
    [InlineData(WorkflowExecutionCommandKind.RunSchedulerWork, WorkflowExecutionId)]
    [InlineData(WorkflowExecutionCommandKind.Start, "wfexec-other")]
    public async Task A_refused_first_commit_without_this_execution_s_start_is_left_alone(
        WorkflowExecutionCommandKind kind,
        string envelopeWorkflowExecutionId)
    {
        await _faulter.FaultIfCheckpointRuleViolatedAsync(
            WorkflowExecutionId,
            DrainResult(checkpointRuleViolation: true),
            null,
            StartEnvelope(ParentWorkflowExecutionId, kind, envelopeWorkflowExecutionId));

        Assert.Empty(_store.ListCommits());
        Assert.Null(await _executions.FindAsync(WorkflowExecutionId));
    }

    private static WorkflowExecutionCommandEnvelope StartEnvelope(
        string? parentWorkflowExecutionId,
        WorkflowExecutionCommandKind kind = WorkflowExecutionCommandKind.Start,
        string? workflowExecutionId = null,
        bool testRun = false,
        JsonElement? payloadElement = null)
    {
        var executionId = workflowExecutionId ?? WorkflowExecutionId;
        var payload = new WorkflowExecutionStartCommandPayload(
            pinnedExecutable: PinnedExecutable,
            requestedArtifactId: PinnedExecutable.ArtifactId,
            variables: null,
            inputs: null,
            stimulusInput: null,
            triggerNodeId: null,
            runKind: testRun ? WorkflowRunKind.TestRun : WorkflowRunKind.PublishedRun,
            pinnedSource: PinnedSource,
            parentWorkflowExecutionId: parentWorkflowExecutionId,
            correlationId: "correlation-child",
            tenantId: "tenant-child",
            partition: new WorkflowExecutionPartition("partition-child"),
            authority: new WorkflowExecutionAuthoritySnapshot("system-child", "root-child"),
            startAuthority: null,
            dispatchNestingDepth: 3,
            testScope: testRun ? TestScope : null);
        var command = new WorkflowExecutionCommand(
            CommandId: "command-start",
            WorkflowExecutionId: executionId,
            Kind: kind,
            EnqueuedAt: Now,
            Payload: payloadElement ?? JsonSerializer.SerializeToElement(payload),
            Metadata: new Dictionary<string, string>());
        return new WorkflowExecutionCommandEnvelope(
            envelopeId: "envelope-start",
            workflowExecutionId: executionId,
            command: command,
            idempotencyKey: "idem-start",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now);
    }

    /// <summary>A dispatch store that never adopted the additive query capability, so no terminal projection can run.</summary>
    private sealed class NonQueryableWorkflowDispatchStore : IWorkflowDispatchStore
    {
        public ValueTask<WorkflowDispatchRecord> SaveAsync(WorkflowDispatchRecord record, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(record);

        public ValueTask<WorkflowDispatchRecord?> FindAsync(string dispatchId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowDispatchRecord?>(null);

        public ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> ListAsync(string parentWorkflowExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<WorkflowDispatchRecord>>([]);
    }

    private ValueTask<WorkflowExecutionState> SaveExecutionAsync(WorkflowExecutionStatus status) =>
        _executions.SaveAsync(new WorkflowExecutionState(
            WorkflowExecutionId: WorkflowExecutionId,
            PinnedExecutable: PinnedExecutable,
            Status: status,
            SubStatus: null,
            CreatedAt: Now,
            StartedAt: Now,
            UpdatedAt: Now,
            CompletedAt: status.IsTerminal() ? Now : null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>()));

    /// <summary>A drain with one faulted work item, or an empty one when <paramref name="checkpointRuleViolation"/> is null.</summary>
    private static RuntimeSchedulerDrainResult DrainResult(bool? checkpointRuleViolation = null) =>
        new(
            WorkflowExecutionId,
            Now,
            Now,
            checkpointRuleViolation is { } violation
                ?
                [
                    new RuntimeSchedulerWorkItemResult(
                        "work-refused",
                        WorkflowExecutionId,
                        WorkflowExecutionCommandKind.Checkpoint,
                        RuntimeSchedulerWorkItemResultStatus.Faulted,
                        "handler",
                        Now,
                        Now,
                        error: $"{typeof(RuntimeCheckpointCommitValidationException).FullName}: {Refusal.Message}",
                        checkpointRuleViolation: violation)
                ]
                : []);
}
