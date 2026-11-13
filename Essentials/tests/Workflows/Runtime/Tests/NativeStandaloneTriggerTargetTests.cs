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
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Triggers;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Values;
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
    [InlineData("tenant", false)]
    [InlineData("logical-tenant", false)]
    [InlineData("tenant", true)]
    [InlineData("logical-tenant", true)]
    public async Task Native_saved_cohort_start_retains_scope_after_the_source_claim_is_complete(string tenant, bool coalescing)
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
            { StimulusDeliveryOrigin = savedOrigin };
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
        var start = StartItem(sealedStart);
        using var childDrain = fixture.BeginCheckpoint(key.WorkflowExecutionId);
        await fixture.StartHandler.HandleAsync(start);
        var checkpoint = Assert.Single((await fixture.SelectedQueue.ListAsync(new(key.WorkflowExecutionId))).Items);
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
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.CheckpointHandler.HandleAsync(changedWork).AsTask());
            Assert.Null(await fixture.Executions.FindAsync(key.WorkflowExecutionId));
        }
        var forgedJson = checkpoint.Payload!.Value.Deserialize<Dictionary<string, JsonElement>>()!;
        forgedJson[nameof(RuntimeCheckpointCommandPayload.StimulusDeliveryOrigin)] = JsonSerializer.SerializeToElement(new RuntimeStimulusDeliveryOrigin(
            1, scope, sourceId, fingerprint, "not-in-cohort"));
        var forged = new RuntimeSchedulerWorkItem(checkpoint.WorkItemId, checkpoint.WorkflowExecutionId, checkpoint.CommandId,
            checkpoint.CommandKind, checkpoint.EnvelopeId, checkpoint.IdempotencyKey, checkpoint.EnqueuedAt, checkpoint.RecordedAt,
            checkpoint.Sequence, JsonSerializer.SerializeToElement(forgedJson), checkpoint.CommandMetadata, checkpoint.EnvelopeMetadata);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.CheckpointHandler.HandleAsync(forged).AsTask());
        Assert.Null(await fixture.Executions.FindAsync(key.WorkflowExecutionId));
        Assert.Equal(checkpoint.WorkItemId, Assert.Single((await fixture.SelectedQueue.ListAsync(new(key.WorkflowExecutionId))).Items).WorkItemId);
        await fixture.CheckpointHandler.HandleAsync(checkpoint);
        await fixture.Queue.DeleteAsync(key.WorkflowExecutionId, checkpoint.WorkItemId);
        var child = (await fixture.Executions.FindAsync(key.WorkflowExecutionId))!;
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
        internal static async Task<Fixture> CreateAsync(string tenant = "tenant", bool coalescing = false)
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
            var payload = JsonSerializer.SerializeToElement(new { payload = new { account = "business-value" } });
            var authority = new WorkflowExecutionAuthoritySnapshot("standalone-trigger", "source-actor");
            var fingerprint = RuntimeStandaloneIngressSource.ComputeRequestFingerprint(scope, tenant, "trigger", "test", "occurrence", schema, payload, authority);
            var source = new RuntimeStandaloneIngressSource(1, "admission", "dispatch", Now, scope, tenant, partition,
                "trigger", 1, 1, "test", "occurrence", fingerprint, schema, payload, RuntimeIngressAdmissionPolicy.RequireAll, authority);
            fixture.Recipient = new("recipient", "subscription", 1, 1, "definition", "native", "slot", 1, "activation", identity,
                new("reference", "Published", "publication", "1", "definition", "version", "1", "activation", "slot"),
                RuntimeIngressAdmission.ComputeInputContractFingerprint(contract), 1, 1,
                new Dictionary<string, JsonElement> { ["AccountId"] = JsonSerializer.SerializeToElement("business-value") }, authority,
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
                durableValueStateStore: new InMemoryDurableValueStateStore(), operationalStateStore: fixture.Executions.ExecutionLivenessStateStore,
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
