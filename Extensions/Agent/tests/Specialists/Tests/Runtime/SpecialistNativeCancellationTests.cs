using Elsa.Activities.Runtime.Services;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Journal;
using Elsa.Agent.Specialists.Services;
using Elsa.Agent.Specialists.Tests.Recovery;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Runtime;

public sealed partial class SpecialistNativeTests
{
    public static IEnumerable<object[]> InvalidCancellationSources() =>
        from durable in new[] { false, true }
        from defect in new[] { "scope", "privileged", "missing", "payload", "owner", "fence", "expired", "overlay", "parent", "contract", "child", "expired-after-proof" }
        select new object[] { durable, defect };

    [Theory]
    [MemberData(nameof(InvalidCancellationSources))]
    public async Task Cancellation_requires_its_physical_source_current_claim_and_exact_native_terminal_proof(bool durable, string defect)
    {
        using var files = new JournalFiles();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var trace = new LeaseTrace { HoldCancellation = true };
        var factory = new FixtureFactory("{\"Article\":\"unused\"}");
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, clock, trace);
        var executable = await ExecutableAsync(provider, allowContinuity: true);
        var (actor, _) = await StartAsync(provider, executable);
        var now = DateTimeOffset.UtcNow;
        var command = new WorkflowExecutionCommand("negative-cancel", "run", WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>());
        await actor.EnqueueAsync(new("negative-cancel-envelope", "run", command, "negative-cancel", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now));
        clock.Advance(TimeSpan.FromSeconds(1));
        _ = await provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10, "run", SpecialistSettlementBoundary.CancelKind));
        Assert.Equal(1, trace.CancellationAttempts);
        trace.HoldCancellation = false;
        clock.Advance(TimeSpan.FromSeconds(6));
        var claims = provider.GetRequiredService<IRuntimePostCommitOutboxClaimStore>();
        var claim = Assert.Single(await claims.ClaimAsync(new("cancel-proof", clock.GetUtcNow(), TimeSpan.FromMinutes(1), 10, "run", SpecialistSettlementBoundary.CancelKind)));
        var access = provider.GetRequiredService<IPersistenceAccessContextAccessor>();
        var scope = access.Current.RequireScope();
        var source = new PostCommitDeliveryContext(claim, scope);
        var workflows = provider.GetRequiredService<IWorkflowExecutionStateStore>();
        var activities = provider.GetRequiredService<IActivityExecutionStateStore>();
        var workflow = (await workflows.FindAsync("run"))!;
        var parent = (await activities.FindAsync("run", claim.Item.Intent.ActivityExecutionId!))!;
        var child = (await activities.ListAllAsync("run")).Single(state => state.ParentActivityExecutionId == parent.InvocationId);
        if (defect == "parent")
        {
            await workflows.SaveAsync(workflow with { Status = WorkflowExecutionStatus.Running });
            await activities.SaveAsync(parent with { Status = ActivityExecutionStatus.Running });
        }
        if (defect == "contract") await activities.SaveAsync(parent with { ContractIdentity = new(parent.ContractIdentity!.ActivityTypeKey, parent.ContractIdentity.ContractVersion, "changed-contract") });
        if (defect == "child") await activities.SaveAsync(child with { ParentActivityExecutionId = "foreign-parent" });
        if (defect == "expired") clock.Advance(TimeSpan.FromSeconds(61));
        var presented = defect == "scope" ? new PostCommitDeliveryContext(claim, new("other")) : source;
        if (defect is "missing" or "payload" or "owner" or "fence")
        {
            var item = claim.Item;
            var intent = item.Intent;
            if (defect == "payload") intent = new(intent.IntentId, intent.WorkflowExecutionId, intent.Kind, intent.RecordedAt, intent.ActivityExecutionId, intent.IdempotencyKey, Json("{\"version\":999}"));
            var owner = defect == "owner" ? "forged-owner" : claim.OwnerId;
            var fence = defect == "fence" ? claim.FencingToken + 1 : claim.FencingToken;
            var changed = new RuntimePostCommitOutboxItem(defect == "missing" ? "missing:" + intent.IntentId : item.OutboxItemId,
                intent, RuntimePostCommitOutboxStatus.Delivering, item.RecordedAt, item.AvailableAt, item.RetryPolicy, item.DeliveryAttemptCount,
                owner, claim.ClaimedAt, deliveryFencingToken: fence, deliveryVisibleAfter: claim.VisibleAfter);
            presented = new(new(changed, owner, fence, claim.ClaimedAt, claim.VisibleAfter), scope);
        }
        var physical = provider.GetRequiredService<IPostCommitOutboxLookupStore>();
        var overlay = new OverlayOnlySource(claim.Item);
        var expiry = new ExpiringCancelParentRead(activities, clock, parent.InvocationId);
        var selected = defect == "privileged" ? new SettlementAccess(PersistenceAccessContext.PrivilegedScoped(scope, new("test"))) : access;
        var reader = defect == "overlay" ? (IPostCommitOutboxLookupStore)overlay : physical;
        var nativeReader = defect == "expired-after-proof" ? expiry : activities;
        var handler = new SpecialistSettlementHandler(provider.GetRequiredService<SpecialistSettlementBoundary>(), nativeReader, reader, selected, clock,
            provider.GetRequiredService<SpecialistTurnJournal>(), provider.GetRequiredService<ISpecialistTurnStore>());
        await Assert.ThrowsAnyAsync<Exception>(() => handler.HandleAsync(presented).AsTask());
        Assert.Equal(1, trace.CancellationAttempts);
        Assert.Empty(trace.Cancellations);
        Assert.Equal(0, overlay.OverlayReads);
        await workflows.SaveAsync(workflow);
        await activities.SaveAsync(parent);
        await activities.SaveAsync(child);
        if (clock.GetUtcNow() >= claim.VisibleAfter)
            source = new(Assert.Single(await claims.ClaimAsync(new("cancel-recovery", clock.GetUtcNow(), TimeSpan.FromMinutes(1), 10, "run", SpecialistSettlementBoundary.CancelKind))), scope);
        await provider.GetRequiredService<SpecialistSettlementHandler>().HandleAsync(source);
        Assert.Equal(SpecialistCancellationOutcome.Cancelled, Assert.Single(trace.Cancellations).Outcome);
        Assert.Equal(2, trace.CancellationAttempts);
        Assert.Equal(0, factory.Calls);
        await Assert.ThrowsAsync<Elsa.Workflows.Runtime.Core.Exceptions.RuntimePostCommitSourceBoundaryException>(() =>
            provider.GetRequiredService<IRuntimePostCommitIntentDispatcher>().DispatchAsync(claim.Item.Intent).AsTask());
        Assert.Equal(2, trace.CancellationAttempts);
    }

    private sealed class ExpiringCancelParentRead(IActivityExecutionStateStore inner, FakeTimeProvider clock, string parentId) : IActivityExecutionStateStore
    {
        private int expired;
        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default) => inner.SaveAsync(state, cancellationToken);
        public async ValueTask<ActivityExecutionState?> FindAsync(string workflow, string activity, CancellationToken cancellationToken = default)
        {
            var state = await inner.FindAsync(workflow, activity, cancellationToken);
            // Expiry is caused by native-proof I/O, independently of whether a final source
            // lookup exists. Removing that guard must not remove the race being tested.
            if (activity == parentId && Interlocked.Exchange(ref expired, 1) == 0) clock.Advance(TimeSpan.FromSeconds(61));
            return state;
        }
        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query, CancellationToken cancellationToken = default) => inner.ListPageAsync(query, cancellationToken);
        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query, CancellationToken cancellationToken = default) => inner.ListByParentPageAsync(query, cancellationToken);
    }

    public static IEnumerable<object[]> NativeCancellationJourneys() =>
        from durable in new[] { false, true }
        from phase in new[] { "before-claim", "claimed", "dispatched", "reserved", "dispatching", "accepted" }
        from mode in new[] { "normal", "before-write", "lost-response" }
        from persistence in new[] { PersistenceMode.Balanced, PersistenceMode.Durable }
        from continuity in new[] { false, true }
        select new object[] { durable, phase, mode, persistence, continuity };

    [Theory]
    [MemberData(nameof(NativeCancellationJourneys))]
    public async Task Real_actor_cancellation_has_durable_idempotent_responsibility_without_native_application(bool durable, string phase, string mode, PersistenceMode persistence, bool continuity)
    {
        using var files = new JournalFiles();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var trace = new LeaseTrace { HoldCancellation = mode == "before-write", LoseCancellationResponse = mode == "lost-response" };
        var factory = new FixtureFactory("{\"Article\":\"returned model text\"}");
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, clock, trace, persistence);
        var executable = await ExecutableAsync(provider, allowContinuity: continuity);
        var (actor, _) = await StartAsync(provider, executable);
        var claim = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        async ValueTask CancelAsync()
        {
            var now = DateTimeOffset.UtcNow;
            var command = new WorkflowExecutionCommand("native-cancel", "run", WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>());
            await actor.EnqueueAsync(new("native-cancel-envelope", "run", command, "native-cancel", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now));
            clock.Advance(TimeSpan.FromSeconds(1));
            _ = await provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10, "run", SpecialistSettlementBoundary.CancelKind));
        }
        if (phase == "before-claim") await CancelAsync();
        if (phase == "claimed") trace.AfterClaimed = CancelAsync;
        if (phase == "dispatched") trace.AfterDispatched = CancelAsync;
        if (phase == "reserved") trace.AfterReserved = CancelAsync;
        if (phase == "dispatching") factory.BeforeResponse = CancelAsync;
        if (phase == "accepted") trace.AfterAccepted = async () => { trace.AlwaysLoseAcceptedReceiptRead = true; await CancelAsync(); };
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim).WaitAsync(TimeSpan.FromSeconds(20));
        await DrainAsync(actor, "cancelled-worker");
        Assert.Equal(1, trace.CancellationAttempts);
        trace.HoldCancellation = false;
        if (mode != "normal")
        {
            clock.Advance(TimeSpan.FromSeconds(6));
            var delivered = await provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10, "run", SpecialistSettlementBoundary.CancelKind));
            Assert.Equal(1, delivered.DeliveredCount);
            Assert.Equal(2, trace.CancellationAttempts);
        }
        trace.AlwaysLoseAcceptedReceiptRead = false;
        var receipts = trace.Cancellations.ToArray();
        var cancellation = receipts.Last();
        Assert.All(receipts, receipt => Assert.Equal(cancellation, receipt));
        var (document, request) = await NativeTurnAsync(provider, executable);
        Assert.Equal(phase is "dispatching" or "accepted" ? 1 : 0, factory.Calls);
        Assert.Equal(phase is "reserved" or "dispatching" or "accepted" ? 1 : 0, factory.Opens);
        Assert.Equal(request.Identity, cancellation.Cancellation.Identity);
        if (phase == "accepted")
        {
            Assert.Equal(SpecialistCancellationOutcome.AlreadyAccepted, cancellation.Outcome);
            Assert.Equal(SpecialistTurnStatus.Accepted, document.Status);
            Assert.NotNull(document.ProtectedResult);
        }
        else
        {
            Assert.Equal(phase is "claimed" or "dispatched" or "reserved" or "dispatching" && mode == "before-write" ? SpecialistCancellationOutcome.AlreadyTerminal : SpecialistCancellationOutcome.Cancelled, cancellation.Outcome);
            Assert.Equal(phase is "claimed" or "dispatched" or "reserved" or "dispatching" && mode == "before-write" ? SpecialistTurnStatus.Failed : SpecialistTurnStatus.Cancelled, document.Status);
            Assert.Null(document.ProtectedResult);
        }
        if (document.Status == SpecialistTurnStatus.Cancelled)
        {
            var refused = await Assert.ThrowsAsync<SpecialistTurnException>(() => provider.GetRequiredService<SpecialistTurnJournal>().ClaimAsync(
                document.Handle, request.Identity, "another-child", clock.GetUtcNow(), default).AsTask());
            Assert.Equal("specialist.turn.cancelled", refused.Code);
        }
        if (mode == "normal") await VerifyCancellationCheckpointAsync(provider);
        Assert.Null(document.Applications);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.All(await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run"), state => Assert.Null(state.Completion));
        if (phase is "before-claim" or "claimed" or "dispatched") Assert.Empty(document.ModelRequests ?? []);
        else Assert.Single(document.ModelRequests!);
        var source = (await provider.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(cancellation.Cancellation.SourceId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, source.Status);
        Assert.True(source.RetryPolicy.RetryUntilAcknowledged);
        Assert.Equal(cancellation.Cancellation.SourceFingerprint, RuntimeCheckpointCommitFingerprint.ComputeIntent(source.Intent));
        var conversation = await provider.GetRequiredService<ISpecialistTurnStore>().ReadConversationAsync(document.Handle, request.Identity, default);
        if (phase == "before-claim" || !continuity) Assert.Null(conversation);
        else
        {
            Assert.Equal(phase == "accepted" ? 1 : 0, conversation!.Revision);
            Assert.Null(conversation.ActiveTurnHandle);
            Assert.False(conversation.OutcomeUnknown);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_cancel_of_seeded_pre_entry_state_tombstones_a_delayed_first_stage_without_a_protected_placeholder(bool durable)
    {
        using var files = new JournalFiles();
        var trace = new LeaseTrace();
        var factory = new FixtureFactory("{\"Article\":\"unused\"}");
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, leaseTrace: trace);
        var executable = await ExecutableAsync(provider, allowContinuity: true);
        _ = await StartAsync(provider, executable);
        var (_, originalRequest) = await NativeTurnAsync(provider, executable);
        var workflows = provider.GetRequiredService<IWorkflowExecutionStateStore>();
        var activities = provider.GetRequiredService<IActivityExecutionStateStore>();
        var original = (await workflows.FindAsync("run"))!;
        var root = (await activities.ListAllAsync("run")).Single(state => state.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId);
        // A separately seeded pre-entry occurrence has no journal row, child, materialized input,
        // or private handle. Its cancellation below uses the real native actor/checkpoint/outbox.
        var workflow = original with { WorkflowExecutionId = "pre-entry-run" };
        var parent = root with
        {
            Execution = root.Execution with { WorkflowExecutionId = workflow.WorkflowExecutionId, ActivityExecutionId = "pre-entry-parent" },
            Status = ActivityExecutionStatus.Scheduled, StartedAt = null, CompletedAt = null, PrivateState = null, InputSnapshot = null,
            ContractIdentity = null, Attempts = null, Attempt = null, Completion = null, ExecutionScopeId = null,
            Metadata = new Dictionary<string, string>(), Provenance = root.Provenance with { Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.OperationKey] = "pre-entry-operation" } }
        };
        await workflows.SaveAsync(workflow);
        await activities.SaveAsync(parent);
        await provider.GetRequiredService<ISchedulerStateStore>().SaveAsync(new(workflow.WorkflowExecutionId, 0));
        var now = DateTimeOffset.UtcNow;
        var actor = await provider.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(workflow.WorkflowExecutionId,
            WorkflowExecutionActorActivationReason.Start, now, "seeded-pre-entry", WorkflowExecutionActorCapabilities.InProcessMailbox));
        var command = new WorkflowExecutionCommand("pre-entry-cancel", workflow.WorkflowExecutionId, WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>());
        await actor.EnqueueAsync(new("pre-entry-cancel-envelope", workflow.WorkflowExecutionId, command, "pre-entry-cancel", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now));
        _ = await provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10, workflow.WorkflowExecutionId, SpecialistSettlementBoundary.CancelKind));
        var receipt = Assert.Single(trace.Cancellations);
        Assert.Equal(SpecialistCancellationOutcome.CancelledBeforeAdmission, receipt.Outcome);
        Assert.Null(receipt.Cancellation.ChildInvocationId);
        Assert.Null(receipt.AdmissionGeneration);
        var refusal = await Assert.ThrowsAsync<SpecialistTurnException>(() => provider.GetRequiredService<SpecialistTurnJournal>().StageAsync(
            originalRequest with { Identity = receipt.Cancellation.Identity }, now, default).AsTask());
        Assert.Equal("specialist.turn.cancelled", refusal.Code);
        Assert.Equal(0, factory.Calls);
        Assert.Equal(0, factory.Opens);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await workflows.FindAsync(workflow.WorkflowExecutionId))!.Status);
        Assert.Null((await activities.FindAsync(workflow.WorkflowExecutionId, parent.InvocationId))!.PrivateState);
    }
    private static async ValueTask VerifyCancellationCheckpointAsync(ServiceProvider provider)
    {
        var workflow = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!;
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        var changes = new RuntimeCheckpointStateChangeSet(
            new("run", RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()), null,
            states.Select(state => new RuntimeStateChange<ActivityExecutionState>(state.InvocationId, RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>())).ToArray(), [], [], [], []);
        var commit = new RuntimeCheckpointCommit("cancel-validation-commit", new("cancel-validation", RuntimeCheckpointNames.ActivityCancelled, "run",
            workflow.CompletedAt!.Value, states.Select(state => state.InvocationId).ToArray(), new Dictionary<string, string>()), changes, [], new Dictionary<string, string>());
        var enricher = provider.GetRequiredService<IEnumerable<IRuntimeCheckpointCommitEnricher>>().OfType<SpecialistCheckpointEnricher>().Single();
        var enriched = await enricher.EnrichAsync(commit);
        var intent = Assert.Single(enriched.PostCommitIntents);
        Assert.Equal(SpecialistSettlementBoundary.CancelKind, intent.Kind);
        await enricher.ValidateFinalAsync(enriched);
        Assert.Single((await enricher.EnrichAsync(enriched)).PostCommitIntents);
        var removed = await Assert.ThrowsAsync<SpecialistTurnException>(() => enricher.ValidateFinalAsync(commit).AsTask());
        Assert.Equal("specialist.settlement.failed", removed.Code);
        Assert.Equal(commit.CommitId, removed.Data["contextId"]);
        Assert.NotNull(removed.InnerException);
        _ = await enricher.EnrichAsync(enriched);
        await Assert.ThrowsAsync<SpecialistTurnException>(() => enricher.ValidateFinalAsync(enriched with { PostCommitIntents = [intent, intent] }).AsTask());
        var forged = new RuntimePostCommitIntent(intent.IntentId, intent.WorkflowExecutionId, intent.Kind, intent.RecordedAt, intent.ActivityExecutionId, intent.IdempotencyKey, Json("{\"version\":999}"));
        await Assert.ThrowsAsync<SpecialistTurnException>(() => enricher.EnrichAsync(commit with { PostCommitIntents = [forged] }).AsTask());
    }

}
