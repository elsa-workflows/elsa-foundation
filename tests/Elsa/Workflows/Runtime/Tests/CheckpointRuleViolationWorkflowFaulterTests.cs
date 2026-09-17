using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
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
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeCheckpointCommitValidationException Refusal = new("The checkpoint broke a rule.");
    private readonly InMemoryWorkflowExecutionStateStore _executions = new();
    private readonly InMemoryIncidentStateStore _incidents = new();
    private readonly InMemoryRuntimeCheckpointCommitStore _store;
    private readonly CheckpointRuleViolationWorkflowFaulter _faulter;

    public CheckpointRuleViolationWorkflowFaulterTests()
    {
        _store = new InMemoryRuntimeCheckpointCommitStore(
            _executions,
            incidentStateStore: _incidents,
            rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
        _faulter = new CheckpointRuleViolationWorkflowFaulter(
            _executions,
            new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), _store, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            new FakeTimeProvider(Now));
    }

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

    private ValueTask<WorkflowExecutionState> SaveExecutionAsync(WorkflowExecutionStatus status) =>
        _executions.SaveAsync(new WorkflowExecutionState(
            WorkflowExecutionId: WorkflowExecutionId,
            PinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
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
