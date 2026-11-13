using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Physical_source_control_and_dispatch_seal_have_one_causal_winner(bool ef, bool sealFirst)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        Assert.True(fixture.Port.SupportsControls);
        var accepted = await fixture.Port.AdmitAsync(fixture.Request);
        var recipient = Assert.Single(accepted.Item.SubscriptionDelivery!.Recipients);
        var claim = Assert.Single(await harness.Claims.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 10)));
        var context = new RuntimeDeliverySourceContext(claim, Scope);
        var seal = NativeIngressSeal(accepted.Item, recipient);
        var current = sealFirst ? await fixture.Port.CommitDispatchAsync(context, seal) : accepted.Item;
        var request = Control(fixture, current, RuntimeIngressControlKind.CancelPendingRecipients);

        var controlled = await fixture.Port.ControlAsync(request);

        var progress = Assert.Single(controlled.Item.SubscriptionDelivery!.Progress);
        Assert.Equal(sealFirst ? RuntimeIngressControlOutcome.DispatchCommitted : RuntimeIngressControlOutcome.Cancelled,
            Assert.Single(controlled.Receipt.Recipients).Outcome);
        Assert.Equal(sealFirst ? RuntimeRecipientPhase.DispatchCommitted : RuntimeRecipientPhase.CancelledBeforeDispatch, progress.Phase);
        if (sealFirst)
        {
            Assert.True(seal.IsEquivalentTo(progress.Seal));
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, controlled.Item.Status);
            Assert.Equal(seal.Fingerprint, (await fixture.Port.ReadCommittedRecipientAsync(new RuntimeIngressCommandOrigin(accepted.Item.OutboxItemId,
                accepted.Item.SourceFingerprint, accepted.Item.SubscriptionDelivery.SelectionFingerprint, Scope, recipient.RecipientId))).Seal.Fingerprint);
        }
        else
        {
            Assert.Equal(RuntimePostCommitOutboxStatus.Cancelled, controlled.Item.Status);
            await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => fixture.Port.CommitDispatchAsync(context, seal).AsTask());
            Assert.Null(progress.Seal);
        }
        Assert.Equal(accepted.Item.SourceFingerprint, controlled.Item.SourceFingerprint);
        Assert.Equal(accepted.Item.SubscriptionDelivery.SelectionFingerprint, controlled.Item.SubscriptionDelivery.SelectionFingerprint);
        Assert.Empty((await fixture.Queue.ListAsync(new(recipient.StartIdentity.WorkflowExecutionId))).Items);
        Assert.Equal(1, (await fixture.Resources.FindAsync(fixture.Request.Scope, "ingress-trigger"))!.Trigger.Revision);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Lost_control_ack_replays_before_resource_tombstone_fences_and_changed_content_cannot_reuse_the_receipt(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        var accepted = await fixture.Port.AdmitAsync(fixture.Request);
        var request = Control(fixture, accepted.Item, RuntimeIngressControlKind.CancelPendingRecipients);
        async Task LoseAcknowledgement()
        {
            await fixture.Port.ControlAsync(request);
            throw new IOException("The physical control committed before its acknowledgement was lost.");
        }
        await Assert.ThrowsAsync<IOException>(LoseAcknowledgement);
        var committed = (await fixture.Port.FindDeliveryAsync(fixture.Request.Scope, accepted.Item.OutboxItemId))!;
        await fixture.Resources.WriteAsync(fixture.Request.Scope, new("delete-after-control", "delete-after-control", "operator", 1,
            DeletedIngressResource(2), []));

        var replay = await fixture.Port.ControlAsync(request);

        Assert.True(replay.Replayed);
        Assert.True(committed.IsEquivalentTo(replay.Item));
        Assert.Single(replay.Item.SubscriptionDelivery!.ControlFacts!.Receipts);
        foreach (var changed in new[]
        {
            request with { RequestFingerprint = "changed-content" },
            request with { ExpectedTriggerRevision = 2 },
            request with { Recipients = [new(request.Recipients[0].RecipientId, new string('a', 64))] }
        })
            Assert.Equal("conflict", (await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Port.ControlAsync(changed).AsTask())).Code);
        Assert.Equal(2, (await fixture.Resources.FindAsync(fixture.Request.Scope, "ingress-trigger"))!.Trigger.Revision);
        Assert.Equal(TriggerDesiredState.Deleted, (await fixture.Resources.FindAsync(fixture.Request.Scope, "ingress-trigger"))!.Trigger.DesiredState);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_stale_resource_revision_refuses_controls_without_source_changes_and_fresh_tombstone_control_cannot_resurrect_cancelled_work(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        var accepted = await fixture.Port.AdmitAsync(fixture.Request);
        var stale = Control(fixture, accepted.Item, RuntimeIngressControlKind.CancelPendingRecipients);
        await fixture.Resources.WriteAsync(fixture.Request.Scope, new("pause-before-control", "pause-before-control", "operator", 1, IngressResource(2, false), []));

        Assert.Equal("conflict", (await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Port.ControlAsync(stale).AsTask())).Code);
        Assert.True(accepted.Item.IsEquivalentTo((await fixture.Port.FindDeliveryAsync(fixture.Request.Scope, accepted.Item.OutboxItemId))!));
        var cancelled = await fixture.Port.ControlAsync(stale with { ExpectedTriggerRevision = 2 });
        await fixture.Resources.WriteAsync(fixture.Request.Scope, new("delete-before-retry", "delete-before-retry", "operator", 2, DeletedIngressResource(3), []));
        var retry = await fixture.Port.ControlAsync(Control(fixture, cancelled.Item, RuntimeIngressControlKind.RetryRecipients, "retry-cancelled", 3));
        Assert.Equal(RuntimeIngressControlOutcome.Cancelled, Assert.Single(retry.Receipt.Recipients).Outcome);
        Assert.Equal(RuntimePostCommitOutboxStatus.Cancelled, retry.Item.Status);
        Assert.Equal(cancelled.Item.SourceFingerprint, retry.Item.SourceFingerprint);
        Assert.Equal(cancelled.Item.SubscriptionDelivery!.SelectionFingerprint, retry.Item.SubscriptionDelivery!.SelectionFingerprint);
        Assert.Equal(3, (await fixture.Resources.FindAsync(fixture.Request.Scope, "ingress-trigger"))!.Trigger.Revision);
        Assert.Empty(await harness.Claims.ClaimAsync(new("after-delete", Now, TimeSpan.FromMinutes(1), 10)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Explicit_retry_reopens_one_finite_attempt_with_the_same_sealed_command_and_preserves_failure_history(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        var candidate = fixture.Request.Candidate;
        var finite = RuntimePostCommitOutboxItem.CreateStandaloneIngress(candidate.StandaloneIngress!, candidate.SubscriptionDelivery!, new(1, TimeSpan.FromMinutes(1)));
        var accepted = await fixture.Port.AdmitAsync(fixture.Request with { Candidate = finite });
        var recipient = Assert.Single(accepted.Item.SubscriptionDelivery!.Recipients);
        var claim = Assert.Single(await harness.Claims.ClaimAsync(new("first", Now, TimeSpan.FromMinutes(1), 10)));
        var context = new RuntimeDeliverySourceContext(claim, Scope);
        var seal = NativeIngressSeal(accepted.Item, recipient);
        await fixture.Port.CommitDispatchAsync(context, seal);
        await fixture.Port.RecordRecipientFailureAsync(context, recipient.RecipientId, RuntimeRecipientRecoveryState.BlockedCapability,
            "target-unavailable", Now.AddDays(1));
        await harness.Claims.RecordDeliveryResultAsync(claim, new(claim.OutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, Now, "first failure"));
        var failed = (await fixture.Port.FindDeliveryAsync(fixture.Request.Scope, claim.OutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, failed.Status);

        var retry = await fixture.Port.ControlAsync(Control(fixture, failed, RuntimeIngressControlKind.RetryRecipients));

        Assert.Equal(RuntimeIngressControlOutcome.RetryScheduled, Assert.Single(retry.Receipt.Recipients).Outcome);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, retry.Item.Status);
        Assert.Equal(failed.DeliveryAttemptCount, retry.Item.DeliveryAttemptCount);
        Assert.True(failed.RetryPolicy.IsEquivalentTo(retry.Item.RetryPolicy));
        Assert.Equal(failed.LastFailureMessage, retry.Item.LastFailureMessage);
        var progress = Assert.Single(retry.Item.SubscriptionDelivery!.Progress);
        Assert.True(seal.IsEquivalentTo(progress.Seal));
        Assert.Equal("target-unavailable", progress.LastFailureCode);
        Assert.Equal(RuntimeRecipientRecoveryState.None, progress.RecoveryState);
        var next = Assert.Single(await harness.Claims.ClaimAsync(new("manual", Now, TimeSpan.FromMinutes(1), 10)));
        Assert.True(seal.IsEquivalentTo(Assert.Single((await fixture.Port.CommitDispatchAsync(new(next, Scope), seal)).SubscriptionDelivery!.Progress).Seal));
        await harness.Claims.RecordDeliveryResultAsync(next, new(next.OutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, Now, "second failure"));
        Assert.Equal(2, (await fixture.Port.FindDeliveryAsync(fixture.Request.Scope, next.OutboxItemId))!.DeliveryAttemptCount);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await fixture.Port.FindDeliveryAsync(fixture.Request.Scope, next.OutboxItemId))!.Status);
        Assert.Empty(await harness.Claims.ClaimAsync(new("no-automatic-extra", Now.AddDays(1), TimeSpan.FromMinutes(1), 10)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Retry_during_a_live_delivery_does_not_take_the_claim_or_repeat_an_already_accepted_target(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        var accepted = await fixture.Port.AdmitAsync(fixture.Request);
        var recipient = Assert.Single(accepted.Item.SubscriptionDelivery!.Recipients);
        var claim = Assert.Single(await harness.Claims.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 10)));
        var current = (await fixture.Port.FindDeliveryAsync(fixture.Request.Scope, claim.OutboxItemId))!;
        var inProgress = await fixture.Port.ControlAsync(Control(fixture, current, RuntimeIngressControlKind.RetryRecipients));
        Assert.Equal(RuntimeIngressControlOutcome.DeliveryInProgress, Assert.Single(inProgress.Receipt.Recipients).Outcome);
        Assert.Equal(claim.FencingToken, inProgress.Item.DeliveryFencingToken);
        var context = new RuntimeDeliverySourceContext(claim, Scope);
        var seal = NativeIngressSeal(accepted.Item, recipient);
        await fixture.Port.CommitDispatchAsync(context, seal);
        var envelope = NativeIngressTargetEvidence.ReadEnvelope(seal);
        await fixture.Queue.EnqueueAsync(new(envelope.EnvelopeId, envelope.WorkflowExecutionId, envelope.Command.CommandId,
            envelope.Command.Kind, envelope.EnvelopeId, envelope.IdempotencyKey, envelope.EnqueuedAt, Now,
            envelope.Sequence, envelope.Command.Payload, envelope.Command.Metadata, envelope.Metadata));
        var receipt = new RuntimeTargetAcceptanceReceipt(recipient.RecipientId, seal.Fingerprint, recipient.StartIdentity, recipient.Material,
            RuntimeTargetAcceptanceKind.SchedulerCommand, envelope.EnvelopeId, Now);
        var acknowledged = await fixture.Port.RecordReceiptAsync(context, receipt);
        var retry = await fixture.Port.ControlAsync(Control(fixture, acknowledged, RuntimeIngressControlKind.RetryRecipients, "accepted-target"));
        Assert.Equal(RuntimeIngressControlOutcome.AlreadyAccepted, Assert.Single(retry.Receipt.Recipients).Outcome);
        Assert.Equal(RuntimeRecipientPhase.TargetAccepted, Assert.Single(retry.Item.SubscriptionDelivery!.Progress).Phase);
        Assert.True(receipt.IsEquivalentTo(Assert.Single(retry.Item.SubscriptionDelivery.Progress).Receipt));
        Assert.Single((await fixture.Queue.ListAsync(new(recipient.StartIdentity.WorkflowExecutionId))).Items);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Inspection_pages_filter_actual_scope_tenant_and_resource_and_transport_facts_do_not_create_dispatch_receipts(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        var first = await fixture.Port.AdmitAsync(fixture.Request);
        var second = await fixture.Port.AdmitAsync(RekeyControlSource(fixture.Request, "second"));
        var ids = new HashSet<string>(); string? cursor = null;
        do
        {
            var page = await fixture.Port.ListDeliveriesAsync(fixture.Request.Scope, new("ingress-trigger", 1, cursor));
            foreach (var item in page.Items) Assert.True(ids.Add(item.OutboxItemId));
            Assert.NotEqual(cursor, page.Next ?? "finished"); cursor = page.Next;
        } while (cursor is not null);
        Assert.Equal(new[] { first.Item.OutboxItemId, second.Item.OutboxItemId }.Order(), ids.Order());
        Assert.Empty((await fixture.Port.ListDeliveriesAsync(fixture.Request.Scope, new("another-trigger"))).Items);
        var wrongTenant = fixture.Request.Scope with { TenantId = "another-tenant" };
        Assert.Null(await fixture.Port.FindDeliveryAsync(wrongTenant, first.Item.OutboxItemId));
        Assert.Empty((await fixture.Port.ListDeliveriesAsync(wrongTenant, new())).Items);
        var otherScope = new TriggerScope(new("another-scope"), fixture.Request.Scope.TenantId);
        var other = harness.ManagementScoped(otherScope).Ingress;
        Assert.Null(await other.FindDeliveryAsync(otherScope, first.Item.OutboxItemId));
        Assert.Empty((await other.ListDeliveriesAsync(otherScope, new())).Items);
        await AssertInvalidAuthorityAsync(() => fixture.Port.ControlAsync(Control(fixture, first.Item,
            RuntimeIngressControlKind.CancelPendingRecipients) with { Scope = wrongTenant }).AsTask());
        var observer = Assert.IsAssignableFrom<IRuntimeIngressTransportObserver>(fixture.Port);
        await observer.RecordAsync(fixture.Request.Scope, new(first.Item.OutboxItemId, "exchange", RuntimeIngressTransportState.AwaitingResponse, 202, Now));
        await observer.RecordAsync(fixture.Request.Scope, new(first.Item.OutboxItemId, "exchange", RuntimeIngressTransportState.WriteCompleted, 202, Now.AddSeconds(1)));
        await observer.RecordAsync(fixture.Request.Scope, new(first.Item.OutboxItemId, "exchange", RuntimeIngressTransportState.TimedOut, null, Now.AddSeconds(2)));
        var observed = (await fixture.Port.FindDeliveryAsync(fixture.Request.Scope, first.Item.OutboxItemId))!;
        Assert.Equal([RuntimeIngressTransportState.AwaitingResponse, RuntimeIngressTransportState.WriteCompleted],
            observed.SubscriptionDelivery!.ControlFacts!.Transport.Select(item => item.State));
        var progress = Assert.Single(observed.SubscriptionDelivery.Progress);
        Assert.Equal(RuntimeRecipientPhase.Pending, progress.Phase); Assert.Null(progress.Seal); Assert.Null(progress.Receipt);
        Assert.Equal(first.Item.SourceFingerprint, observed.SourceFingerprint);
        Assert.Throws<InvalidOperationException>(() => observed.SubscriptionDelivery.WithControlFacts(new([], [])));
    }

    private static RuntimeIngressControlRequest Control(IngressFixture fixture, RuntimePostCommitOutboxItem item, RuntimeIngressControlKind kind,
        string requestId = "control", long revision = 1) => new(fixture.Request.Scope, item.OutboxItemId, item.SourceFingerprint,
        item.SubscriptionDelivery!.SelectionFingerprint, "ingress-trigger", "operator", requestId, "content-" + requestId, kind,
        item.SubscriptionDelivery.Progress.Select(progress => new RuntimeIngressRecipientExpectation(progress.RecipientId,
            RuntimeIngressInspection.ProgressFingerprint(progress))).ToArray()) { ExpectedTriggerRevision = revision };

    private static StandaloneTrigger DeletedIngressResource(long revision) => new("ingress-trigger", "Deleted source", "event", 1,
        JsonSerializer.SerializeToElement(new { }), revision, revision, revision, TriggerDesiredState.Deleted, new(TriggerManagementKind.Studio), Now, Now, "creator",
        hasActivated: true, tenantId: Scope.Value);

    private static RuntimeIngressAdmissionRequest RekeyControlSource(RuntimeIngressAdmissionRequest request, string key)
    {
        var old = request.Candidate.StandaloneIngress!;
        var envelope = JsonNode.Parse(old.CanonicalPayload.GetRawText())!; envelope["source"]!["key"] = key;
        var source = new RuntimeStandaloneIngressSource(1, "admission-" + key, "dispatch-" + key, old.RecordedAt, old.Scope, old.TenantId, old.Partition,
            old.TriggerId, old.TriggerRevision, old.AssociationGeneration, old.SourceFamily, key,
            RuntimeStandaloneIngressSource.ComputeRequestFingerprint(old.Scope, old.TenantId, old.TriggerId, old.SourceFamily, key, old.PayloadSchema,
                old.CanonicalPayload.GetProperty("submittedIntent"), old.SourceActorAttribution), old.PayloadSchema, JsonSerializer.SerializeToElement(envelope),
            old.AdmissionPolicy, old.SourceActorAttribution);
        var recipients = request.Candidate.SubscriptionDelivery!.Recipients.Select(oldRecipient =>
        {
            var start = new RuntimeSubscriptionStartIdentity(1, source.Scope, source.OutboxItemId, oldRecipient.SubscriptionId);
            return new RuntimeSubscriptionRecipient(start.Digest, oldRecipient.SubscriptionId, oldRecipient.SubscriptionRevision, oldRecipient.GrantRevision,
                oldRecipient.LogicalDefinitionId, oldRecipient.ModelKey, oldRecipient.SlotId, oldRecipient.SlotRevision, oldRecipient.ActivationId,
                oldRecipient.Material, oldRecipient.SourceProvenance, oldRecipient.InputContractFingerprint, oldRecipient.MappingRevision, oldRecipient.MappingVersion,
                oldRecipient.Inputs, oldRecipient.Authority, oldRecipient.TenantId, oldRecipient.Partition, start);
        }).ToArray();
        var delivery = new RuntimeSubscriptionDelivery(1, source.OutboxItemId, source.Fingerprint, source.Scope, recipients,
            request.Candidate.SubscriptionDelivery.Refusals, recipients.Select(recipient => new RuntimeRecipientProgress(recipient.RecipientId, RuntimeRecipientPhase.Pending)).ToArray());
        return request with { Candidate = RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, delivery, request.Candidate.RetryPolicy) };
    }
}
