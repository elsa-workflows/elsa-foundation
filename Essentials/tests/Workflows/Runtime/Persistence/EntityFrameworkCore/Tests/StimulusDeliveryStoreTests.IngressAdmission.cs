using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Checked_ingress_admits_actual_roots_and_replays_before_pause_unpublication_or_changed_candidate_checks(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        Assert.True(fixture.Port.SupportsAdmission);
        var first = await fixture.Port.AdmitAsync(fixture.Request);
        Assert.False(first.Replayed);
        Assert.True(first.Item.IsEquivalentTo((await harness.Lookup.FindDurableAsync(first.Item.OutboxItemId))!));
        Assert.Equal([fixture.Material.Identity.ArtifactId], await harness.Delivery.ListPinnedExecutableArtifactIdsAsync());
        Assert.Null(await harness.FindPublisher());
        await fixture.Resources.WriteAsync(fixture.Request.Scope,
            new("pause", "pause", "operator", 1, IngressResource(revision: 2, enabled: false), []));
        await fixture.Slots.TryDeactivateAsync("ingress-definition", "Published", WorkflowActivationSource.Publishing, 1, Now);
        var replay = await fixture.Port.FindAcceptedAsync(fixture.Request.Scope, first.Item.OutboxItemId,
            first.Item.StandaloneIngress!.RequestFingerprint);
        Assert.True(replay!.Replayed);
        Assert.True(first.Item.IsEquivalentTo(replay.Item));
        Assert.True((await fixture.Port.AdmitAsync(fixture.Request)).Replayed);
        var conflict = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Port.FindAcceptedAsync(fixture.Request.Scope,
            first.Item.OutboxItemId, new string('b', 64)).AsTask());
        Assert.Equal("conflict", conflict.Code);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Lost_admission_acknowledgement_reconciles_the_actual_accepted_row(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        harness.LoseInstallAcknowledgement = true;
        var reconciled = await fixture.Port.AdmitAsync(fixture.Request);
        Assert.True(reconciled.Replayed);
        Assert.True(reconciled.Item.IsEquivalentTo((await harness.Lookup.FindDurableAsync(reconciled.Item.OutboxItemId))!));
        Assert.Single(await harness.Outbox.GetDeliverableAsync(new(Now, 10)));
        Assert.Empty((await fixture.Queue.ListAsync(new(Assert.Single(reconciled.Item.SubscriptionDelivery!.Recipients).StartIdentity.WorkflowExecutionId))).Items);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Winner_between_initial_key_read_and_commit_replays_before_current_resource_checks(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        RuntimeIngressAdmissionReceipt? winner = null;
        harness.BeforeAcceptance = async () =>
        {
            var competitor = harness.IngressInSeparateScope();
            winner = await competitor.Port.AdmitAsync(fixture.Request);
            await competitor.Resources.WriteAsync(fixture.Request.Scope,
                new("pause", "pause", "operator", 1, IngressResource(2, false), []));
        };
        var replay = await fixture.Port.AdmitAsync(fixture.Request);
        Assert.NotNull(winner);
        Assert.False(winner.Replayed);
        Assert.True(replay.Replayed);
        Assert.True(winner.Item.IsEquivalentTo(replay.Item));
        Assert.Single(await harness.Outbox.GetDeliverableAsync(new(Now, 10)));
    }

    [Theory]
    [InlineData(false, "absent-refused-slot")]
    [InlineData(true, "absent-refused-slot")]
    [InlineData(false, "accepted-slot")]
    [InlineData(true, "accepted-slot")]
    [InlineData(false, "resource")]
    [InlineData(true, "resource")]
    [InlineData(false, "source")]
    [InlineData(true, "source")]
    public async Task Changed_observation_including_a_refused_absent_slot_prevents_the_whole_admission(bool ef, string change)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        harness.BeforeAcceptance = async () =>
        {
            if (change == "absent-refused-slot")
                await fixture.Slots.TryActivateAsync(new("missing-definition", "Published", "newly-published", WorkflowActivationSource.Publishing, 0, Now));
            else if (change == "accepted-slot")
                await fixture.Slots.TryDeactivateAsync("ingress-definition", "Published", WorkflowActivationSource.Publishing, 1, Now);
            else if (change == "resource")
                await fixture.Resources.WriteAsync(fixture.Request.Scope,
                    new("pause", "pause", "operator", 1, IngressResource(revision: 2, enabled: false), []));
            else await harness.References.SaveAsync(fixture.Reference with { DeletedAt = Now, DeletedReason = "retired-before-admission" });
        };
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Port.AdmitAsync(fixture.Request).AsTask());
        Assert.Null(await harness.Lookup.FindDurableAsync(fixture.Request.Candidate.OutboxItemId));
        Assert.Empty(await harness.Outbox.GetDeliverableAsync(new(Now, 10)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_refused_observation_and_forged_submitted_fingerprint_cannot_create_authority(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Port.AdmitAsync(fixture.Request with
            { Targets = fixture.Request.Targets.Where(target => target.SubscriptionId != "refused").ToArray() }).AsTask());
        Assert.Null(await harness.Lookup.FindDurableAsync(fixture.Request.Candidate.OutboxItemId));
        var source = fixture.Request.Candidate.StandaloneIngress!;
        var forged = new RuntimeStandaloneIngressSource(1, source.AdmissionId, source.LogicalIntentId, source.RecordedAt,
            source.Scope, source.TenantId, source.Partition, source.TriggerId, source.TriggerRevision, source.AssociationGeneration,
            source.SourceFamily, source.SourceKey, new string('b', 64), source.PayloadSchema, source.CanonicalPayload,
            source.AdmissionPolicy, source.SourceActorAttribution);
        var selection = fixture.Request.Candidate.SubscriptionDelivery!;
        var forgedDelivery = new RuntimeSubscriptionDelivery(1, forged.OutboxItemId, forged.Fingerprint, forged.Scope,
            selection.Recipients, selection.Refusals, selection.Progress);
        var forgedRequest = fixture.Request with { Candidate = RuntimePostCommitOutboxItem.CreateStandaloneIngress(forged, forgedDelivery,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5))) };
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Port.AdmitAsync(forgedRequest).AsTask());
        Assert.Null(await harness.Lookup.FindDurableAsync(forged.OutboxItemId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Physical_cancel_wins_before_seal_and_failure_state_does_not_claim_execution(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        var accepted = await fixture.Port.AdmitAsync(fixture.Request);
        var claim = Assert.Single(await harness.Claims.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 10)));
        var context = new RuntimeDeliverySourceContext(claim, Scope);
        var recipient = Assert.Single(accepted.Item.SubscriptionDelivery!.Recipients);
        var failed = await fixture.Port.RecordRecipientFailureAsync(context, recipient.RecipientId,
            RuntimeRecipientRecoveryState.BlockedCapability, "target-runtime-unavailable", Now.AddMinutes(1));
        var failedProgress = Assert.Single(failed.SubscriptionDelivery!.Progress);
        Assert.Equal(RuntimeRecipientPhase.Pending, failedProgress.Phase);
        Assert.Equal(RuntimeRecipientRecoveryState.BlockedCapability, failedProgress.RecoveryState);
        Assert.Equal(1, failedProgress.AttemptCount);
        var cancelled = await fixture.Port.CancelPendingAsync(new(fixture.Request.Scope, accepted.Item.OutboxItemId,
            accepted.Item.SourceFingerprint, accepted.Item.SubscriptionDelivery.SelectionFingerprint, [recipient.RecipientId]));
        Assert.Equal([recipient.RecipientId], cancelled.CancelledRecipientIds);
        Assert.Empty(cancelled.CommittedRecipientIds);
        var origin = new RuntimeIngressCommandOrigin(accepted.Item.OutboxItemId, accepted.Item.SourceFingerprint,
            accepted.Item.SubscriptionDelivery.SelectionFingerprint, Scope, recipient.RecipientId);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Port.ReadCommittedRecipientAsync(origin).AsTask());
        Assert.Equal(RuntimeRecipientPhase.CancelledBeforeDispatch,
            Assert.Single((await harness.Lookup.FindDurableAsync(accepted.Item.OutboxItemId))!.SubscriptionDelivery!.Progress).Phase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Port.CommitDispatchAsync(context,
            NativeIngressSeal(accepted.Item, recipient)).AsTask());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Seal_survives_claim_expiry_and_cancel_until_actual_deterministic_queue_receipt_is_observed(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        var accepted = await fixture.Port.AdmitAsync(fixture.Request);
        var recipient = Assert.Single(accepted.Item.SubscriptionDelivery!.Recipients);
        var firstClaim = Assert.Single(await harness.Claims.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 10)));
        var firstContext = new RuntimeDeliverySourceContext(firstClaim, Scope);
        var seal = NativeIngressSeal(accepted.Item, recipient);
        await fixture.Port.CommitDispatchAsync(firstContext, seal);
        var cancelled = await fixture.Port.CancelPendingAsync(new(fixture.Request.Scope, accepted.Item.OutboxItemId,
            accepted.Item.SourceFingerprint, accepted.Item.SubscriptionDelivery.SelectionFingerprint, [recipient.RecipientId]));
        Assert.Empty(cancelled.CancelledRecipientIds);
        Assert.Equal([recipient.RecipientId], cancelled.CommittedRecipientIds);
        Assert.Equal(RuntimeRecipientPhase.DispatchCommitted, Assert.Single(cancelled.Item.SubscriptionDelivery!.Progress).Phase);
        var alleged = new RuntimeTargetAcceptanceReceipt(recipient.RecipientId, seal.Fingerprint, recipient.StartIdentity,
            recipient.Material, RuntimeTargetAcceptanceKind.InitialCheckpoint, "unobserved-claim", Now);
        var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Port.RecordReceiptAsync(firstContext, alleged).AsTask());
        Assert.Equal("unavailable", refusal.Code);
        Assert.Null(Assert.Single((await harness.Lookup.FindDurableAsync(accepted.Item.OutboxItemId))!.SubscriptionDelivery!.Progress).Receipt);

        harness.Clock.Advance(TimeSpan.FromMinutes(2));
        var origin = new RuntimeIngressCommandOrigin(accepted.Item.OutboxItemId, accepted.Item.SourceFingerprint,
            accepted.Item.SubscriptionDelivery.SelectionFingerprint, Scope, recipient.RecipientId);
        Assert.Equal(seal.Fingerprint, (await fixture.Port.ReadCommittedRecipientAsync(origin)).Seal.Fingerprint);
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => fixture.Port.ReadSourceAsync(firstContext).AsTask());
        await fixture.Resources.WriteAsync(fixture.Request.Scope, new("pause", "pause", "operator", 1, IngressResource(2, false), []));
        await fixture.Slots.TryDeactivateAsync("ingress-definition", "Published", WorkflowActivationSource.Publishing, 1, harness.Clock.GetUtcNow());
        Assert.Equal(seal.Fingerprint, (await fixture.Port.ReadCommittedRecipientAsync(origin)).Seal.Fingerprint);

        var envelope = NativeIngressTargetEvidence.ReadEnvelope(seal);
        await fixture.Queue.EnqueueAsync(new(envelope.EnvelopeId, envelope.WorkflowExecutionId, envelope.Command.CommandId,
            envelope.Command.Kind, envelope.EnvelopeId, envelope.IdempotencyKey, envelope.EnqueuedAt, harness.Clock.GetUtcNow(),
            envelope.Sequence, envelope.Command.Payload, envelope.Command.Metadata, envelope.Metadata));
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => fixture.Port.RecordReceiptAsync(firstContext, alleged).AsTask());
        var successorClaim = Assert.Single(await harness.Claims.ClaimAsync(new("successor", harness.Clock.GetUtcNow(), TimeSpan.FromMinutes(1), 10)));
        var settled = await fixture.Port.RecordReceiptAsync(new(successorClaim, Scope), alleged);
        var progress = Assert.Single(settled.SubscriptionDelivery!.Progress);
        Assert.Equal(RuntimeRecipientPhase.TargetAccepted, progress.Phase);
        Assert.Equal(RuntimeTargetAcceptanceKind.SchedulerCommand, progress.Receipt!.Kind);
        Assert.Equal(envelope.EnvelopeId, progress.Receipt.EvidenceId);
        Assert.Equal(seal.Fingerprint, progress.Seal!.Fingerprint);
        await harness.Completion.CompleteClaimAsync(new(successorClaim,
            new(settled.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, harness.Clock.GetUtcNow())));
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await harness.Lookup.FindDurableAsync(settled.OutboxItemId))!.Status);
        Assert.Equal(seal.Fingerprint, (await fixture.Port.ReadCommittedRecipientAsync(origin)).Seal.Fingerprint);
        Assert.Single((await fixture.Queue.ListAsync(new(recipient.StartIdentity.WorkflowExecutionId))).Items);
    }

    private static RuntimeDispatchSeal NativeIngressSeal(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient)
    {
        var origin = new RuntimeIngressCommandOrigin(source.OutboxItemId, source.SourceFingerprint,
            source.SubscriptionDelivery!.SelectionFingerprint, Scope, recipient.RecipientId);
        var payload = new WorkflowExecutionStartCommandPayload(recipient.Material, recipient.Material.ArtifactId,
            new Dictionary<string, JsonElement>(), recipient.Inputs, source.StandaloneIngress!.CanonicalPayload, null,
            WorkflowRunKind.PublishedRun, recipient.SourceProvenance, null, null, recipient.TenantId, recipient.Partition,
            recipient.Authority, WorkflowExecutableStartAuthority.FromStandaloneTrigger(origin), 0);
        var command = new WorkflowExecutionCommand(recipient.StartIdentity.CommandId, recipient.StartIdentity.WorkflowExecutionId,
            WorkflowExecutionCommandKind.Start, source.RecordedAt, JsonSerializer.SerializeToElement(payload), new Dictionary<string, string>());
        var envelope = new WorkflowExecutionCommandEnvelope(recipient.StartIdentity.EnvelopeId, recipient.StartIdentity.WorkflowExecutionId,
            command, recipient.StartIdentity.IdempotencyKey, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, source.RecordedAt, partition: recipient.Partition);
        return new(1, source.OutboxItemId, source.SourceFingerprint, source.SubscriptionDelivery.SelectionFingerprint, recipient.RecipientId,
            recipient.StartIdentity, source.RecordedAt, NativeStandaloneTriggerRuntimeTarget.Protocol, 1, JsonSerializer.SerializeToElement(envelope));
    }

    private sealed record IngressFixture(IRuntimeIngressDeliveryStore Port, IStandaloneTriggerStore Resources,
        IWorkflowActivationAuthority Slots, RuntimeIngressAdmissionRequest Request, WorkflowExecutable Material,
        WorkflowExecutableSourceReference Reference, IWorkflowSchedulerWorkQueue Queue);

    private static async Task<IngressFixture> PrepareIngressAsync(Harness harness)
    {
        var (port, resources, slots, queue) = harness.Ingress();
        var material = new WorkflowExecutable(new("ingress-material", "material-producer", "material-version", "1", "hash"),
            new ExecutableNode("node", "authored", "test/activity", "1.0.0", new RuntimeActivityDescriptor("test", "1.0.0", JsonSerializer.SerializeToElement(new { })),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(), new(1, []), [],
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
        await harness.Artifacts.SaveAsync(material);
        var slot = (await slots.TryActivateAsync(new("ingress-definition", "Published", "ingress-activation", WorkflowActivationSource.Publishing, 0, Now))).Slot;
        var reference = new WorkflowExecutableSourceReference(WorkflowActivationReferenceIdentity.Create("ingress-activation"), material.Identity.ArtifactId,
            "definition", "ingress-definition", "1", "ingress-definition", "published-version", "published-label", Now, Now,
            WorkflowExecutableReferenceScope.Published, ActivationId: slot.ActiveActivationId, SlotId: slot.SlotId, TenantId: Scope.Value);
        await harness.References.SaveAsync(reference);
        var trigger = IngressResource();
        TriggerSubscription Sub(string id, string definition) => new(id, trigger.Id, definition, "Published", "a-model-hint", 1, 1, true, [],
            new(TriggerManagementKind.Studio), new("grant-" + id, trigger.Id, id, definition, "Published", "a-model-hint", "event", 1,
                TriggerGrantStatus.Granted, "grant-author", Now));
        var scope = new TriggerScope(Scope, Scope.Value);
        await resources.WriteAsync(scope, new("create-ingress", "create-ingress", "operator", null, trigger,
            [Sub("accepted", "ingress-definition"), Sub("refused", "missing-definition")]));
        var actor = new WorkflowExecutionAuthoritySnapshot("source-module", "initiator");
        var schema = new ValueTypeDescriptor("Test.Payload", schemaVersion: 1);
        var payload = JsonSerializer.SerializeToElement(new { value = 42 });
        var metadata = JsonSerializer.SerializeToElement(new { });
        var envelope = JsonSerializer.SerializeToElement(new
        {
            payload, metadata, submittedIntent = RuntimeIngressAdmission.ComputeSubmittedIntent(payload, metadata),
            source = new { family = "Test.Source", key = "key", recordedAt = Now, actor },
            trigger = new { trigger.Id, trigger.Name, trigger.TypeKey, trigger.SchemaVersion, trigger.Revision,
                trigger.ConfigurationRevision, trigger.AssociationRevision, trigger.Configuration }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var source = new RuntimeStandaloneIngressSource(1, "actual-admission", "dispatch", Now, Scope, Scope.Value, new(Scope.Value), trigger.Id, 1, 1,
            "Test.Source", "key", RuntimeStandaloneIngressSource.ComputeRequestFingerprint(Scope, Scope.Value, trigger.Id, "Test.Source", "key", schema,
                RuntimeIngressAdmission.ComputeSubmittedIntent(payload, metadata), actor),
            schema, envelope, RuntimeIngressAdmissionPolicy.Independent, actor);
        var identity = new RuntimeSubscriptionStartIdentity(1, Scope, source.OutboxItemId, "accepted");
        var recipient = new RuntimeSubscriptionRecipient(identity.Digest, "accepted", 1, 1, "ingress-definition", "native", slot.SlotId, slot.Revision,
            slot.ActiveActivationId!, material.Identity, WorkflowExecutableSourceProvenance.From(reference), RuntimeIngressAdmission.ComputeInputContractFingerprint(material.InputContract!),
            1, 1, new Dictionary<string, JsonElement>(), new("standalone-trigger:ingress-trigger", actor.RootInitiator, new Dictionary<string, string>
            { ["triggerId"] = trigger.Id, ["subscriptionId"] = "accepted", ["grantId"] = "grant-accepted", ["sourceFamily"] = "Test.Source" }),
            Scope.Value, new(Scope.Value), identity);
        var selection = new RuntimeSubscriptionDelivery(1, source.OutboxItemId, source.Fingerprint, Scope, [recipient],
            [new("refused", 1, "Admission", "target-unpublished")], [new(recipient.RecipientId, RuntimeRecipientPhase.Pending)]);
        var candidate = RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, selection, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5)));
        return new(port, resources, slots, new(scope, 1, candidate,
            [new("accepted", "ingress-definition", "Published", slot, reference), new("refused", "missing-definition", "Published", null, null)]), material, reference, queue);
    }

    private static StandaloneTrigger IngressResource(long revision = 1, bool enabled = true) => new("ingress-trigger", "Event trigger", "event", 1,
        JsonSerializer.SerializeToElement(new { }), revision, revision, revision, enabled ? TriggerDesiredState.Enabled : TriggerDesiredState.Paused,
        new(TriggerManagementKind.Studio), Now, Now, "creator", tenantId: Scope.Value);

    private sealed partial class Harness
    {
        private readonly InMemoryWorkflowActivationAuthority ingressAuthority = new();
        private readonly InMemoryWorkflowSchedulerWorkQueue ingressQueue = new();
        private readonly InMemoryBookmarkStateStore ingressBookmarks = new();
        private readonly InMemoryActivityExecutionStateStore ingressActivities = new();
        public IBookmarkStateStore WaitBookmarks => Context is null ? ingressBookmarks : new EfBookmarkStateStore(Context, new Accessor());
        public IActivityExecutionStateStore WaitActivities => Context is null ? ingressActivities : new EfActivityExecutionStateStore(Context, new Accessor(), new Codec());

        public (IRuntimeIngressDeliveryStore Port, IStandaloneTriggerStore Resources, IWorkflowActivationAuthority Slots, IWorkflowSchedulerWorkQueue Queue) Ingress(
            IEnumerable<IStimulusDeliveryIntentAdapter>? adapters = null)
        {
            IStandaloneTriggerStore resources = Context is null ? new InMemoryStandaloneTriggerStore(memory, new Accessor(), Clock) :
                new EfStandaloneTriggerStore(Context, new Accessor(), Clock);
            IWorkflowActivationAuthority authority = Context is null ? ingressAuthority : new EfWorkflowActivationAuthority(Context, new Accessor());
            IWorkflowSchedulerWorkQueue queue = Context is null ? ingressQueue : new EfSchedulerWorkQueueStore(Context, new Accessor(), new Codec());
            var artifacts = Artifacts; var executions = Executions;
            var proof = new NativeIngressTargetEvidence(queue, executions, Clock, executables: artifacts);
            var waitProof = new NativeIngressWaitTargetEvidence(queue, executions, WaitActivities, WaitBookmarks, Clock);
            var leaseManager = Observe(new WorkflowExecutableRootWriteLeaseManager(artifacts, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), Clock));
            IRuntimeIngressDeliveryStore port = Context is null
                ? new InMemoryRuntimeIngressDeliveryStore(resources, Outbox, authority, artifacts, References, executions, leaseManager, new Accessor(), Clock, proof,
                    stimulusAdapters: adapters, waitEvidence: waitProof, resumeResolver: new BookmarkResumeResolver(), checkpoints: Checkpoints)
                : new EfRuntimeIngressDeliveryStore(Context, resources, Outbox, authority, artifacts, References, executions, leaseManager, new Accessor(), Clock, proof,
                    stimulusAdapters: adapters, waitEvidence: waitProof, resumeResolver: new BookmarkResumeResolver(), checkpoints: Checkpoints);
            return (port, resources, authority, queue);
        }

        public (IRuntimeIngressDeliveryStore Port, IStandaloneTriggerStore Resources) IngressInSeparateScope()
        {
            if (Context is null)
            {
                var native = Ingress();
                return (native.Port, native.Resources);
            }
            var context = Open(); var access = new Accessor();
            var artifacts = new EfWorkflowExecutableStore(context, access);
            var references = new EfWorkflowExecutableSourceReferenceStore(context, access, new Codec(), Clock);
            var executions = new EfWorkflowExecutionStateStore(context, access, new Codec());
            var roots = Observe(new WorkflowExecutableRootWriteLeaseManager(artifacts, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), Clock));
            var source = new EfRuntimePostCommitOutboxStore(context, access, Clock, references, roots);
            var resources = new EfStandaloneTriggerStore(context, access, Clock);
            var queue = new EfSchedulerWorkQueueStore(context, access, new Codec());
            var evidence = new NativeIngressTargetEvidence(queue, executions, Clock, executables: artifacts);
            return (new EfRuntimeIngressDeliveryStore(context, resources, source, new EfWorkflowActivationAuthority(context, access),
                artifacts, references, executions, roots, access, Clock, evidence), resources);
        }
    }
}
