using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Services.Coalescing;
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Triggers;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class NativeStandaloneTriggerTargetTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"StartAuthority\":{}}")]
    [InlineData("{\"StartAuthority\":{\"StandaloneTrigger\":null}}")]
    public async Task An_opaque_legacy_start_keeps_its_duplicate_response(string json)
    {
        var executor = new WitnessExecutor();
        using var actors = new InProcessWorkflowExecutionActorProvider(executor);
        var envelope = new WorkflowExecutionCommandEnvelope("legacy", "legacy-execution",
            new("legacy-command", "legacy-execution", WorkflowExecutionCommandKind.Start, Now,
                JsonDocument.Parse(json).RootElement.Clone(), new Dictionary<string, string>()),
            "legacy-key", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, Now);
        var actor = await actors.GetAgentAsync(new(envelope.WorkflowExecutionId, WorkflowExecutionActorActivationReason.Start,
            Now, "legacy", WorkflowExecutionActorCapabilities.None));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await actor.EnqueueAsync(envelope)).Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, (await actor.EnqueueAsync(envelope)).Status);
        Assert.Equal(1, executor.Calls);
    }

    [Fact]
    public void A_claimed_start_origin_requires_a_valid_typed_payload()
    {
        var envelope = new WorkflowExecutionCommandEnvelope("claimed", "running",
            new("command", "running", WorkflowExecutionCommandKind.Start, Now,
                JsonDocument.Parse("{\"StartAuthority\":{\"StandaloneTrigger\":{}}}").RootElement.Clone(), new Dictionary<string, string>()),
            "claimed-key", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, Now);
        Assert.ThrowsAny<Exception>(() => NativeIngressTargetEvidence.Origin(envelope));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_accepted_first_checkpoint_fault_remains_reconcilable_without_starting_root_work(bool failStart)
    {
        using var fixture = await Fixture.CreateAsync();
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Recipient); fixture.Seal(seal);
        var envelope = NativeIngressTargetEvidence.ReadEnvelope(seal);
        await AssertProtectedFirstFaultAsync(fixture, envelope, failStart);
        var receipt = await fixture.Target.ReconcileAsync(fixture.Item, fixture.Recipient, seal);
        Assert.Equal(envelope.WorkflowExecutionId, receipt!.EvidenceId);
    }

    [Fact]
    public async Task Retained_initial_work_remains_private_to_admission_and_is_absent_from_incident_diagnostics()
    {
        const string sentinel = "private-initial-input-3582-sentinel";
        using var fixture = await Fixture.CreateAsync(inputValue: sentinel);
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Recipient); fixture.Seal(seal);
        var envelope = NativeIngressTargetEvidence.ReadEnvelope(seal);
        var commit = await AssertProtectedFirstFaultAsync(fixture, envelope, false);
        Assert.Contains(sentinel, commit.ExpectedInitialStartFault!.Poison.Metadata[InitialStartFaultAuthority.WorkSnapshotMetadata]);
        var incident = Assert.Single(await fixture.Incidents.ListAsync(envelope.WorkflowExecutionId));
        var view = Elsa.Workflows.Runtime.Api.Models.IncidentStateView.From(incident, canInspectSensitiveValues: true);
        Assert.DoesNotContain(InitialStartFaultAuthority.WorkSnapshotMetadata, commit.Metadata.Keys);
        Assert.DoesNotContain(InitialStartFaultAuthority.WorkSnapshotMetadata, incident.Metadata.Keys);
        Assert.DoesNotContain(InitialStartFaultAuthority.WorkSnapshotMetadata, view.Metadata.Keys);
        Assert.DoesNotContain(sentinel, string.Join(" ", view.Metadata.Values));
    }

    [Fact]
    public async Task An_unsupported_initial_origin_cannot_prevent_the_original_poison_record()
    {
        using var fixture = await Fixture.CreateAsync();
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Recipient); fixture.Seal(seal);
        var start = StartItem(NativeIngressTargetEvidence.ReadEnvelope(seal));
        var payload = System.Text.Json.Nodes.JsonNode.Parse(start.Payload!.Value.GetRawText())!;
        payload["StartAuthority"] = System.Text.Json.Nodes.JsonNode.Parse("{\"Kind\":" + (int)WorkflowExecutableStartAuthorityKind.ModelWork + ",\"ModelWork\":{\"FormatVersion\":999}}");
        var future = new RuntimeSchedulerWorkItem(start.WorkItemId, start.WorkflowExecutionId, start.CommandId, start.CommandKind,
            start.EnvelopeId, start.IdempotencyKey, start.EnqueuedAt, start.RecordedAt, start.Sequence,
            JsonSerializer.SerializeToElement(payload), start.CommandMetadata, start.EnvelopeMetadata);
        var poison = new InMemoryWorkflowSchedulerPoisonStore();
        await fixture.Queue.EnqueueAsync(future);
        var drain = await new WorkflowSchedulerDrainer(fixture.Queue, [fixture.StartHandler], fixture.Executions, poison,
            new FaultTestPauseGate(), fixture.Clock, retryPolicy: new NoopRuntimeDomainRetryPolicy()).DrainAsync(new(future.WorkflowExecutionId));
        Assert.True(drain.StoppedOnFault);
        var record = Assert.Single(await poison.ListAsync(future.WorkflowExecutionId));
        Assert.Equal(typeof(InvalidOperationException).FullName, record.Fault.ExceptionType);
        Assert.Equal("Start scheduler work item payload is not a valid start command payload.", record.Fault.Message);
        Assert.Equal(typeof(NotSupportedException).FullName, record.InnerFault!.ExceptionType);
        Assert.DoesNotContain(InitialStartFaultAuthority.WorkSnapshotMetadata, record.Metadata.Keys);
        Assert.Empty((await fixture.Queue.ListAsync(new(future.WorkflowExecutionId))).Items);
        Assert.Null(await fixture.Executions.FindAsync(future.WorkflowExecutionId));
    }

    private sealed class FaultTestPauseGate : IWorkflowSchedulerPauseGate
    {
        public ValueTask<SchedulerPauseDecision?> EvaluateAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<SchedulerPauseDecision?>(null);
    }

    [Fact]
    public async Task Prepared_native_command_is_pure_deterministic_and_a_seal_or_actor_result_is_not_a_target_receipt()
    {
        using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Recipient);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        var repeat = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Recipient);
        Assert.True(first.IsEquivalentTo(repeat));
        Assert.Equal(0, fixture.Executor.Calls);
        Assert.Empty((await fixture.Queue.ListAsync(new(fixture.Recipient.StartIdentity.WorkflowExecutionId))).Items);
        fixture.Seal(first);
        var envelope = NativeIngressTargetEvidence.ReadEnvelope(first);
        var payload = envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Equal(Now, envelope.EnqueuedAt);
        Assert.Equal(fixture.Recipient.StartIdentity.CommandId, envelope.Command.CommandId);
        Assert.Equal("business-value", payload.Inputs["AccountId"].GetString());
        Assert.Null(payload.TriggerNodeId);
        Assert.Null(payload.ParentWorkflowExecutionId);
        Assert.Equal(WorkflowRunKind.PublishedRun, payload.RunKind);
        Assert.Equal(0, payload.DispatchNestingDepth);
        Assert.NotNull(payload.StartAuthority!.StandaloneTrigger);
        await fixture.Target.DispatchAsync(fixture.Item, fixture.Recipient, first);
        Assert.Equal(1, fixture.Executor.Calls);
        Assert.Null(await fixture.Target.ReconcileAsync(fixture.Item, fixture.Recipient, first));
    }

    [Fact]
    public async Task Cancelled_unsealed_origin_cannot_reach_an_actor_or_create_a_checkpoint()
    {
        using var fixture = await Fixture.CreateAsync();
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Recipient);
        fixture.Ingress.Cancelled = true;
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Target.DispatchAsync(fixture.Item, fixture.Recipient, seal).AsTask());
        var start = StartItem(NativeIngressTargetEvidence.ReadEnvelope(seal));
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.StartHandler.HandleAsync(start).AsTask());
        Assert.Equal(0, fixture.Executor.Calls);
        Assert.Empty((await fixture.Queue.ListAsync(new(start.WorkflowExecutionId))).Items);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_ingress_physical_checkpoint_preserves_the_sealed_capture_choice(bool replacePolicy)
    {
        using var fixture = await Fixture.CreateAsync();
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Recipient); fixture.Seal(seal);
        var start = StartItem(NativeIngressTargetEvidence.ReadEnvelope(seal));
        await fixture.StartHandler.HandleAsync(start);
        var checkpoint = Assert.Single((await fixture.SelectedQueue.ListAsync(new(start.WorkflowExecutionId))).Items);
        var context = new WorkflowRuntimePipelineContext(checkpoint);
        await fixture.CheckpointHandler.HandleAsync(checkpoint, context);
        var commit = Assert.Single(context.Workspace.PendingCheckpointCommits); var changes = commit.StateChanges;
        var original = changes.WorkflowExecution!;
        var state = original.State with { HistoryCapturePolicy = replacePolicy ? new("substituted", 1, false, ["WorkflowStarted"]) : null,
            HistoryCaptureEnabledAtAdmission = replacePolicy };
        var modified = commit with { StateChanges = new(new(original.StateId, original.Operation, state, original.Metadata),
            changes.Scheduler, changes.ActivityExecutions, changes.Bookmarks, changes.DurableValues, changes.Incidents, changes.Operational,
            changes.ActivityExecutionInspections, changes.PostCommitOutbox, changes.ActivityScopeCleanups) };
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.CheckpointStore.CommitAsync(modified, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Null(await fixture.Executions.FindAsync(start.WorkflowExecutionId)); Assert.Empty(fixture.CheckpointStore.ListCommits());
        Assert.Single((await fixture.SelectedQueue.ListAsync(new(start.WorkflowExecutionId))).Items);
    }

    [Fact]
    public async Task Exact_queue_then_deterministic_checkpoint_successor_then_state_are_distinct_physical_witnesses()
    {
        using var fixture = await Fixture.CreateAsync();
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Recipient); fixture.Seal(seal);
        var start = StartItem(NativeIngressTargetEvidence.ReadEnvelope(seal));
        await fixture.Queue.EnqueueAsync(start);
        var queued = await fixture.Target.ReconcileAsync(fixture.Item, fixture.Recipient, seal);
        Assert.Equal(RuntimeTargetAcceptanceKind.SchedulerCommand, queued!.Kind);
        await fixture.StartHandler.HandleAsync(start);
        await fixture.Queue.DeleteAsync(start.WorkflowExecutionId, start.WorkItemId);
        var checkpoint = Assert.Single((await fixture.Queue.ListAsync(new(start.WorkflowExecutionId))).Items);
        var checkpointPayload = checkpoint.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        Assert.True(payloadOrigin(seal).IsEquivalentTo(checkpointPayload.IngressOrigin));
        Assert.Equal(Now, checkpoint.EnqueuedAt);
        Assert.Equal(RuntimeMetadataKeys.CheckpointRequirementMandatory, checkpoint.CommandMetadata[RuntimeMetadataKeys.CheckpointRequirement]);
        var root = Assert.Single(checkpointPayload.PostCommitIntents).Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        var rootPayload = root.Payload!.Value.Deserialize<RuntimeScheduleActivityCommandPayload>()!;
        Assert.Equal($"{start.WorkflowExecutionId}:activity:root", rootPayload.ActivityExecutionId);
        Assert.False(root.CommandMetadata.ContainsKey(RuntimeMetadataKeys.CheckpointRequirement));
        var successor = await fixture.Target.ReconcileAsync(fixture.Item, fixture.Recipient, seal);
        Assert.Equal(RuntimeTargetAcceptanceKind.InitialCheckpoint, successor!.Kind);
        Assert.Equal(checkpoint.WorkItemId, successor.EvidenceId);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        await fixture.StartHandler.HandleAsync(start);
        Assert.Single((await fixture.Queue.ListAsync(new(start.WorkflowExecutionId))).Items);
        await fixture.CheckpointHandler.HandleAsync(checkpoint);
        await fixture.Queue.DeleteAsync(start.WorkflowExecutionId, checkpoint.WorkItemId);
        var state = await fixture.Target.ReconcileAsync(fixture.Item, fixture.Recipient, seal);
        Assert.Equal(start.WorkflowExecutionId, state!.EvidenceId);

        static RuntimeIngressCommandOrigin payloadOrigin(RuntimeDispatchSeal captured) =>
            NativeIngressTargetEvidence.ReadEnvelope(captured).Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!.StartAuthority!.StandaloneTrigger!;
    }

    [Fact]
    public async Task A_corrupt_initial_checkpoint_with_the_right_origin_cannot_prove_transfer()
    {
        using var fixture = await Fixture.CreateAsync();
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Recipient); fixture.Seal(seal);
        var start = StartItem(NativeIngressTargetEvidence.ReadEnvelope(seal));
        var expected = NativeIngressStartLineage.Build(start, start.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!, "root");
        var json = expected.Payload!.Value.Deserialize<Dictionary<string, JsonElement>>()!;
        json["PostCommitIntents"] = JsonSerializer.SerializeToElement(Array.Empty<RuntimePostCommitIntent>());
        var corrupt = new RuntimeSchedulerWorkItem(expected.WorkItemId, expected.WorkflowExecutionId, expected.CommandId,
            expected.CommandKind, expected.EnvelopeId, expected.IdempotencyKey, expected.EnqueuedAt, expected.RecordedAt,
            expected.Sequence, JsonSerializer.SerializeToElement(json), expected.CommandMetadata, expected.EnvelopeMetadata);
        await fixture.Queue.EnqueueAsync(corrupt);
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Target.ReconcileAsync(fixture.Item, fixture.Recipient, seal).AsTask());
    }

    [Fact]
    public async Task Actor_memo_after_precheckpoint_failure_retries_inside_mailbox_until_physical_witness_exists()
    {
        using var fixture = await Fixture.CreateAsync();
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Recipient);
        var envelope = NativeIngressTargetEvidence.ReadEnvelope(seal);
        var actor = await fixture.Actors.GetAgentAsync(new(envelope.WorkflowExecutionId, WorkflowExecutionActorActivationReason.Start,
            Now, "standalone-trigger", WorkflowExecutionActorCapabilities.None, partition: envelope.Partition));
        fixture.Executor.Fail = true;
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted, (await actor.EnqueueAsync(envelope)).Status);
        fixture.Executor.Fail = false;
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await actor.EnqueueAsync(envelope)).Status);
        Assert.Equal(2, fixture.Executor.Calls);
        fixture.Executor.HasWitness = true;
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, (await actor.EnqueueAsync(envelope)).Status);
        Assert.Equal(2, fixture.Executor.Calls);
    }

    [Fact]
    public async Task Direct_native_state_save_cannot_invent_an_ingress_checkpoint_even_while_an_unrelated_writer_holds_the_gate()
    {
        using var fixture = await Fixture.CreateAsync();
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Recipient); fixture.Seal(seal);
        var origin = NativeIngressTargetEvidence.ReadEnvelope(seal).Command.Payload!.Value
            .Deserialize<WorkflowExecutionStartCommandPayload>()!.StartAuthority!.StandaloneTrigger!;
        var alleged = new WorkflowExecutionState(fixture.Recipient.StartIdentity.WorkflowExecutionId, fixture.Recipient.Material,
            WorkflowExecutionStatus.Running, null, Now, Now, Now, null, null, null, "tenant", new Dictionary<string, string>())
        { IngressOrigin = origin, RunKind = WorkflowRunKind.PublishedRun, Partition = fixture.Recipient.Partition,
            PinnedSource = fixture.Recipient.SourceProvenance, Authority = fixture.Recipient.Authority };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = fixture.Executions.CheckpointState.ExecuteWriteGateAsync(async _ =>
        { entered.SetResult(); await release.Task; return true; }).AsTask();
        await entered.Task;
        var save = Task.Run(async () => await fixture.Executions.SaveAsync(alleged));
        release.SetResult(); await writer;
        await Assert.ThrowsAsync<NotSupportedException>(() => save);
        Assert.Null(await fixture.Executions.FindAsync(alleged.WorkflowExecutionId));
        Assert.Null(await fixture.Target.ReconcileAsync(fixture.Item, fixture.Recipient, seal));
    }

    [Fact]
    public async Task Stripping_a_retained_origin_refuses_before_consuming_claimed_work()
    {
        using var fixture = await Fixture.CreateAsync();
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Recipient); fixture.Seal(seal);
        var start = StartItem(NativeIngressTargetEvidence.ReadEnvelope(seal));
        await fixture.StartHandler.HandleAsync(start);
        var initial = Assert.Single((await fixture.Queue.ListAsync(new(start.WorkflowExecutionId))).Items);
        await fixture.CheckpointHandler.HandleAsync(initial);
        await fixture.Queue.DeleteAsync(initial.WorkflowExecutionId, initial.WorkItemId);
        var prior = (await fixture.Executions.FindAsync(start.WorkflowExecutionId))!;
        Assert.NotNull(prior.IngressOrigin);
        var work = new RuntimeSchedulerWorkItem("continuation", start.WorkflowExecutionId, "continue", WorkflowExecutionCommandKind.RunSchedulerWork,
            "continue-envelope", "continue-key", Now, Now);
        await fixture.Queue.EnqueueAsync(work);
        var claim = (await fixture.Queue.ClaimAsync(new(start.WorkflowExecutionId, "owner", Now, TimeSpan.FromMinutes(1))))!;
        var commit = StateCommit(prior with { IngressOrigin = null }, "stripped", [ConsumedSchedulerWorkItem.FromClaim(claim)]);
        var before = fixture.CheckpointStore.ListCommits().Count;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.CheckpointStore.CommitAsync(commit,
            new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Same(prior, await fixture.Executions.FindAsync(prior.WorkflowExecutionId));
        Assert.Equal(before, fixture.CheckpointStore.ListCommits().Count);
        Assert.Equal(work.WorkItemId, Assert.Single((await fixture.Queue.ListAsync(new(start.WorkflowExecutionId))).Items).WorkItemId);
        Assert.True((await fixture.Queue.ConsumeClaimedAsync(ConsumedSchedulerWorkItem.FromClaim(claim))).Succeeded);
    }

    [Fact]
    public async Task A_mixed_continuation_writer_refuses_before_consuming_claimed_work()
    {
        using var fixture = await Fixture.CreateAsync();
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Recipient); fixture.Seal(seal);
        var start = StartItem(NativeIngressTargetEvidence.ReadEnvelope(seal));
        await fixture.StartHandler.HandleAsync(start);
        var initial = Assert.Single((await fixture.Queue.ListAsync(new(start.WorkflowExecutionId))).Items);
        await fixture.CheckpointHandler.HandleAsync(initial);
        await fixture.Queue.DeleteAsync(initial.WorkflowExecutionId, initial.WorkItemId);
        var prior = (await fixture.Executions.FindAsync(start.WorkflowExecutionId))!;
        var work = new RuntimeSchedulerWorkItem("mixed-continuation", start.WorkflowExecutionId, "continue", WorkflowExecutionCommandKind.RunSchedulerWork,
            "continue-envelope", "continue-key", Now, Now);
        await fixture.Queue.EnqueueAsync(work);
        var claim = (await fixture.Queue.ClaimAsync(new(start.WorkflowExecutionId, "owner", Now, TimeSpan.FromMinutes(1))))!;
        var mixed = new InMemoryRuntimeCheckpointCommitStore(workflowExecutionStateStore: fixture.Executions,
            state: new InMemoryRuntimeCheckpointStoreState(), schedulerWorkQueue: fixture.Queue,
            rootWriteLeaseManager: new WorkflowExecutableRootWriteLeaseManager(fixture.Executables,
                Options.Create(new WorkflowExecutableGarbageCollectionOptions()), fixture.Clock));
        await Assert.ThrowsAsync<NotSupportedException>(() => mixed.CommitAsync(StateCommit(prior, "mixed",
            [ConsumedSchedulerWorkItem.FromClaim(claim)]), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Same(prior, await fixture.Executions.FindAsync(prior.WorkflowExecutionId));
        Assert.Empty(mixed.ListCommits());
        Assert.Equal(work.WorkItemId, Assert.Single((await fixture.Queue.ListAsync(new(start.WorkflowExecutionId))).Items).WorkItemId);
        Assert.True((await fixture.Queue.ConsumeClaimedAsync(ConsumedSchedulerWorkItem.FromClaim(claim))).Succeeded);
    }

    [Fact]
    public async Task A_fresh_nested_model_origin_without_parent_proof_cannot_receive_a_native_writer_permit()
    {
        using var fixture = await Fixture.CreateAsync();
        var id = fixture.Recipient.StartIdentity.WorkflowExecutionId;
        var material = fixture.Recipient.Material;
        var origin = NativeIngressTargetEvidence.Origin(fixture.Item, fixture.Recipient);
        var nested = new RuntimeModelNativeWorkOrigin(1, origin.Scope, "model-parent", new("test.model", 1),
            material, "incarnation", "binding", "invocation", "attempt", material, id,
            "model-commit:worker-intent", "worker-intent", "test.result", "result-input")
        { IngressOrigin = origin, Partition = fixture.Recipient.Partition };
        var state = new WorkflowExecutionState(id, material, WorkflowExecutionStatus.Running, null, Now, Now, Now,
            null, null, null, "tenant", new Dictionary<string, string>())
        { ModelWorkOrigin = nested, RunKind = WorkflowRunKind.PublishedRun, Partition = fixture.Recipient.Partition };
        var commit = StateCommit(state, "unproved-worker");
        Assert.Null(commit.ExpectedModelExecution); Assert.Null(commit.ModelState);
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.CheckpointStore.CommitAsync(commit,
            new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Null(await fixture.Executions.FindAsync(id));
        Assert.Empty(fixture.CheckpointStore.ListCommits());
        Assert.Empty((await fixture.Queue.ListAsync(new(id))).Items);
    }

    [Theory]
    [InlineData("tenant", false, null, false)]
    [InlineData("logical-tenant", false, null, false)]
    [InlineData("tenant", true, null, false)]
    [InlineData("logical-tenant", true, null, false)]
    [InlineData("tenant", false, false, false)]
    [InlineData("logical-tenant", false, false, false)]
    [InlineData("tenant", true, false, false)]
    [InlineData("logical-tenant", true, false, false)]
    [InlineData("tenant", false, true, false)]
    [InlineData("logical-tenant", false, true, false)]
    [InlineData("tenant", true, true, false)]
    [InlineData("logical-tenant", true, true, false)]
    [InlineData("tenant", false, null, true)]
    [InlineData("logical-tenant", true, null, true)]
    public Task Native_saved_cohort_start_retains_scope_after_the_source_claim_is_complete(string tenant, bool coalescing, bool? historyEnabled, bool firstFault) =>
        RunSavedCohort(tenant, coalescing, historyEnabled, firstFault: firstFault);

    [Theory]
    [InlineData("logical-tenant", false, false)] [InlineData("logical-tenant", false, true)]
    [InlineData("logical-tenant", true, false)] [InlineData("logical-tenant", true, true)]
    [InlineData("tenant", false, false)] [InlineData("tenant", false, true)]
    [InlineData("tenant", true, false)] [InlineData("tenant", true, true)]
    public Task Captured_saved_cohort_preserves_real_physical_scope_through_first_retained_and_replayed_commits(string tenant, bool coalescing, bool batch) =>
        RunSavedCohort(tenant, coalescing, true, commitCaptured: true, batch);

    [Theory]
    [InlineData("tenant", false, "none")]
    [InlineData("tenant", true, "none")]
    [InlineData("logical-tenant", false, "none")]
    [InlineData("logical-tenant", true, "none")]
    [InlineData("logical-tenant", false, "unsealed")]
    [InlineData("logical-tenant", true, "unsealed")]
    [InlineData("tenant", false, "foreign-partition")]
    [InlineData("tenant", true, "foreign-partition")]
    public Task Captured_saved_cohort_observation_respects_bound_physical_scope(string tenant, bool coalescing, string nativeHeader) =>
        RunSavedCohort(tenant, coalescing, true, commitCaptured: true, batch: true, observationCheck: nativeHeader);

    private static async Task RunSavedCohort(string tenant, bool coalescing, bool? historyEnabled, bool commitCaptured = false, bool batch = false,
        bool firstFault = false, string? observationCheck = null)
    {
        using var fixture = await Fixture.CreateAsync(tenant, coalescing);
        var parentSeal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Recipient); fixture.Seal(parentSeal);
        var parentStart = StartItem(NativeIngressTargetEvidence.ReadEnvelope(parentSeal));
        using var parentDrain = fixture.BeginCheckpoint(parentStart.WorkflowExecutionId);
        await fixture.StartHandler.HandleAsync(parentStart);
        var parentCheckpoint = Assert.Single((await fixture.SelectedQueue.ListAsync(new(parentStart.WorkflowExecutionId))).Items);
        await fixture.CheckpointHandler.HandleAsync(parentCheckpoint);
        await fixture.Queue.DeleteAsync(parentStart.WorkflowExecutionId, parentCheckpoint.WorkItemId);
        var parent = (await fixture.Executions.FindAsync(parentStart.WorkflowExecutionId))!;
        var scope = fixture.Recipient.StartIdentity.Scope;
        var childPin = new WorkflowExecutableIdentity("child-artifact", "child-definition", "child-version", "1", "child-hash");
        var node = new ExecutableNode("child-root", "child-root", "test", "1", "test", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        await fixture.Executables.SaveAsync(new(childPin, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), Now,
            new Dictionary<string, string>(), new WorkflowExecutableInputContract(1, [new("DefaultCode", new("String"), false, JsonSerializer.SerializeToElement("default"))]), [], IncidentStrategyBuiltIns.FaultReference));
        var intent = new RuntimePostCommitIntent("send", parent.WorkflowExecutionId, "publish", Now, null, "saved-child-key", JsonSerializer.SerializeToElement(new { }));
        const string sourceId = "saved-child-source:send";
        var fingerprint = RuntimeCheckpointCommitFingerprint.ComputeIntent(intent);
        var binding = new WorkflowTriggerBinding("ordinary-binding", childPin.ArtifactId, childPin.DefinitionId, childPin.ArtifactVersion,
            childPin.ArtifactHash, node.ExecutableNodeId, "event", "named", null, new Dictionary<string, string>(), Now);
        var snapshot = new StimulusDeliverySnapshot(1, sourceId, fingerprint, StimulusDeliveryScope.From(parent, scope), Now, "event", "named",
            [new(binding, childPin, (WorkflowExecutableSourceProvenance?)null)], [], StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion);
        // One workflow source owns an independent subscription and an ordinary saved start.
        var independentJson = JsonSerializer.SerializeToElement(fixture.Recipient).Deserialize<Dictionary<string, JsonElement>>()!;
        independentJson[nameof(RuntimeSubscriptionRecipient.StartIdentity)] = JsonSerializer.SerializeToElement(new RuntimeSubscriptionStartIdentity(1, scope, sourceId, fixture.Recipient.SubscriptionId));
        var independent = JsonSerializer.SerializeToElement(independentJson).Deserialize<RuntimeSubscriptionRecipient>()!;
        var combined = new RuntimeSubscriptionDelivery(1, sourceId, fingerprint, scope, [independent], [],
            [new(independent.RecipientId, RuntimeRecipientPhase.Pending)], resourceCaptures:
            [new("trigger", 1, 1, 1, [independent.SubscriptionId], JsonSerializer.SerializeToElement(new { }))]);
        // Physical accepted-cohort fixture; seal through the real source port before ending its claim.
        fixture.Executions.CheckpointState.OutboxItems[sourceId] = new(sourceId, intent, RuntimePostCommitOutboxStatus.Delivering,
            Now, Now, null, 1, "previous-owner", Now, null, null, null, 1, Now.AddMinutes(5), 1, scope, snapshot, null, 1, combined);
        fixture.CheckpointStore.StimulusAdapters = [new SavedEventAdapter()];
        fixture.CheckpointStore.StimulusInputValidator = new WorkflowExecutableInputValidator(new StringTypes());
        var key = KeyedWorkflowStartIdentity.For("saved-child-key", childPin.ArtifactId);
        var savedOrigin = new RuntimeStimulusDeliveryOrigin(1, scope, sourceId, fingerprint, binding.TriggerBindingId);
        var payload = new WorkflowExecutionStartCommandPayload(childPin, childPin.ArtifactId,
            new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement> { ["DefaultCode"] = JsonSerializer.SerializeToElement("default") }, intent.Payload, node.ExecutableNodeId,
            WorkflowRunKind.PublishedRun, null, null, null, tenant, fixture.Recipient.Partition, new("publish", "publish"),
            new WorkflowExecutableStartAuthority(WorkflowExecutableStartAuthorityKind.StimulusDelivery, null,
                new WorkflowExecutableStimulusDeliveryAuthority(sourceId, fingerprint, scope, "previous-owner", 1, binding.TriggerBindingId)), 0)
            {
                StimulusDeliveryOrigin = savedOrigin,
                HistoryCapturePolicy = historyEnabled == true ? new("saved-start-history", 1, !commitCaptured || batch,
                    observationCheck is null ? ["WorkflowStarted"] : ["WorkflowStarted", "DeliveryReceived"]) : null,
                HistoryCaptureEnabledAtAdmission = historyEnabled
            };
        var authority = payload.StartAuthority!.StimulusDelivery!;
        var initialMetadata = new Dictionary<string, string> { [SavedStimulusStartSeal.OriginMetadata] = JsonSerializer.Serialize(savedOrigin) };
        var proposed = new WorkflowExecutionCommandEnvelope(key.EnvelopeId, key.WorkflowExecutionId,
            new(key.CommandId, key.WorkflowExecutionId, WorkflowExecutionCommandKind.Start, Now, JsonSerializer.SerializeToElement(payload), initialMetadata),
            key.StartKey, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, Now, metadata: initialMetadata, partition: fixture.Recipient.Partition);
        foreach (var changed in new Dictionary<string, JsonElement>
        {
            [nameof(WorkflowExecutionStartCommandPayload.Authority)] = JsonSerializer.SerializeToElement(new WorkflowExecutionAuthoritySnapshot("other-system", "other-initiator")),
            [nameof(WorkflowExecutionStartCommandPayload.Variables)] = JsonSerializer.SerializeToElement(new { forged = "variable" }),
            [nameof(WorkflowExecutionStartCommandPayload.Inputs)] = JsonSerializer.SerializeToElement(new { forged = "input" }),
            [nameof(WorkflowExecutionStartCommandPayload.StimulusInput)] = JsonSerializer.SerializeToElement(new { forged = "stimulus" })
        })
        {
            var changedPayload = proposed.Command.Payload!.Value.Deserialize<Dictionary<string, JsonElement>>()!;
            changedPayload[changed.Key] = changed.Value;
            var tampered = new WorkflowExecutionCommandEnvelope(proposed.EnvelopeId, proposed.WorkflowExecutionId,
                proposed.Command with { Payload = JsonSerializer.SerializeToElement(changedPayload) }, proposed.IdempotencyKey,
                proposed.DeliveryMode, proposed.EnqueuedAt, metadata: proposed.Metadata, partition: proposed.Partition);
            var refused = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => fixture.CheckpointStore.SealSavedStartAsync(authority, tampered).AsTask());
            Assert.IsType<InvalidDataException>(refused.InnerException);
            Assert.Null(fixture.Executions.CheckpointState.OutboxItems[sourceId].SavedStimulusStarts);
        }
        var sealedStart = await fixture.CheckpointStore.SealSavedStartAsync(authority, proposed);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var later = new WorkflowExecutionCommandEnvelope(proposed.EnvelopeId, proposed.WorkflowExecutionId,
            proposed.Command with { EnqueuedAt = Now.AddSeconds(1) }, proposed.IdempotencyKey, proposed.DeliveryMode,
            Now.AddSeconds(1), metadata: proposed.Metadata, partition: proposed.Partition);
        Assert.Equal(sealedStart.EnqueuedAt, (await fixture.CheckpointStore.SealSavedStartAsync(authority, later)).EnqueuedAt);
        var physical = fixture.Executions.CheckpointState.OutboxItems[sourceId];
        var sourceClaim = new RuntimePostCommitOutboxClaim(physical, "previous-owner", 1, Now, Now.AddMinutes(5));
        await fixture.CheckpointStore.RecordDeliveryResultAsync(sourceClaim, new(sourceId, RuntimePostCommitOutboxStatus.FailedFinal, Now, "Independent recipient needs operator retry."));
        var beforeRetry = fixture.Executions.CheckpointState.OutboxItems[sourceId];
        var request = new RuntimeIngressControlRequest(new(scope, tenant), sourceId, fingerprint, combined.SelectionFingerprint,
            "trigger", "operator", "retry-request", "request-fingerprint", RuntimeIngressControlKind.RetryRecipients,
            [new(independent.RecipientId, RuntimeIngressInspection.ProgressFingerprint(combined.Progress.Single()))]);
        var retried = RuntimeIngressControlRules.Apply(beforeRetry, request, fixture.Clock.GetUtcNow());
        Assert.Equal(RuntimeIngressControlOutcome.RetryScheduled, Assert.Single(retried.Receipt.Recipients).Outcome);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, retried.Item.Status);
        Assert.NotNull(retried.Item.SavedStimulusStarts);
        Assert.True(JsonElement.DeepEquals(beforeRetry.SavedStimulusStarts![key.WorkflowExecutionId], retried.Item.SavedStimulusStarts![key.WorkflowExecutionId]));
        fixture.Executions.CheckpointState.OutboxItems[sourceId] = retried.Item;
        if (firstFault)
        {
            await AssertProtectedFirstFaultAsync(fixture, sealedStart, false);
            return;
        }
        var start = StartItem(sealedStart);
        using var childDrain = fixture.BeginCheckpoint(key.WorkflowExecutionId);
        await fixture.StartHandler.HandleAsync(start);
        var checkpoint = Assert.Single((await fixture.SelectedQueue.ListAsync(new(key.WorkflowExecutionId))).Items);
        var handed = checkpoint.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        Assert.Equal(historyEnabled, handed.HistoryCaptureEnabledAtAdmission);
        Assert.Equal(payload.HistoryCapturePolicy is null, handed.HistoryCapturePolicy is null);
        var physicalContext = new WorkflowRuntimePipelineContext(checkpoint);
        await fixture.CheckpointHandler.HandleAsync(checkpoint, physicalContext);
        var originalCommit = Assert.Single(physicalContext.Workspace.PendingCheckpointCommits);
        var originalChange = originalCommit.StateChanges.WorkflowExecution!;
        var originalState = originalChange.State;
        var substitutions = new[]
        {
            originalState with { HistoryCapturePolicy = historyEnabled == true ? null : new("substituted", 1, false, ["WorkflowStarted"]),
                HistoryCaptureEnabledAtAdmission = historyEnabled != true },
            originalState with { HistoryCaptureEnabledAtAdmission = historyEnabled is null ? false : null }
        };
        foreach (var substitution in substitutions)
        {
            var changes = originalCommit.StateChanges;
            var modified = originalCommit with { StateChanges = new(new(originalChange.StateId, originalChange.Operation, substitution, originalChange.Metadata),
                changes.Scheduler, changes.ActivityExecutions, changes.Bookmarks, changes.DurableValues, changes.Incidents, changes.Operational,
                changes.ActivityExecutionInspections, changes.PostCommitOutbox, changes.ActivityScopeCleanups) };
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.CheckpointStore.CommitAsync(modified, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
            Assert.Null(await fixture.Executions.FindAsync(key.WorkflowExecutionId));
            Assert.DoesNotContain(fixture.CheckpointStore.ListCommits(), item => item.Commit.WorkflowExecutionId == key.WorkflowExecutionId);
            Assert.Single((await fixture.SelectedQueue.ListAsync(new(key.WorkflowExecutionId))).Items);
            Assert.True(JsonElement.DeepEquals(sealedStart.Command.Payload!.Value,
                SavedStimulusStartSeal.Read(fixture.Executions.CheckpointState.OutboxItems[sourceId], key.WorkflowExecutionId).Command.Payload!.Value));
        }
        if (payload.HistoryCapturePolicy is { } required)
        {
            Assert.True(required.IsEquivalentTo(handed.HistoryCapturePolicy));
            var context = new WorkflowRuntimePipelineContext(checkpoint);
            await fixture.CheckpointHandler.HandleAsync(checkpoint, context);
            var staged = Assert.Single(context.Workspace.PendingCheckpointCommits);
            Assert.True(required.IsEquivalentTo(staged.StateChanges.WorkflowExecution!.State.HistoryCapturePolicy));
            Assert.True(staged.StateChanges.WorkflowExecution.State.HistoryCaptureEnabledAtAdmission);
            if (commitCaptured)
            {
                var historyAccess = new Access(scope);
                var reader = new Elsa.Workflows.Runtime.Services.History.InMemoryRuntimeHistoryStore(fixture.Executions.CheckpointState,
                    historyAccess, new Elsa.Workflows.Runtime.Services.Recovery.HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
                    { SigningKey = "saved-history-scope-control-signing-key-32-bytes" })));
                RuntimeCheckpointCommit WithHistory(RuntimeCheckpointCommit value, bool starts) => batch ? value with
                {
                    HistoryBatches = [new(required, value.Checkpoint.CheckpointId, value.Checkpoint.OccurredAt, starts,
                        starts ? [new(new RuntimeHistoryEvent("event-" + value.CommitId, key.WorkflowExecutionId, 0,
                            RuntimeHistoryEventCategory.WorkflowLifecycle, "WorkflowStarted", value.Checkpoint.OccurredAt,
                            checkpointId: value.Checkpoint.CheckpointId) { Authority = RuntimeHistoryAuthority.CommittedTransition }, 0)] : [])]
                } : value;
                var first = WithHistory(staged, true);
                await fixture.CheckpointStore.CommitAsync(first, new(RuntimeCheckpointPersistenceMode.Immediate));
                var initialHead = await reader.FindHeadAsync(key.WorkflowExecutionId); Assert.NotNull(initialHead);
                Assert.Equal(batch ? 1 : 0, initialHead.LastSequence); Assert.Equal(!batch, initialHead.HasGaps);
                await fixture.CheckpointStore.CommitAsync(first, new(RuntimeCheckpointPersistenceMode.Immediate));
                Assert.Equal(initialHead, await reader.FindHeadAsync(key.WorkflowExecutionId));
                var capturedNative = (await fixture.Executions.FindAsync(key.WorkflowExecutionId))!;
                Assert.Equal(tenant, capturedNative.TenantId); Assert.Equal(scope.Value, capturedNative.Partition!.Value);
                Assert.True(savedOrigin.IsEquivalentTo(capturedNative.StimulusDeliveryOrigin));
                var retained = WithHistory(StateCommit(capturedNative with { UpdatedAt = Now.AddSeconds(1) }, "captured-continuation", []), false);
                await fixture.CheckpointStore.CommitAsync(retained, new(RuntimeCheckpointPersistenceMode.Immediate));
                var retainedHead = await reader.FindHeadAsync(key.WorkflowExecutionId);
                await fixture.CheckpointStore.CommitAsync(retained, new(RuntimeCheckpointPersistenceMode.Immediate));
                Assert.Equal(retainedHead, await reader.FindHeadAsync(key.WorkflowExecutionId));
                var foreign = new InMemoryRuntimeCheckpointCommitStore(workflowExecutionStateStore: fixture.Executions,
                    state: fixture.Executions.CheckpointState, persistenceAccessContextAccessor: new Access(new("other-scope")));
                await Assert.ThrowsAsync<InvalidOperationException>(() => foreign.CommitAsync(first,
                    new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
                Assert.Equal(retainedHead, await reader.FindHeadAsync(key.WorkflowExecutionId));
                if (observationCheck is not null)
                {
                    // Start and seal through the real native source before exercising the independent observation port.
                    // A mismatched read adapter supplies ordinary, unsealed headers for negative controls; the accepted
                    // native owner's continuation guard and retained state stay intact.
                    var owner = (await fixture.Executions.FindAsync(key.WorkflowExecutionId))!;
                    var observationExecutions = fixture.Executions;
                    if (observationCheck != "none")
                    {
                        observationExecutions = new InMemoryWorkflowExecutionStateStore(fixture.Executions.CheckpointState, null);
                        await observationExecutions.SaveAsync(owner with
                        {
                            StimulusDeliveryOrigin = null,
                            Partition = observationCheck == "foreign-partition" ? new("other-scope") : owner.Partition
                        });
                    }
                    var writer = new InMemoryRuntimeHistoryObservationWriter(fixture.Executions.CheckpointState,
                        historyAccess, observationExecutions, fixture.Clock);
                    Assert.True(writer.CanRecordHistory(reader));
                    var observation = new RuntimeHistoryObservation(required,
                        new RuntimeHistoryEvent("saved-scope-observation", key.WorkflowExecutionId, 0,
                            RuntimeHistoryEventCategory.DeliveryObservation, "DeliveryReceived", fixture.Clock.GetUtcNow())
                        { Authority = RuntimeHistoryAuthority.DeliveryObservation, Causation = new(DeliveryId: "saved-scope-delivery") });
                    var beforeObservation = (await reader.FindHeadAsync(key.WorkflowExecutionId))!;
                    var markerCount = fixture.CheckpointStore.ListCommits().Count;
                    if (observationCheck == "none")
                    {
                        var result = await writer.RecordAsync(observation);
                        Assert.True(result.Recorded);
                        Assert.Equal(beforeObservation.LastSequence + 1, result.Event!.Sequence);
                        Assert.Equal(result.Event, (await writer.RecordAsync(observation)).Event);
                        var afterObservation = (await reader.FindHeadAsync(key.WorkflowExecutionId))!;
                        Assert.Equal(result.Event.Sequence, afterObservation.LastSequence);
                        Assert.Equal(beforeObservation.LastPhysicalCommitId, afterObservation.LastPhysicalCommitId);
                        Assert.Equal(beforeObservation.LastCommittedAt, afterObservation.LastCommittedAt);
                    }
                    else
                    {
                        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await writer.RecordAsync(observation));
                        Assert.Equal(beforeObservation, await reader.FindHeadAsync(key.WorkflowExecutionId));
                        Assert.Null(await reader.FindEventAsync(key.WorkflowExecutionId, observation.Event.HistoryEventId));
                    }
                    Assert.Equal(markerCount, fixture.CheckpointStore.ListCommits().Count);
                    Assert.Equal(owner, await fixture.Executions.FindAsync(key.WorkflowExecutionId));
                }
                return;
            }
            var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), fixture.CheckpointStore,
                new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []);
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => committer.CommitAsync(staged).AsTask());
            Assert.Null(await fixture.Executions.FindAsync(key.WorkflowExecutionId));
            Assert.Single((await fixture.SelectedQueue.ListAsync(new(key.WorkflowExecutionId))).Items);
            return;
        }
        if (coalescing)
        {
            Assert.Empty((await fixture.Queue.ListAsync(new(key.WorkflowExecutionId))).Items);
            var pending = new RuntimeCheckpointCommit("prior-overlay", new("prior-overlay", "EarlierDeferred", key.WorkflowExecutionId, Now, [], new Dictionary<string, string>()),
                new(workflowExecution: null, scheduler: null, activityExecutions: [], bookmarks: [], durableValues: [], incidents: [], operational: []),
                [], new Dictionary<string, string>());
            fixture.CurrentSession.BufferDeferred(pending);
            await Assert.ThrowsAsync<NotSupportedException>(() => fixture.CheckpointHandler.HandleAsync(checkpoint).AsTask());
            Assert.Null(await fixture.Executions.FindAsync(key.WorkflowExecutionId));
            Assert.True(fixture.CurrentSession.HasBufferedChanges);
            fixture.CurrentSession.ClearBuffer();
        }
        foreach (var changed in new Dictionary<string, JsonElement>
        {
            [nameof(RuntimeCheckpointCommandPayload.CheckpointName)] = JsonSerializer.SerializeToElement("OtherCheckpoint"),
            [nameof(RuntimeCheckpointCommandPayload.StimulusDeliveryOrigin)] = JsonSerializer.SerializeToElement<object?>(null),
            [nameof(RuntimeCheckpointCommandPayload.PostCommitIntents)] = TamperedRoot(checkpoint),
            [nameof(RuntimeCheckpointCommandPayload.Authority)] = JsonSerializer.SerializeToElement(new WorkflowExecutionAuthoritySnapshot("different-system", "different-initiator")),
            [nameof(RuntimeCheckpointCommandPayload.SeedInputs)] = JsonSerializer.SerializeToElement(new { forged = "input" }),
            [nameof(RuntimeCheckpointCommandPayload.SeedStimulusInput)] = JsonSerializer.SerializeToElement(new { forged = "stimulus" }),
            [nameof(RuntimeCheckpointCommandPayload.SeedTriggerMetadata)] = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["forged"] = "metadata" })
        })
        {
            var changedPayload = checkpoint.Payload!.Value.Deserialize<Dictionary<string, JsonElement>>()!;
            changedPayload[changed.Key] = changed.Value;
            var changedWork = new RuntimeSchedulerWorkItem(checkpoint.WorkItemId, checkpoint.WorkflowExecutionId, checkpoint.CommandId,
                checkpoint.CommandKind, checkpoint.EnvelopeId, checkpoint.IdempotencyKey, checkpoint.EnqueuedAt, checkpoint.RecordedAt,
                checkpoint.Sequence, JsonSerializer.SerializeToElement(changedPayload), checkpoint.CommandMetadata, checkpoint.EnvelopeMetadata);
            var refusal = await Record.ExceptionAsync(() => fixture.CheckpointHandler.HandleAsync(changedWork).AsTask());
            if (changed.Key is nameof(RuntimeCheckpointCommandPayload.CheckpointName) or nameof(RuntimeCheckpointCommandPayload.StimulusDeliveryOrigin))
            {
                var domain = Assert.IsType<RuntimeCheckpointCommitValidationException>(refusal);
                Assert.IsType<InvalidDataException>(domain.InnerException);
                Assert.Contains(changedWork.WorkflowExecutionId, domain.Message);
            }
            else Assert.IsType<InvalidDataException>(refusal);
            Assert.Null(await fixture.Executions.FindAsync(key.WorkflowExecutionId));
        }
        var forgedJson = checkpoint.Payload!.Value.Deserialize<Dictionary<string, JsonElement>>()!;
        forgedJson[nameof(RuntimeCheckpointCommandPayload.StimulusDeliveryOrigin)] = JsonSerializer.SerializeToElement(new RuntimeStimulusDeliveryOrigin(
            1, scope, sourceId, fingerprint, "not-in-cohort"));
        var forged = new RuntimeSchedulerWorkItem(checkpoint.WorkItemId, checkpoint.WorkflowExecutionId, checkpoint.CommandId,
            checkpoint.CommandKind, checkpoint.EnvelopeId, checkpoint.IdempotencyKey, checkpoint.EnqueuedAt, checkpoint.RecordedAt,
            checkpoint.Sequence, JsonSerializer.SerializeToElement(forgedJson), checkpoint.CommandMetadata, checkpoint.EnvelopeMetadata);
        var forgedRefusal = await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() => fixture.CheckpointHandler.HandleAsync(forged).AsTask());
        Assert.IsType<InvalidDataException>(forgedRefusal.InnerException);
        Assert.Null(await fixture.Executions.FindAsync(key.WorkflowExecutionId));
        Assert.Equal(checkpoint.WorkItemId, Assert.Single((await fixture.SelectedQueue.ListAsync(new(key.WorkflowExecutionId))).Items).WorkItemId);
        await fixture.CheckpointHandler.HandleAsync(checkpoint);
        await fixture.Queue.DeleteAsync(key.WorkflowExecutionId, checkpoint.WorkItemId);
        var admittedCommit = Assert.Single(fixture.CheckpointStore.ListCommits(), item => item.Commit.WorkflowExecutionId == key.WorkflowExecutionId).Commit;
        Assert.Equal(JsonSerializer.Serialize(savedOrigin), admittedCommit.Metadata[SavedStimulusStartSeal.OriginMetadata]);
        var rootWork = admittedCommit.PostCommitIntents.Single().Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.False(rootWork.CommandMetadata.ContainsKey(SavedStimulusStartSeal.OriginMetadata));
        Assert.False(rootWork.EnvelopeMetadata.ContainsKey(SavedStimulusStartSeal.OriginMetadata));
        var child = (await fixture.Executions.FindAsync(key.WorkflowExecutionId))!;
        Assert.Equal(historyEnabled, child.HistoryCaptureEnabledAtAdmission); Assert.Null(child.HistoryCapturePolicy);
        Assert.Equal(tenant, child.TenantId); Assert.Null(child.IngressOrigin); Assert.True(savedOrigin.IsEquivalentTo(child.StimulusDeliveryOrigin));
        await fixture.CheckpointStore.CommitAsync(StateCommit(child with { UpdatedAt = Now.AddSeconds(1) }, "saved-continuation", []),
            new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.True(savedOrigin.IsEquivalentTo((await fixture.Executions.FindAsync(key.WorkflowExecutionId))!.StimulusDeliveryOrigin));
    }

    private static RuntimeCheckpointCommit StateCommit(WorkflowExecutionState state, string id,
        IReadOnlyCollection<ConsumedSchedulerWorkItem>? consumed = null) => new(id,
        new("checkpoint:" + id, consumed is null ? RuntimeCheckpointNames.WorkflowStarted : RuntimeCheckpointNames.PostCommitIntentRecorded, state.WorkflowExecutionId, Now, [], new Dictionary<string, string>()),
        new(new(state.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>()),
            null, [], [], [], [], [], null, null, null, null, null, consumed), [], new Dictionary<string, string>());

    private static async Task<RuntimeCheckpointCommit> AssertProtectedFirstFaultAsync(Fixture fixture, WorkflowExecutionCommandEnvelope envelope, bool failStart)
    {
        var id = envelope.WorkflowExecutionId;
        var ownership = Assert.IsType<InMemoryExecutionLivenessStateStore>(fixture.Executions.ExecutionLivenessStateStore);
        var poison = new InMemoryWorkflowSchedulerPoisonStore(ownership, fixture.Clock);
        fixture.CheckpointStore.InitialStartFaultPoisonStore = poison;
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        var lease = await new RuntimeExecutionOwnershipService(ownership, fixture.Clock, new()).AcquireAsync(id);
        var ambient = new AsyncLocalRuntimeExecutionOwnershipContextAccessor();
        using var owned = ambient.Push(lease);
        var start = StartItem(envelope);
        var initial = NativeIngressStartLineage.Build(start, envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!,
            (await fixture.Executables.FindAsync(envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!.PinnedExecutable.ArtifactId))!.RootActivity.ExecutableNodeId);
        var work = failStart ? start : initial;
        // A physical parked-decision fixture follows the same work ids/kinds as the real start/checkpoint handlers.
        var record = new RuntimeSchedulerPoisonRecord(id, work.WorkItemId, work.CommandKind,
            failStart ? WorkflowStartSchedulerWorkHandler.HandlerName : WorkflowCheckpointSchedulerWorkHandler.HandlerName,
            new RuntimeFaultInfo("System.InvalidOperationException", "initial checkpoint failed"), 1,
            RuntimeSchedulerPoisonDisposition.Poisoned, fixture.Clock.GetUtcNow(), fixture.Clock.GetUtcNow(), null,
            new Dictionary<string, string> { [InitialStartFaultAuthority.WorkSnapshotMetadata] = JsonSerializer.Serialize(work) });
        await poison.RecordFencedAsync(record, lease.ToFence());
        var capture = new FaultCaptureStore();
        var observer = new PoisonedSchedulerWorkIncidentObserver(poison, fixture.Incidents,
            new(new CoalescingRuntimeCheckpointPersistencePolicy(), capture, ambient, [], []), fixture.Clock,
            activityExecutionStateStore: null, inspectionAccumulator: null, activityExecutionInspectionStore: null,
            workflowExecutionStateStore: fixture.Executions);
        var drain = new RuntimeSchedulerDrainResult(id, fixture.Clock.GetUtcNow(), fixture.Clock.GetUtcNow(),
            [new RuntimeSchedulerWorkItemResult(work.WorkItemId, id, work.CommandKind, RuntimeSchedulerWorkItemResultStatus.Faulted,
                record.HandlerName, fixture.Clock.GetUtcNow(), fixture.Clock.GetUtcNow(), error: "initial checkpoint failed")]);
        await observer.OnDrainedAsync(envelope, drain);
        var candidate = Assert.IsType<RuntimeCheckpointCommit>(capture.Commit);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, capture.Decision!.Mode);
        Assert.NotNull(candidate.ExpectedInitialStartFault);
        var unsupported = new UnsupportedFaultStore();
        await Assert.ThrowsAsync<NotSupportedException>(() => new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(),
            new CoalescingRuntimeCheckpointCommitStore(new(unsupported), new AsyncLocalRuntimeCoalescingSessionAccessor()), ambient, [], [])
            .CommitAsync(candidate).AsTask());
        Assert.False(unsupported.Called);
        var changes = candidate.StateChanges;
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var unwanted = new RuntimePostCommitIntent("unrelated", id, "publish", fixture.Clock.GetUtcNow(), null, "unrelated", JsonSerializer.SerializeToElement("bad"));
        var extra = RuntimePostCommitOutboxItems.CreatePendingChanges(candidate with { PostCommitIntents = [unwanted] });
        foreach (var refused in new[]
        {
            candidate with { ExpectedInitialStartFault = new(work, new(id, work.WorkItemId, work.CommandKind, record.HandlerName,
                record.Fault, 2, record.Disposition, record.FirstFailedAt, record.LastFailedAt, null)) },
            candidate with { StateChanges = new(null, null, [], [], [], [], []) },
            candidate with { StateChanges = new(changes.WorkflowExecution! with { Operation = RuntimeStateChangeOperation.Delete }, null, [], [], [], changes.Incidents, []) },
            candidate with { StateChanges = changes.WithPostCommitOutbox(extra) },
            candidate with { PostCommitIntents = [unwanted], StateChanges = changes.WithPostCommitOutbox(extra) },
            candidate with { ExpectedFence = null }
        })
        {
            var error = await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() => fixture.CheckpointStore.CommitAsync(refused, decision).AsTask());
            Assert.IsType<InvalidDataException>(error.InnerException);
            Assert.Contains(id, error.Message); Assert.Contains(work.WorkItemId, error.Message);
            Assert.Null(await fixture.Executions.FindAsync(id)); Assert.Empty(await fixture.Incidents.ListAsync(id));
            Assert.DoesNotContain(fixture.CheckpointStore.ListCommits(), item => item.Commit.WorkflowExecutionId == id);
        }
        await fixture.CheckpointStore.CommitAsync(candidate, decision);
        await fixture.CheckpointStore.CommitAsync(candidate, decision); // replay after the first fault is durable
        var state = (await fixture.Executions.FindAsync(id))!;
        Assert.Equal(WorkflowExecutionStatus.Faulted, state.Status);
        Assert.Equal(envelope.EnqueuedAt, state.CreatedAt); Assert.Equal(envelope.EnqueuedAt, state.StartedAt);
        var payload = envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.True(NativeIngressTargetEvidence.Equal(payload.StartAuthority?.StandaloneTrigger, state.IngressOrigin));
        Assert.True(NativeIngressTargetEvidence.Equal(payload.StimulusDeliveryOrigin, state.StimulusDeliveryOrigin));
        Assert.Equal(payload.PinnedSource, state.PinnedSource);
        Assert.Single(await fixture.Incidents.ListAsync(id));
        Assert.Empty((await fixture.Queue.ListAsync(new(id))).Items);
        Assert.DoesNotContain(fixture.Executions.CheckpointState.OutboxItems.Values,
            item => item.Intent?.WorkflowExecutionId == id && item.Intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork);
        return candidate;
    }

    private sealed class UnsupportedFaultStore : IRuntimeCheckpointCommitStore
    {
        internal bool Called;
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        { Called = true; return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([])); }
    }

    private sealed class FaultCaptureStore : IRuntimeCheckpointCommitStore
    {
        public bool SupportsInitialStartFaultAdmission => true;
        internal RuntimeCheckpointCommit? Commit;
        internal RuntimeCheckpointPersistenceDecision? Decision;
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        { Commit = commit; Decision = decision; return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([])); }
    }

    private static RuntimeSchedulerWorkItem StartItem(WorkflowExecutionCommandEnvelope envelope) => new(envelope.EnvelopeId,
        envelope.WorkflowExecutionId, envelope.Command.CommandId, envelope.Command.Kind, envelope.EnvelopeId,
        envelope.IdempotencyKey, envelope.EnqueuedAt, envelope.EnqueuedAt.AddSeconds(1), envelope.Sequence,
        envelope.Command.Payload, envelope.Command.Metadata, envelope.Metadata);

    private sealed class Fixture : IDisposable
    {
        internal readonly FakeTimeProvider Clock = new(Now);
        internal readonly InMemoryWorkflowExecutableStore Executables = new();
        internal readonly InMemoryWorkflowSchedulerWorkQueue Queue = new();
        internal IWorkflowSchedulerWorkQueue SelectedQueue = null!;
        internal readonly InMemoryWorkflowExecutionStateStore Executions = new();
        internal readonly InMemoryIncidentStateStore Incidents = new();
        internal readonly WitnessExecutor Executor = new();
        internal readonly FakeIngress Ingress = new();
        internal readonly InProcessWorkflowExecutionActorProvider Actors;
        internal RuntimePostCommitOutboxItem Item = null!;
        internal RuntimeSubscriptionRecipient Recipient = null!;
        internal NativeStandaloneTriggerRuntimeTarget Target = null!;
        internal WorkflowStartSchedulerWorkHandler StartHandler = null!;
        internal WorkflowCheckpointSchedulerWorkHandler CheckpointHandler = null!;
        internal InMemoryRuntimeCheckpointCommitStore CheckpointStore = null!;
        private readonly AsyncLocalRuntimeCoalescingSessionAccessor coalescingAccessor = new();
        internal RuntimeCoalescingSession CurrentSession => coalescingAccessor.Current!;
        internal IDisposable BeginCheckpoint(string executionId) => coalescingAccessor.Push(new RuntimeCoalescingSession(executionId, Queue, new PersistenceModeOptions()));
        private Fixture() => Actors = new(Executor);
        internal static async Task<Fixture> CreateAsync(string tenant = "tenant", bool coalescing = false, string inputValue = "business-value")
        {
            var fixture = new Fixture();
            var contract = new WorkflowExecutableInputContract(1, [new("AccountId", new("String"), true)]);
            var identity = new WorkflowExecutableIdentity("artifact", "producer-definition", "producer-version", "1", "hash");
            var node = new ExecutableNode("root", "authored-root", "test", "1", "test", JsonSerializer.SerializeToElement(new { }),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
            await fixture.Executables.SaveAsync(new(identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), Now,
                new Dictionary<string, string>(), contract, [], IncidentStrategyBuiltIns.FaultReference));
            var scope = new PersistenceScope("tenant"); var partition = new WorkflowExecutionPartition("tenant");
            var schema = new ValueTypeDescriptor("Test.Payload", schemaVersion: 1);
            var payload = JsonSerializer.SerializeToElement(new { payload = new { account = inputValue } });
            var authority = new WorkflowExecutionAuthoritySnapshot("standalone-trigger", "source-actor");
            var fingerprint = RuntimeStandaloneIngressSource.ComputeRequestFingerprint(scope, tenant, "trigger", "test", "occurrence", schema, payload, authority);
            var source = new RuntimeStandaloneIngressSource(1, "admission", "dispatch", Now, scope, tenant, partition,
                "trigger", 1, 1, "test", "occurrence", fingerprint, schema, payload, RuntimeIngressAdmissionPolicy.RequireAll, authority);
            fixture.Recipient = new("recipient", "subscription", 1, 1, "definition", "native", "slot", 1, "activation", identity,
                new("reference", "Published", "publication", "1", "definition", "version", "1", "activation", "slot"),
                RuntimeIngressAdmission.ComputeInputContractFingerprint(contract), 1, 1,
                new Dictionary<string, JsonElement> { ["AccountId"] = JsonSerializer.SerializeToElement(inputValue) }, authority,
                tenant, partition, new(1, scope, source.OutboxItemId, "subscription"));
            var delivery = new RuntimeSubscriptionDelivery(1, source.OutboxItemId, source.Fingerprint, scope, [fixture.Recipient], [],
                [new(fixture.Recipient.RecipientId, RuntimeRecipientPhase.Pending)]);
            fixture.Item = RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, delivery, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));
            var evidence = new NativeIngressTargetEvidence(fixture.Queue, fixture.Executions, fixture.Clock, executables: fixture.Executables);
            fixture.Target = new(fixture.Executables, fixture.Actors, fixture.Ingress, new WorkflowExecutableInputValidator(new StringTypes()),
                new AllowWorkflowExecutableStartPolicy(), evidence, new Access(scope));
            fixture.SelectedQueue = coalescing ? new CoalescingWorkflowSchedulerWorkQueue(new(fixture.Queue), fixture.coalescingAccessor) : fixture.Queue;
            fixture.StartHandler = new(fixture.Executables, fixture.SelectedQueue, new RuntimeExecutionIdGenerator(fixture.Clock), fixture.Clock,
                fixture.Executions, null, null, null, null, fixture.Ingress);
            var activities = new InMemoryActivityExecutionStateStore();
            var checkpointStore = fixture.CheckpointStore = new InMemoryRuntimeCheckpointCommitStore(fixture.Executables,
                new InMemoryWorkflowExecutableSourceReferenceStore(fixture.Executables, fixture.Clock), fixture.Executions, activities,
                incidentStateStore: fixture.Incidents, durableValueStateStore: new InMemoryDurableValueStateStore(), operationalStateStore: fixture.Executions.ExecutionLivenessStateStore,
                state: fixture.Executions.CheckpointState, timeProvider: fixture.Clock, persistenceAccessContextAccessor: new Access(scope),
                schedulerWorkQueue: fixture.Queue,
                rootWriteLeaseManager: new WorkflowExecutableRootWriteLeaseManager(fixture.Executables,
                    Options.Create(new WorkflowExecutableGarbageCollectionOptions()), fixture.Clock));
            IRuntimeCheckpointCommitStore selectedStore = coalescing
                ? new CoalescingRuntimeCheckpointCommitStore(new(checkpointStore), fixture.coalescingAccessor) : checkpointStore;
            IRuntimeCheckpointPersistencePolicy policy = coalescing
                ? new CoalescingRuntimeCheckpointPersistencePolicy() : new ImmediateRuntimeCheckpointPersistencePolicy();
            var committer = new RuntimeCheckpointCommitter(policy, selectedStore,
                new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []);
            fixture.CheckpointHandler = new(activities, committer, null, fixture.Clock, fixture.Executions, fixture.Executables,
                null, null, fixture.Ingress);
            return fixture;
        }
        internal void Seal(RuntimeDispatchSeal seal)
        {
            Item = Item.WithSubscriptionDelivery(Item.SubscriptionDelivery!.WithProgress(
                new(Recipient.RecipientId, RuntimeRecipientPhase.DispatchCommitted, seal)));
            Ingress.Current = new(Item, Recipient, seal);
            Executions.CheckpointState.OutboxItems[Item.OutboxItemId] = Item;
        }
        public void Dispose() => Actors.Dispose();
    }
    private static JsonElement TamperedRoot(RuntimeSchedulerWorkItem checkpoint)
    {
        var intent = checkpoint.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!.PostCommitIntents.Single();
        var json = JsonSerializer.SerializeToElement(intent).Deserialize<Dictionary<string, JsonElement>>()!;
        json[nameof(RuntimePostCommitIntent.IntentId)] = JsonSerializer.SerializeToElement(intent.IntentId + ":forged");
        return JsonSerializer.SerializeToElement(new[] { json });
    }

    private sealed class SavedEventAdapter : IStimulusDeliveryIntentAdapter
    {
        public string IntentKind => "publish";
        public StimulusDispatchRequest CreateRequest(RuntimePostCommitIntent intent) => new("event", "named", intent.Payload,
            idempotencyKey: intent.IdempotencyKey, requestedBy: "publish") { ResumeAllMatchingBookmarks = true };
    }
    private sealed class Access(PersistenceScope scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(scope); }
    private sealed class FakeIngress : IRuntimeIngressDeliveryStore
    {
        internal RuntimeCommittedRecipient? Current;
        internal bool Cancelled;
        public ValueTask<RuntimeCommittedRecipient> ReadCommittedRecipientAsync(RuntimeIngressCommandOrigin origin, CancellationToken cancellationToken = default) =>
            Cancelled || Current is null ? ValueTask.FromException<RuntimeCommittedRecipient>(new TriggerOperationException("conflict", "No immutable physical dispatch seal.")) : ValueTask.FromResult(Current);
    }
    private sealed class WitnessExecutor : IWorkflowExecutionCommandExecutor, IIngressCommandWitness
    {
        internal int Calls; internal bool Fail, HasWitness;
        public ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult(Fail ? new WorkflowExecutionCommandProcessResult(true, RuntimeSchedulerDrainStopReason.Faulted, true, false, "precheckpoint fault") : WorkflowExecutionCommandProcessResult.NoDrain); }
        public ValueTask<bool> HasIngressWitnessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken) => ValueTask.FromResult(HasWitness);
    }
    private sealed class StringTypes : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias) { alias = "String"; return type == typeof(string); }
        public bool TryGetType(string alias, out Type type) { type = typeof(string); return alias == "String"; }
        public IEnumerable<Type> ListTypes() => [typeof(string)];
        public string GetAliasOrDefault(Type type) => type == typeof(string) ? "String" : type.FullName!;
        public Type GetTypeOrDefault(string alias) => alias == "String" ? typeof(string) : typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type) => TryGetType(alias, out type);
    }
}
