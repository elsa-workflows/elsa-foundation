using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Added_authorization_sensitive_wait_after_empty_preflight_refuses_all_source_effects(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        var request = WithWaitCandidate(fixture.Request, new("http", "route", JsonSerializer.SerializeToElement(new { raw = "request" }),
            new Dictionary<string, string>(), observedWaits: []));
        harness.BeforeAcceptance = async () => { await SaveIngressWaitAsync(harness, "protected-wait", protectedPolicy: true); };
        var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Port.AdmitAsync(request).AsTask());
        Assert.Equal("conflict", refusal.Code);
        Assert.Null(await harness.Lookup.FindDurableAsync(request.Candidate.OutboxItemId));
        Assert.Empty((await fixture.Queue.ListAsync(new("already-running"))).Items);
        Assert.Empty((await fixture.Queue.ListAsync(new(Assert.Single(request.Candidate.SubscriptionDelivery!.Recipients).StartIdentity.WorkflowExecutionId))).Items);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Changed_wait_generation_or_authorization_metadata_refuses_the_complete_fresh_cohort(bool ef, bool metadata)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        var bookmark = await SaveIngressWaitAsync(harness, "wait");
        var observed = (await harness.WaitBookmarks.ObserveAsync("already-running", "wait"))!;
        var request = WithWaitCandidate(fixture.Request, new("http", "route", null, new Dictionary<string, string>(),
            observedWaits: [RuntimeIngressWaitObservation.From(observed)]));
        harness.BeforeAcceptance = async () =>
        {
            await harness.WaitBookmarks.SaveAsync(metadata ? bookmark with { Metadata = new Dictionary<string, string> { ["authorizationPolicy"] = "private" } } : bookmark);
        };
        Assert.Equal("conflict", (await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Port.AdmitAsync(request).AsTask())).Code);
        Assert.Null(await harness.Lookup.FindDurableAsync(request.Candidate.OutboxItemId));
        Assert.Empty(await harness.Outbox.GetDeliverableAsync(new(Now, 10)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Accepted_waits_freeze_before_new_roots_and_remain_owed_after_root_cancel_and_claim_expiry(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        await SaveIngressWaitAsync(harness, "wait");
        var observation = (await harness.WaitBookmarks.ObserveAsync("already-running", "wait"))!;
        var request = WithWaitCandidate(fixture.Request, new("http", "route", JsonSerializer.SerializeToElement(new { number = 42 }),
            new Dictionary<string, string>(), observedWaits: [RuntimeIngressWaitObservation.From(observation)]));
        var accepted = await fixture.Port.AdmitAsync(request);
        var waits = accepted.Item.SubscriptionDelivery!.WaitDelivery!;
        var recipient = Assert.Single(waits.Recipients);
        Assert.Equal("already-running", recipient.Identity.WorkflowExecutionId);
        Assert.Contains("wait-material", await harness.Delivery.ListPinnedExecutableArtifactIdsAsync());
        await SaveIngressWaitAsync(harness, "late-wait");
        var replay = await fixture.Port.AdmitAsync(request);
        Assert.True(replay.Replayed);
        Assert.Single(replay.Item.SubscriptionDelivery!.WaitDelivery!.Recipients);

        var root = Assert.Single(accepted.Item.SubscriptionDelivery.Recipients);
        var cancelled = await fixture.Port.CancelPendingAsync(new(request.Scope, accepted.Item.OutboxItemId, accepted.Item.SourceFingerprint,
            accepted.Item.SubscriptionDelivery.SelectionFingerprint, [root.RecipientId]));
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, cancelled.Item.Status);
        Assert.False(cancelled.Item.SubscriptionDelivery!.IsSettled);
        var claim = Assert.Single(await harness.Claims.ClaimAsync(new("first", Now, TimeSpan.FromMinutes(1), 10)));
        var source = new RuntimeDeliverySourceContext(claim, Scope);
        var seal = RuntimeIngressWaitCommand.CreateSeal(accepted.Item, recipient);
        await fixture.Port.CommitWaitDispatchAsync(source, seal);
        var claimed = new RuntimeIngressWaitReceipt(recipient.WaitId, seal.Fingerprint, recipient.Identity,
            recipient.Expected.RegistrationCapture!.Executable, RuntimeIngressWaitAcceptanceKind.ConsumptionCheckpoint, "invented", Now);
        Assert.Equal("unavailable", (await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Port.RecordWaitReceiptAsync(source, claimed).AsTask())).Code);
        Assert.Null(Assert.Single((await harness.Lookup.FindDurableAsync(accepted.Item.OutboxItemId))!.SubscriptionDelivery!.WaitDelivery!.Progress).Receipt);
        harness.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(seal.Fingerprint, (await fixture.Port.ReadCommittedWaitAsync(seal.Origin)).Seal.Fingerprint);
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => fixture.Port.RecordWaitReceiptAsync(source, claimed).AsTask());
        var envelope = RuntimeIngressWaitCommand.ReadEnvelope(seal);
        await fixture.Queue.EnqueueAsync(new(envelope.EnvelopeId, envelope.WorkflowExecutionId, envelope.Command.CommandId,
            envelope.Command.Kind, envelope.EnvelopeId, envelope.IdempotencyKey, envelope.EnqueuedAt, harness.Clock.GetUtcNow(), envelope.Sequence,
            envelope.Command.Payload, envelope.Command.Metadata, envelope.Metadata));
        var successor = Assert.Single(await harness.Claims.ClaimAsync(new("successor", harness.Clock.GetUtcNow(), TimeSpan.FromMinutes(1), 10)));
        var complete = await fixture.Port.RecordWaitReceiptAsync(new(successor, Scope), claimed);
        var progress = Assert.Single(complete.SubscriptionDelivery!.WaitDelivery!.Progress);
        Assert.Equal(RuntimeIngressWaitAcceptanceKind.SchedulerCommand, progress.Receipt!.Kind);
        Assert.Equal(envelope.EnvelopeId, progress.Receipt.EvidenceId);
        Assert.True(complete.SubscriptionDelivery.IsSettled);
        await harness.Completion.CompleteClaimAsync(new(successor, new(complete.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, harness.Clock.GetUtcNow())));
        // The queued target still requires the immutable seal; completion does not delete its physical source.
        Assert.Equal(seal.Fingerprint, (await fixture.Port.ReadCommittedWaitAsync(seal.Origin)).Seal.Fingerprint);
        Assert.Contains("wait-material", await harness.Delivery.ListPinnedExecutableArtifactIdsAsync());
    }

    private static RuntimeIngressAdmissionRequest WithWaitCandidate(RuntimeIngressAdmissionRequest original, RuntimeIngressWaitCandidate candidate)
    {
        var old = original.Candidate.StandaloneIngress!;
        var envelope = old.CanonicalPayload.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        envelope["waitCandidate"] = JsonSerializer.SerializeToElement(candidate, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var source = new RuntimeStandaloneIngressSource(old.Version, old.AdmissionId, old.LogicalIntentId, old.RecordedAt, old.Scope, old.TenantId,
            old.Partition, old.TriggerId, old.TriggerRevision, old.AssociationGeneration, old.SourceFamily, old.SourceKey, old.RequestFingerprint,
            old.PayloadSchema, JsonSerializer.SerializeToElement(envelope), old.AdmissionPolicy, old.SourceActorAttribution);
        var selection = original.Candidate.SubscriptionDelivery!;
        var delivery = new RuntimeSubscriptionDelivery(selection.Version, source.OutboxItemId, source.Fingerprint, source.Scope,
            selection.Recipients, selection.Refusals, selection.Progress, resourceCaptures: selection.ResourceCaptures);
        return original with { Candidate = RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, delivery, original.Candidate.RetryPolicy), WaitCandidate = candidate };
    }

    private static async Task<BookmarkState> SaveIngressWaitAsync(Harness harness, string bookmarkId, bool protectedPolicy = false)
    {
        // Keep real ordinary suspension state: a bookmark with no private state or typed registration is not resumable.
        var template = RuntimeEventExecutableTestFixture.Create("wait");
        var material = new WorkflowExecutable(new("wait-material", "wait-definition", "wait-version", "1", "hash"),
            template.RootActivity, template.ResumeTargets, Now, template.CompatibilityMetadata, template.IncidentStrategy);
        await using var runtime = WorkflowExecutionHarness.Create().Build(material.Identity, "already-running", ["wait-activity"]);
        await runtime.RunAsync(material);
        material = (await runtime.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(material.Identity.ArtifactId))!;
        var suspended = Assert.Single(await runtime.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("already-running"));
        var originalBookmark = Assert.Single(await runtime.Services.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync("already-running"));
        var originalRegistration = Assert.Single(suspended.TriggerRegistrations!);
        if (await harness.Artifacts.FindAsync(material.Identity.ArtifactId) is null) await harness.Artifacts.SaveAsync(material);
        if (await harness.Executions.FindAsync("already-running") is null)
            await harness.Executions.SaveAsync(new("already-running", material.Identity, WorkflowExecutionStatus.Running, null, Now, Now, Now,
                null, null, null, Scope.Value, new Dictionary<string, string>()) { Partition = new(Scope.Value), RunKind = WorkflowRunKind.PublishedRun });
        var bookmark = new BookmarkState(bookmarkId, "already-running", "wait-activity", originalBookmark.ExecutableNodeId, originalBookmark.ResumeTargetId, "http", "route", null,
            protectedPolicy ? new Dictionary<string, string> { ["authorizationPolicy"] = "private" } : new Dictionary<string, string>(), Now, null);
        var current = await harness.WaitActivities.FindAsync("already-running", "wait-activity");
        var ids = (current?.BookmarkIds ?? []).Append(bookmarkId).Distinct(StringComparer.Ordinal).ToArray();
        var registration = new ActivityTriggerRegistration(bookmarkId, suspended.InvocationId, bookmark.ResumeTargetId, originalRegistration.PayloadType,
            bookmark.StimulusType, bookmark.StimulusHash, originalRegistration.DeduplicationPolicy, bookmark.Metadata);
        await harness.WaitActivities.SaveAsync(suspended with { BookmarkIds = ids,
            TriggerRegistrations = (current?.TriggerRegistrations ?? []).Where(item => item.RegistrationId != bookmarkId).Append(registration).ToArray() });
        return await harness.WaitBookmarks.SaveAsync(bookmark);
    }
}
