using System.Text.Json;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    private static readonly string[] SilentWaitDefects =
    [
        "private-missing", "registration-missing", "registration-duplicate", "registration-invocation", "registration-resume",
        "registration-stimulus", "registration-hash", "registration-type", "registration-collection", "registration-schema-version", "registration-schema", "duplicate-received", "duplicate-consumed",
        "once-received", "once-consumed", "dedup-received", "dedup-consumed"
    ];

    private static readonly string[] OtherWaitDefects =
    [
        "private-invocation", "private-external", "snapshot-missing", "snapshot-invocation", "snapshot-contract", "contract-missing",
        "contract-type", "contract-version", "contract-fingerprint", "completion-present"
    ];

    public static TheoryData<bool, string> IngressWaitCompatibilityCases
    {
        get
        {
            var result = new TheoryData<bool, string>();
            foreach (var ef in new[] { false, true })
            foreach (var defect in SilentWaitDefects.Concat(OtherWaitDefects)) result.Add(ef, defect);
            return result;
        }
    }

    [Theory]
    [InlineData(false, "document-version")] [InlineData(true, "document-version")]
    [InlineData(false, "document-incompatible")] [InlineData(true, "document-incompatible")]
    public async Task Incompatible_wait_documents_cannot_reach_fresh_source_acceptance(bool ef, string defect)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        var bookmark = await SaveIngressWaitAsync(harness, "wait");
        var state = (await harness.WaitActivities.FindAsync("already-running", "wait-activity"))!;
        var observed = (await harness.WaitBookmarks.ObserveAsync("already-running", "wait"))!;
        var request = WithWaitCandidate(fixture.Request, new("http", "route", null, new Dictionary<string, string>()));
        var identity = new RuntimeIngressWaitIdentity(1, Scope, request.Candidate.OutboxItemId, bookmark.WorkflowExecutionId, bookmark.BookmarkId, observed.Generation);
        var damaged = DamageIngressWait(state, identity, defect);
        if (ef)
            // EF already rejects incompatible activity documents at its writer; do not forge an unreadable row.
            await Assert.ThrowsAsync<InvalidOperationException>(() => harness.WaitActivities.SaveAsync(damaged).AsTask());
        else
        {
            await harness.WaitActivities.SaveAsync(damaged);
            Assert.Equal("wait-not-resumable", (await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Port.AdmitAsync(request).AsTask())).Code);
        }
        Assert.Null(await harness.Lookup.FindDurableAsync(request.Candidate.OutboxItemId));
        Assert.Empty(await harness.Outbox.GetDeliverableAsync(new(Now, 100)));
        Assert.Empty((await fixture.Queue.ListAsync(new("already-running"))).Items);
    }

    [Theory]
    [MemberData(nameof(IngressWaitCompatibilityCases))]
    public async Task Fresh_source_refuses_a_physically_unresumable_wait_before_any_acceptance_or_target_effect(bool ef, string defect)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        var bookmark = await SaveIngressWaitAsync(harness, "wait");
        var healthy = (await harness.WaitActivities.FindAsync("already-running", "wait-activity"))!;
        var observed = (await harness.WaitBookmarks.ObserveAsync("already-running", "wait"))!;
        var candidate = new RuntimeIngressWaitCandidate("http", "route", JsonSerializer.SerializeToElement(new { value = "detached-request" }),
            new Dictionary<string, string>(), Assert.Single(healthy.TriggerRegistrations!).PayloadType, "actual-source");
        var request = WithWaitCandidate(fixture.Request, candidate);
        var identity = new RuntimeIngressWaitIdentity(1, Scope, request.Candidate.OutboxItemId, bookmark.WorkflowExecutionId, bookmark.BookmarkId, observed.Generation);
        var damaged = DamageIngressWait(healthy, identity, defect);
        await harness.WaitActivities.SaveAsync(damaged);

        // Causal witness of the old gap: the actual ordinary handler drops these exact suspended states without
        // consuming their bookmark. No fake actor, callback or source receipt is used to establish that behavior.
        if (SilentWaitDefects.Contains(defect))
            await AssertOrdinaryWaitIsSilentAsync(harness, bookmark, candidate, identity, damaged);

        var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Port.AdmitAsync(request).AsTask());
        Assert.Equal("wait-not-resumable", refusal.Code);
        Assert.Null(await harness.Lookup.FindDurableAsync(request.Candidate.OutboxItemId));
        Assert.Empty(await harness.Outbox.GetDeliverableAsync(new(Now, 100)));
        Assert.Empty((await fixture.Queue.ListAsync(new(bookmark.WorkflowExecutionId))).Items);
        Assert.Empty((await fixture.Queue.ListAsync(new(Assert.Single(request.Candidate.SubscriptionDelivery!.Recipients).StartIdentity.WorkflowExecutionId))).Items);
        Assert.NotNull(await harness.WaitBookmarks.FindAsync(bookmark.WorkflowExecutionId, bookmark.BookmarkId));

        // Rejection stores no permanent negative result: repairing the actual same wait allows the same request.
        await harness.WaitActivities.SaveAsync(healthy);
        var repaired = await fixture.Port.AdmitAsync(request);
        Assert.False(repaired.Replayed);
        Assert.Single(repaired.Item.SubscriptionDelivery!.WaitDelivery!.Recipients);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Healthy_wait_capture_supports_explicit_source_type_and_ordinary_inherited_type(bool ef, bool explicitType)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        await SaveIngressWaitAsync(harness, "wait");
        var state = (await harness.WaitActivities.FindAsync("already-running", "wait-activity"))!;
        var type = Assert.Single(state.TriggerRegistrations!).PayloadType;
        var request = WithWaitCandidate(fixture.Request, new("http", "route", JsonSerializer.SerializeToElement(new { value = 42 }),
            new Dictionary<string, string>(), explicitType ? type : null, explicitType ? "actual-source" : null));
        var accepted = await fixture.Port.AdmitAsync(request);
        var wait = Assert.Single(accepted.Item.SubscriptionDelivery!.WaitDelivery!.Recipients);
        Assert.Equal("already-running", wait.Identity.WorkflowExecutionId);
        var seal = RuntimeIngressWaitCommand.CreateSeal(accepted.Item, wait);
        var payload = RuntimeIngressWaitCommand.ReadEnvelope(seal).Command.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!;
        if (explicitType) Assert.Equal(type.Alias, payload.TriggerDelivery!.PayloadType.Alias);
        else Assert.Null(payload.TriggerDelivery);
        Assert.Equal(RuntimeIngressWaitPhase.Pending, Assert.Single(accepted.Item.SubscriptionDelivery.WaitDelivery.Progress).Phase);
        Assert.Empty((await fixture.Queue.ListAsync(new("already-running"))).Items);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Wait_state_mutation_and_source_acceptance_obey_the_actual_winning_order(bool ef, bool mutationWins)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareIngressAsync(harness);
        var bookmark = await SaveIngressWaitAsync(harness, "wait");
        var state = (await harness.WaitActivities.FindAsync("already-running", "wait-activity"))!;
        var request = WithWaitCandidate(fixture.Request, new("http", "route", null, new Dictionary<string, string>()));
        async Task Mutate() { await harness.WaitActivities.SaveAsync(state with { PrivateState = null }); }
        if (mutationWins)
        {
            // Initial discovery sees the healthy typed wait; corruption wins before the protected commit.
            harness.BeforeAcceptance = Mutate;
            var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Port.AdmitAsync(request).AsTask());
            Assert.Equal("wait-not-resumable", refusal.Code);
            Assert.Null(await harness.Lookup.FindDurableAsync(request.Candidate.OutboxItemId));
            Assert.Empty(await harness.Outbox.GetDeliverableAsync(new(Now, 100)));
        }
        else
        {
            var accepted = await fixture.Port.AdmitAsync(request);
            await Mutate();
            var replay = await fixture.Port.AdmitAsync(request);
            Assert.True(replay.Replayed);
            Assert.True(accepted.Item.IsEquivalentTo(replay.Item));
            Assert.Single(replay.Item.SubscriptionDelivery!.WaitDelivery!.Recipients);
            Assert.Equal(RuntimeIngressWaitPhase.Pending, Assert.Single(replay.Item.SubscriptionDelivery.WaitDelivery.Progress).Phase);
        }
        Assert.NotNull(await harness.WaitBookmarks.FindAsync(bookmark.WorkflowExecutionId, bookmark.BookmarkId));
        Assert.Empty((await fixture.Queue.ListAsync(new("already-running"))).Items);
    }

    private static ActivityExecutionState DamageIngressWait(ActivityExecutionState state, RuntimeIngressWaitIdentity identity, string defect)
    {
        var registration = Assert.Single(state.TriggerRegistrations!);
        ActivityTriggerRegistration Registration(string? invocation = null, string? target = null, string? stimulus = null,
            string? hash = null, ValueTypeDescriptor? type = null, ActivityTriggerDeduplicationPolicy? policy = null) =>
            new(registration.RegistrationId, invocation ?? registration.InvocationId, target ?? registration.ResumeTargetKey,
                type ?? registration.PayloadType, stimulus ?? registration.StimulusType, hash ?? registration.StimulusHash,
                policy ?? registration.DeduplicationPolicy, registration.Metadata);
        ActivityInputSnapshot Snapshot(string? invocation = null, string? contract = null) => new(invocation ?? state.InvocationId,
            contract ?? state.InputSnapshot!.ContractFingerprint, state.InputSnapshot!.BindingFingerprint, state.InputSnapshot.Values, Now);
        ActivityInvocationContractIdentity Contract(string? type = null, string? version = null, string? fingerprint = null) =>
            new(type ?? state.ContractIdentity!.ActivityTypeKey, version ?? state.ContractIdentity!.ContractVersion,
                fingerprint ?? state.ContractIdentity!.SchemaFingerprint);
        if (defect.StartsWith("duplicate-", StringComparison.Ordinal) || defect.StartsWith("once-", StringComparison.Ordinal) || defect.StartsWith("dedup-", StringComparison.Ordinal))
        {
            var policy = defect.StartsWith("once-", StringComparison.Ordinal) ? ActivityTriggerDeduplicationPolicy.Once : ActivityTriggerDeduplicationPolicy.IdempotencyKey;
            var delivery = new ActivityTriggerDelivery(defect.StartsWith("duplicate-", StringComparison.Ordinal) ? identity.CommandId : "earlier-command",
                registration.RegistrationId, registration.PayloadType, ValueEnvelope.Null(registration.PayloadType, ValueProtectionPolicy.InstanceInline),
                "earlier-provider", Now, defect.StartsWith("dedup-", StringComparison.Ordinal) ? identity.IdempotencyKey : "earlier-key",
                defect.EndsWith("consumed", StringComparison.Ordinal) ? ActivityTriggerDeliveryStatus.Consumed : ActivityTriggerDeliveryStatus.Received);
            return state with { TriggerRegistrations = [Registration(policy: policy)], TriggerDeliveries = [delivery] };
        }
        return defect switch
        {
            "private-missing" => state with { PrivateState = null },
            "private-invocation" => state with { PrivateState = new("another-invocation", state.PrivateState!.StateVersion, state.PrivateState.Value, state.PrivateState.ProducedByAttemptId, Now) },
            "private-external" => state with { PrivateState = new(state.InvocationId, state.PrivateState!.StateVersion,
                ValueEnvelope.External(state.PrivateState.Value.Type, new("external-state", "locator", new Dictionary<string, string>()), ValueProtectionPolicy.InstanceInline), state.PrivateState.ProducedByAttemptId, Now) },
            "registration-missing" => state with { TriggerRegistrations = [] },
            "registration-duplicate" => state with { TriggerRegistrations = [registration, registration] },
            "registration-invocation" => state with { TriggerRegistrations = [Registration(invocation: "another-invocation")] },
            "registration-resume" => state with { TriggerRegistrations = [Registration(target: "another-resume-target")] },
            "registration-stimulus" => state with { TriggerRegistrations = [Registration(stimulus: "another-stimulus")] },
            "registration-hash" => state with { TriggerRegistrations = [Registration(hash: "another-hash")] },
            "registration-type" => state with { TriggerRegistrations = [Registration(type: new("Another.Payload", schemaVersion: 1))] },
            "registration-collection" => state with { TriggerRegistrations = [Registration(type: new(registration.PayloadType.Alias,
                CollectionKind.Array, registration.PayloadType.Schema, registration.PayloadType.SchemaVersion))] },
            "registration-schema-version" => state with { TriggerRegistrations = [Registration(type: new(registration.PayloadType.Alias,
                registration.PayloadType.CollectionKind, registration.PayloadType.Schema, (registration.PayloadType.SchemaVersion ?? 0) + 1))] },
            "registration-schema" => state with { TriggerRegistrations = [Registration(type: new(registration.PayloadType.Alias,
                registration.PayloadType.CollectionKind, JsonSerializer.SerializeToElement(new { incompatible = true }), registration.PayloadType.SchemaVersion))] },
            "snapshot-missing" => state with { InputSnapshot = null },
            "snapshot-invocation" => state with { InputSnapshot = Snapshot(invocation: "another-invocation") },
            "snapshot-contract" => state with { InputSnapshot = Snapshot(contract: "another-contract") },
            "contract-missing" => state with { ContractIdentity = null },
            "contract-type" => state with { ContractIdentity = Contract(type: "another-activity") },
            "contract-version" => state with { ContractIdentity = Contract(version: "another-version") },
            "contract-fingerprint" => state with { ContractIdentity = Contract(fingerprint: "another-contract") },
            "document-version" => state with { DocumentVersion = ActivityExecutionValueFlowDocumentVersions.Legacy },
            "document-incompatible" => state with { ValueFlowCompatibility = ActivityExecutionValueFlowDocumentVersionGuard.Incompatible(
                ActivityExecutionValueFlowDocumentVersions.Current, ActivityExecutionValueFlowDocumentVersions.Current, "incompatible-test", "Constructed incompatible document.") },
            "completion-present" => state with { Completion = new(state.InvocationId, "earlier-attempt", state.PrivateState!.Value, "Done", Now, "earlier-contract") },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
    }

    private static async Task AssertOrdinaryWaitIsSilentAsync(Harness harness, BookmarkState bookmark, RuntimeIngressWaitCandidate candidate,
        RuntimeIngressWaitIdentity identity, ActivityExecutionState state)
    {
        await using var runtime = WorkflowExecutionHarness.Create().ConfigureServices(services =>
        {
            services.AddSingleton(harness.Artifacts);
            services.AddSingleton(harness.Executions);
            services.AddSingleton(harness.WaitActivities);
            services.AddSingleton(harness.WaitBookmarks);
        }).Build();
        var execution = (await harness.Executions.FindAsync(bookmark.WorkflowExecutionId))!;
        var payload = new RuntimeResumeBookmarkCommandPayload(execution.PinnedExecutable, bookmark.BookmarkId,
            bookmark.ActivityExecutionId, bookmark.ExecutableNodeId, bookmark.ResumeTargetId, bookmark.StimulusType, bookmark.StimulusHash,
            candidate.Input, RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason,
            new(identity.CommandId, candidate.PayloadType!, candidate.ProviderId!, Now, identity.IdempotencyKey));
        var work = new RuntimeSchedulerWorkItem(identity.EnvelopeId, identity.WorkflowExecutionId, identity.CommandId,
            WorkflowExecutionCommandKind.ResumeBookmark, identity.EnvelopeId, identity.IdempotencyKey, Now, Now,
            payload: JsonSerializer.SerializeToElement(payload));
        var handler = new WorkflowResumeBookmarkSchedulerWorkHandler(runtime.Services.GetRequiredService<IServiceScopeFactory>(), harness.Clock);
        await handler.HandleAsync(work);
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(await harness.WaitActivities.FindAsync(bookmark.WorkflowExecutionId, bookmark.ActivityExecutionId)));
        Assert.NotNull(await harness.WaitBookmarks.FindAsync(bookmark.WorkflowExecutionId, bookmark.BookmarkId));
        Assert.Empty((await runtime.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new(bookmark.WorkflowExecutionId))).Items);
    }
}
