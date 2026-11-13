using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeHistoryValueOwnerTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v3", 3, true, ["ValueCaptured"]);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Retained_policy_keeps_invoke_resume_and_output_deltas_distinct_at_equal_clocks(bool newAdmissionsEnabled)
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(Workflow(Policy));
        var inspections = new InMemoryActivityExecutionInspectionStore();
        var values = new RuntimeActivityExecutionInspectionAccumulator(inspections, executions, new RuntimeHistoryOptions { Enabled = newAdmissionsEnabled });
        var input = Snapshot("invoke");
        var first = await values.BuildProjectionWithSourceAsync(State(), "invoke-checkpoint", At,
            () => new("run", "invoke-work", ActivityValueCapturePhase.Invoke), valueSnapshots: [input]);
        await inspections.SaveAsync(first);
        var output = input with { Subject = ActivityExecutionInspectionValueSubject.ActivityOutput, Payload = JsonSerializer.SerializeToElement("output") };
        var resumed = await values.BuildProjectionWithSourceAsync(State(), "resume-checkpoint", At,
            () => new("run", "resume-work", ActivityValueCapturePhase.Resume), valueSnapshots: [Snapshot("resume"), output]);

        Assert.True(values.SupportsCheckpointValueCapture);
        Assert.Equal(3, resumed.ValueSnapshots.Count);
        Assert.Equal(3, resumed.ValueSnapshots.Select(value => value.EvidenceId).Distinct(StringComparer.Ordinal).Count());
        var delta = resumed.CheckpointValueCapture!;
        Assert.Equal("resume-checkpoint", delta.CheckpointId);
        Assert.Equal("resume-work", delta.SchedulerWorkItemId);
        Assert.Equal(2, delta.EvidenceIds.Count);
        Assert.All(delta.EvidenceIds, id =>
        {
            var value = Assert.Single(resumed.ValueSnapshots, value => value.EvidenceId == id);
            Assert.Equal("Resume", value.Phase);
            Assert.NotNull(value.EvaluationId);
        });
        Assert.Equal("Invoke", Assert.Single(resumed.ValueSnapshots, value => !delta.EvidenceIds.Contains(value.EvidenceId!)).Phase);
        Assert.Contains(resumed.ValueSnapshots, value => value.Subject == ActivityExecutionInspectionValueSubject.ActivityOutput && value.Payload!.Value.GetString() == "output");
    }

    [Fact]
    public async Task Fresh_owner_replaying_the_same_raw_input_keeps_its_original_delta_after_later_projection()
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(Workflow(Policy));
        var inspections = new InMemoryActivityExecutionInspectionStore();
        var values = new RuntimeActivityExecutionInspectionAccumulator(inspections, executions, new RuntimeHistoryOptions());
        var raw = Snapshot("original");
        var first = await values.BuildProjectionWithSourceAsync(State(), "checkpoint-A", At,
            () => new("run", "work-A", ActivityValueCapturePhase.Invoke), valueSnapshots: [raw]);
        await inspections.SaveAsync(first);
        var later = await values.BuildProjectionWithSourceAsync(State(), "checkpoint-B", At.AddMinutes(1),
            () => new("run", "work-B", ActivityValueCapturePhase.Resume), valueSnapshots: [Snapshot("later")]);
        await inspections.SaveAsync(later);

        var fresh = new RuntimeActivityExecutionInspectionAccumulator(inspections, executions, new RuntimeHistoryOptions());
        var replay = await fresh.BuildProjectionWithSourceAsync(State(), "checkpoint-A", At,
            () => new("run", "work-A", ActivityValueCapturePhase.Invoke), valueSnapshots: [raw]);
        Assert.Equal(first.CheckpointValueCapture!.EvidenceIds, replay.CheckpointValueCapture!.EvidenceIds);
        Assert.Equal("work-A", replay.CheckpointValueCapture.SchedulerWorkItemId);
        Assert.Single(replay.CheckpointValueCapture.EvidenceIds);
        Assert.Equal(2, replay.ValueSnapshots.Count);
        Assert.Equal("original", Assert.Single(replay.ValueSnapshots, value => value.EvidenceId == replay.CheckpointValueCapture.EvidenceIds[0]).Payload!.Value.GetString());
    }

    [Fact]
    public async Task Reprojection_retains_existing_values_but_records_a_known_empty_current_delta()
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(Workflow(Policy));
        var inspections = new InMemoryActivityExecutionInspectionStore();
        var values = new RuntimeActivityExecutionInspectionAccumulator(inspections, executions, new RuntimeHistoryOptions());
        var first = await values.BuildProjectionWithSourceAsync(State(), "checkpoint-A", At,
            () => new("run", "work-A", ActivityValueCapturePhase.Invoke), valueSnapshots: [Snapshot("original")]);
        await inspections.SaveAsync(first);
        var reprojection = await values.BuildProjectionWithSourceAsync(State(), "checkpoint-B", At,
            () => new("run", "work-B", ActivityValueCapturePhase.Structural));
        Assert.Single(reprojection.ValueSnapshots);
        Assert.Empty(reprojection.CheckpointValueCapture!.EvidenceIds);
        Assert.Equal("checkpoint-B", reprojection.CheckpointValueCapture.CheckpointId);
    }

    [Fact]
    public async Task Same_evaluation_at_another_checkpoint_has_no_dangling_delta_reference()
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(Workflow(Policy));
        var inspections = new InMemoryActivityExecutionInspectionStore();
        var values = new RuntimeActivityExecutionInspectionAccumulator(inspections, executions, new RuntimeHistoryOptions());
        var raw = Snapshot("original") with { InputKey = "input" };
        var first = await values.BuildProjectionWithSourceAsync(State(), "checkpoint-A", At,
            () => new("run", "work", ActivityValueCapturePhase.Invoke), valueSnapshots: [raw]);
        await inspections.SaveAsync(first);
        var second = await values.BuildProjectionWithSourceAsync(State(), "checkpoint-B", At.AddMinutes(1),
            () => new("run", "work", ActivityValueCapturePhase.Invoke), valueSnapshots: [raw]);
        Assert.Equal(first.CheckpointValueCapture!.EvidenceIds, second.CheckpointValueCapture!.EvidenceIds);
        Assert.Equal("checkpoint-B", second.CheckpointValueCapture.CheckpointId);
        Assert.Equal(1L, Assert.Single(second.ValueSnapshots).Sequence);
        Assert.All(second.CheckpointValueCapture.EvidenceIds, id => Assert.Single(second.ValueSnapshots, value => value.EvidenceId == id));
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("phase")]
    [InlineData("disposition")]
    [InlineData("evaluation-identity")]
    public async Task Existing_evidence_or_evaluation_cannot_silently_hide_a_conflicting_observation(string conflict)
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(Workflow(Policy));
        var inspections = new InMemoryActivityExecutionInspectionStore();
        var values = new RuntimeActivityExecutionInspectionAccumulator(inspections, executions, new RuntimeHistoryOptions());
        var raw = Snapshot("original");
        var first = await values.BuildProjectionWithSourceAsync(State(), "checkpoint", At,
            () => new("run", "work", ActivityValueCapturePhase.Invoke), valueSnapshots: [raw]);
        await inspections.SaveAsync(first);
        var changed = conflict switch
        {
            "payload" => raw with { Payload = JsonSerializer.SerializeToElement("changed") },
            "phase" => raw with { Phase = "Resume" },
            "disposition" => raw with { CaptureReason = "withheld", IsSensitive = true },
            _ => raw with { EvaluationId = Assert.Single(first.ValueSnapshots).EvaluationId, EvidenceId = "different-evidence" }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await values.BuildProjectionWithSourceAsync(State(), "checkpoint", At,
            () => new("run", "work", ActivityValueCapturePhase.Invoke), valueSnapshots: [changed]));
        Assert.Equal("original", Assert.Single((await inspections.FindAsync("run", "activity"))!.ValueSnapshots).Payload!.Value.GetString());
    }

    [Fact]
    public async Task Duplicate_raw_identity_is_deduplicated_only_when_its_content_agrees()
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(Workflow(Policy));
        var values = new RuntimeActivityExecutionInspectionAccumulator(new InMemoryActivityExecutionInspectionStore(), executions, new RuntimeHistoryOptions());
        var raw = Snapshot("original");
        var equivalent = await values.BuildProjectionWithSourceAsync(State(), "checkpoint", At,
            () => new("run", "work", ActivityValueCapturePhase.Invoke), valueSnapshots: [raw, Snapshot("original")]);
        Assert.Single(equivalent.ValueSnapshots);
        Assert.Single(equivalent.CheckpointValueCapture!.EvidenceIds);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await values.BuildProjectionWithSourceAsync(State(), "checkpoint", At,
            () => new("run", "work", ActivityValueCapturePhase.Invoke), valueSnapshots: [raw, Snapshot("changed")]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uncaptured_and_legacy_composition_never_activate_an_origin_factory(bool configured)
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(Workflow(null));
        var inspections = new InMemoryActivityExecutionInspectionStore();
        var values = configured ? new RuntimeActivityExecutionInspectionAccumulator(inspections, executions, new RuntimeHistoryOptions())
            : new RuntimeActivityExecutionInspectionAccumulator(inspections);
        var projection = await values.BuildProjectionWithSourceAsync(State(), "legacy", At,
            () => throw new InvalidOperationException("Must remain lazy"), valueSnapshots: [Snapshot("value")]);

        Assert.Null(projection.CheckpointValueCapture);
        Assert.Null(Assert.Single(projection.ValueSnapshots).Phase);
        Assert.Equal(configured, values.SupportsCheckpointValueCapture);
    }

    [Fact]
    public async Task Captured_values_require_actual_owner_origin_and_exact_workflow_agreement()
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(Workflow(Policy));
        var values = new RuntimeActivityExecutionInspectionAccumulator(new InMemoryActivityExecutionInspectionStore(), executions, new RuntimeHistoryOptions());
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () =>
            await values.BuildProjectionAsync(State(), "checkpoint", At, valueSnapshots: [Snapshot("value")]));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await values.BuildProjectionWithSourceAsync(State(), "checkpoint", At,
                () => new("other-run", "work", ActivityValueCapturePhase.Invoke), valueSnapshots: [Snapshot("value")]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Policy_storage_outage_retains_its_cause_and_does_not_activate_the_owner(bool timeout)
    {
        Exception cause = timeout ? new TimeoutException("unavailable") : new IOException("unavailable");
        var values = new RuntimeActivityExecutionInspectionAccumulator(new InMemoryActivityExecutionInspectionStore(), new FailingPolicyStore(cause), new RuntimeHistoryOptions());
        var calls = 0;
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () =>
            await values.BuildProjectionWithSourceAsync(State(), "checkpoint", At,
                () => { calls++; return new("run", "work", ActivityValueCapturePhase.Invoke); }, valueSnapshots: [Snapshot("value")]));
        Assert.Same(cause, failure.InnerException);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Malformed_authoritative_policy_remains_a_hard_failure()
    {
        var cause = new InvalidDataException("malformed");
        var values = new RuntimeActivityExecutionInspectionAccumulator(new InMemoryActivityExecutionInspectionStore(), new FailingPolicyStore(cause), new RuntimeHistoryOptions());
        var failure = await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(async () =>
            await values.BuildProjectionWithSourceAsync(State(), "checkpoint", At,
                () => new("run", "work", ActivityValueCapturePhase.Invoke), valueSnapshots: [Snapshot("value")]));
        Assert.Same(cause, failure.InnerException);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Normalized_policy_and_inspection_outages_preserve_the_whole_provider_chain(bool inspection, bool providerTimeout)
    {
        Exception provider = providerTimeout ? new OperationCanceledException("provider timeout") : new TestDbException();
        var normalized = new InvalidOperationException("contextual persistence failure", provider);
        var values = await FailingOwner(inspection, normalized);
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () =>
            await values.BuildProjectionWithSourceAsync(State(), "checkpoint", At,
                () => new("run", "work", ActivityValueCapturePhase.Invoke), valueSnapshots: [Snapshot("value")]));
        Assert.Same(normalized, failure.InnerException);
        Assert.Same(provider, failure.InnerException!.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wrapped_malformed_evidence_stays_hard_even_inside_an_IO_boundary(bool inspection)
    {
        var normalized = new IOException("boundary", new InvalidDataException("malformed"));
        var values = await FailingOwner(inspection, normalized);
        var failure = await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(async () =>
            await values.BuildProjectionWithSourceAsync(State(), "checkpoint", At,
                () => new("run", "work", ActivityValueCapturePhase.Invoke), valueSnapshots: [Snapshot("value")]));
        Assert.Same(normalized, failure.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_a_read_is_not_reclassified_as_provider_unavailability(bool inspection)
    {
        using var cancellation = new CancellationTokenSource();
        var normalized = new InvalidOperationException("cancelled boundary", new OperationCanceledException(cancellation.Token));
        var values = await FailingOwner(inspection, normalized, cancellation.Cancel);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await values.BuildProjectionWithSourceAsync(State(), "checkpoint", At,
                () => new("run", "work", ActivityValueCapturePhase.Invoke), valueSnapshots: [Snapshot("value")], cancellationToken: cancellation.Token));
        Assert.Same(normalized, failure);
    }

    [Fact]
    public void Default_runtime_composition_resolves_the_owner_without_history_or_an_execution_scope()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var owner = scope.ServiceProvider.GetRequiredService<IRuntimeActivityExecutionInspectionAccumulator>();
        Assert.IsType<RuntimeActivityExecutionInspectionAccumulator>(owner);
        Assert.False(owner.SupportsCheckpointValueCapture);
        Assert.Null(scope.ServiceProvider.GetService<RuntimeHistoryOptions>());
    }

    private static WorkflowExecutionState Workflow(RuntimeHistoryCapturePolicy? policy) => new("run", new("artifact", "definition", "version", "1", "hash"),
        WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, null, new Dictionary<string, string>()) { HistoryCapturePolicy = policy };

    private static ActivityExecutionState State() => new(new("activity", "run", "node", "authored", "test", "1"),
        ActivityExecutionStatus.Running, null, 1, At, At, null, null, null, null, null,
        ActivitySchedulingProvenance.From("run", null, null, null, null, null, null, "test"), 0, [], [], 0, 0, new Dictionary<string, string>());

    private static ActivityExecutionInspectionValueSnapshot Snapshot(string value) => new("input", ActivityExecutionInspectionValueSubject.ActivityInput,
        default, null, At, JsonSerializer.SerializeToElement(value), "test", false, new Dictionary<string, string>());

    private static async Task<RuntimeActivityExecutionInspectionAccumulator> FailingOwner(bool inspection, Exception failure, Action? beforeFailure = null)
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(Workflow(Policy));
        return new(inspection ? new FailingInspectionStore(failure, beforeFailure) : new InMemoryActivityExecutionInspectionStore(),
            inspection ? executions : new FailingPolicyStore(failure, beforeFailure), new RuntimeHistoryOptions());
    }

    private sealed class TestDbException() : DbException("provider unavailable") { }

    private sealed class FailingInspectionStore(Exception failure, Action? beforeFailure) : IActivityExecutionInspectionStore
    {
        public ValueTask<ActivityExecutionInspectionProjection?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
        {
            beforeFailure?.Invoke();
            throw failure;
        }
        public ValueTask<ActivityExecutionInspectionSummaryPage> ListSummariesPageAsync(ActivityExecutionInspectionSummaryPageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FailingPolicyStore(Exception failure, Action? beforeFailure = null) : IWorkflowExecutionStateStore
    {
        public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            beforeFailure?.Invoke();
            throw failure;
        }
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
