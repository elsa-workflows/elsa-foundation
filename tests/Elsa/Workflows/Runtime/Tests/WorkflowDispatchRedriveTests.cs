using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowDispatchRedriveTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FailedAt = Now.AddMinutes(1);
    private static readonly DateTimeOffset RedriveAt = Now.AddMinutes(2);

    [Fact]
    public async Task Eligible_detached_failure_reopens_the_same_dispatch_and_outbox_identity()
    {
        var fixture = await CreateFailureAsync();

        var result = await fixture.RedriveStore.RedriveAsync(new(fixture.Identity.DispatchId, "request-1", RedriveAt));
        var dispatch = Assert.IsType<WorkflowDispatchRecord>(await fixture.DispatchStore.FindAsync(fixture.Identity.DispatchId));
        var outbox = Assert.IsType<RuntimePostCommitOutboxItem>(await fixture.Store.FindAsync(fixture.DeadLetter.OutboxItemId));

        Assert.Equal(WorkflowDispatchRedriveDisposition.Accepted, result.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Pending, result.Status);
        Assert.Equal(1, result.Generation);
        Assert.Equal(fixture.Identity.DeliveryIncidentId(0), result.IncidentId);
        Assert.Equal(fixture.DeadLetter.OutboxItemId, result.DeadLetterId);

        Assert.Equal(WorkflowDispatchStatus.Pending, dispatch.Status);
        Assert.Equal(1, WorkflowDispatchLifecycle.ReadDeliveryGeneration(dispatch));
        Assert.Equal("request-1", WorkflowDispatchLifecycle.ReadDeliveryRedriveRequestId(dispatch));
        Assert.True(WorkflowDispatchLifecycle.ImmutableFieldsEqual(fixture.FailedDispatch, dispatch));
        Assert.Equal(fixture.FailedDispatch.ParentWorkflowExecutionId, dispatch.ParentWorkflowExecutionId);
        Assert.Equal(fixture.FailedDispatch.ParentActivityExecutionId, dispatch.ParentActivityExecutionId);
        Assert.Equal(fixture.FailedDispatch.ChildWorkflowExecutionId, dispatch.ChildWorkflowExecutionId);
        Assert.Equal(fixture.FailedDispatch.ChildExecutable, dispatch.ChildExecutable);
        Assert.Equal(fixture.FailedDispatch.ChildSource, dispatch.ChildSource);
        Assert.Equal(3, fixture.FailedDispatch.DispatchNestingDepth);
        Assert.Equal(3, dispatch.DispatchNestingDepth);
        Assert.Equal("dispatch-context", dispatch.Metadata["test.context"]);

        Assert.Equal(fixture.DeadLetter.OutboxItemId, outbox.OutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, outbox.Status);
        Assert.Equal(RedriveAt, outbox.AvailableAt);
        Assert.Equal(0, outbox.DeliveryAttemptCount);
        Assert.Equal(fixture.DeadLetter.DeliveryFencingToken + 1, outbox.DeliveryFencingToken);
        Assert.Null(outbox.DeliveringOwnerId);
        Assert.Null(outbox.DeliveryVisibleAfter);
        Assert.Null(outbox.LastFailureMessage);
        Assert.Equal(fixture.DeadLetter.Intent.IntentId, outbox.Intent.IntentId);
        Assert.Equal(fixture.DeadLetter.Intent.IdempotencyKey, outbox.Intent.IdempotencyKey);
        Assert.Equal(fixture.DeadLetter.Intent.WorkflowExecutionId, outbox.Intent.WorkflowExecutionId);
        Assert.Equal(fixture.DeadLetter.Intent.ActivityExecutionId, outbox.Intent.ActivityExecutionId);
        Assert.Equal(fixture.DeadLetter.Intent.Payload?.GetRawText(), outbox.Intent.Payload?.GetRawText());
        Assert.Equal("intent-context", outbox.Intent.Metadata["test.intent"]);
        Assert.True(fixture.DeadLetter.RetryPolicy.IsEquivalentTo(outbox.RetryPolicy));
        Assert.Equal("policy-context", outbox.RetryPolicy.Metadata["test.policy"]);
        Assert.Equal("outbox-context", outbox.Metadata["test.outbox"]);

        var admission = await fixture.DispatchStore.TryAdmitAsync(fixture.Identity.DispatchId, RedriveAt.AddSeconds(1));
        Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted, admission.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Started, admission.Record.Status);
        Assert.Equal(3, admission.Record.DispatchNestingDepth);
    }

    [Fact]
    public async Task Same_request_is_idempotent_without_advancing_generation_or_fence_twice()
    {
        var fixture = await CreateFailureAsync();
        var request = new WorkflowDispatchRedriveRequest(fixture.Identity.DispatchId, "request-1", RedriveAt);

        var accepted = await fixture.RedriveStore.RedriveAsync(request);
        var afterAccepted = Assert.IsType<RuntimePostCommitOutboxItem>(await fixture.Store.FindAsync(fixture.DeadLetter.OutboxItemId));
        var replayed = await fixture.RedriveStore.RedriveAsync(request);
        var afterReplay = Assert.IsType<RuntimePostCommitOutboxItem>(await fixture.Store.FindAsync(fixture.DeadLetter.OutboxItemId));

        Assert.Equal(WorkflowDispatchRedriveDisposition.Accepted, accepted.Disposition);
        Assert.Equal(WorkflowDispatchRedriveDisposition.AlreadyApplied, replayed.Disposition);
        Assert.Equal(1, replayed.Generation);
        Assert.Equal(afterAccepted.DeliveryFencingToken, afterReplay.DeliveryFencingToken);
        Assert.Equal(afterAccepted.AvailableAt, afterReplay.AvailableAt);
    }

    [Fact]
    public async Task Different_request_conflicts_while_the_redriven_delivery_is_active()
    {
        var fixture = await CreateFailureAsync();

        var accepted = await fixture.RedriveStore.RedriveAsync(new(fixture.Identity.DispatchId, "request-1", RedriveAt));
        var conflicting = await fixture.RedriveStore.RedriveAsync(new(fixture.Identity.DispatchId, "request-2", RedriveAt.AddSeconds(1)));
        var dispatch = Assert.IsType<WorkflowDispatchRecord>(await fixture.DispatchStore.FindAsync(fixture.Identity.DispatchId));

        Assert.Equal(WorkflowDispatchRedriveDisposition.Accepted, accepted.Disposition);
        Assert.Equal(WorkflowDispatchRedriveDisposition.ActiveConflict, conflicting.Disposition);
        Assert.Equal(1, WorkflowDispatchLifecycle.ReadDeliveryGeneration(dispatch));
        Assert.Equal("request-1", WorkflowDispatchLifecycle.ReadDeliveryRedriveRequestId(dispatch));
    }

    [Theory]
    [InlineData(WorkflowDispatchMode.WaitForCompletion, true)]
    [InlineData(WorkflowDispatchMode.FireAndForget, false)]
    public async Task Waited_or_non_delivery_failure_is_not_eligible(
        WorkflowDispatchMode mode,
        bool deliveryFailure)
    {
        var fixture = await CreateFailureAsync(mode, deliveryFailure);

        var result = await fixture.RedriveStore.RedriveAsync(new(fixture.Identity.DispatchId, "request-1", RedriveAt));

        Assert.Equal(WorkflowDispatchRedriveDisposition.NotEligible, result.Disposition);
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, result.Status);
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, (await fixture.DispatchStore.FindAsync(fixture.Identity.DispatchId))!.Status);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await fixture.Store.FindAsync(fixture.DeadLetter.OutboxItemId))!.Status);
    }

    [Fact]
    public async Task Wrong_intent_kind_is_not_eligible_even_when_all_deterministic_ids_match()
    {
        var fixture = await CreateFailureAsync(intentKind: "Unsupported.Intent");

        var result = await fixture.RedriveStore.RedriveAsync(new(fixture.Identity.DispatchId, "request-1", RedriveAt));

        Assert.Equal(WorkflowDispatchRedriveDisposition.NotEligible, result.Disposition);
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, (await fixture.DispatchStore.FindAsync(fixture.Identity.DispatchId))!.Status);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await fixture.Store.FindAsync(fixture.DeadLetter.OutboxItemId))!.Status);
    }

    [Fact]
    public async Task Redrive_advances_the_fence_and_rejects_a_stale_pre_redrive_claim_result()
    {
        var fixture = await CreateFailureAsync();
        await fixture.RedriveStore.RedriveAsync(new(fixture.Identity.DispatchId, "request-1", RedriveAt));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.RecordDeliveryResultAsync(
            fixture.FinalClaim,
            new RuntimePostCommitOutboxDeliveryResult(
                fixture.DeadLetter.OutboxItemId,
                RuntimePostCommitOutboxStatus.Delivered,
                RedriveAt.AddSeconds(1))).AsTask());

        var outbox = Assert.IsType<RuntimePostCommitOutboxItem>(await fixture.Store.FindAsync(fixture.DeadLetter.OutboxItemId));
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, outbox.Status);
        Assert.Equal(fixture.FinalClaim.FencingToken + 1, outbox.DeliveryFencingToken);
    }

    [Fact]
    public async Task One_hundred_duplicate_races_converge_on_one_accepted_generation()
    {
        var fixture = await CreateFailureAsync();
        var request = new WorkflowDispatchRedriveRequest(fixture.Identity.DispatchId, "request-race", RedriveAt);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var racers = Enumerable.Range(0, 100)
            .Select(async _ =>
            {
                await gate.Task;
                return await fixture.RedriveStore.RedriveAsync(request);
            })
            .ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(racers);

        Assert.Single(results, result => result.Disposition == WorkflowDispatchRedriveDisposition.Accepted);
        Assert.Equal(99, results.Count(result => result.Disposition == WorkflowDispatchRedriveDisposition.AlreadyApplied));
        Assert.All(results, result => Assert.Equal(1, result.Generation));
        var dispatch = Assert.IsType<WorkflowDispatchRecord>(await fixture.DispatchStore.FindAsync(fixture.Identity.DispatchId));
        var outbox = Assert.IsType<RuntimePostCommitOutboxItem>(await fixture.Store.FindAsync(fixture.DeadLetter.OutboxItemId));
        Assert.Equal(1, WorkflowDispatchLifecycle.ReadDeliveryGeneration(dispatch));
        Assert.Equal(fixture.FinalClaim.FencingToken + 1, outbox.DeliveryFencingToken);
    }

    [Fact]
    public void Runtime_core_registers_a_scoped_redrive_guard_over_the_atomic_checkpoint_store()
    {
        using var provider = new ServiceCollection().AddWorkflowRuntime().BuildServiceProvider();
        using var scope = provider.CreateScope();

        var redrive = scope.ServiceProvider.GetRequiredService<IWorkflowDispatchRedriveStore>();
        var checkpoint = scope.ServiceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();

        Assert.NotSame(checkpoint, redrive);
    }

    [Fact]
    public async Task Runtime_core_redrive_rejects_a_dispatch_outside_the_selected_tenant_scope()
    {
        using var provider = new ServiceCollection().AddWorkflowRuntime().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatchStore = scope.ServiceProvider.GetRequiredService<IWorkflowDispatchStore>();
        var identity = new WorkflowDispatchIdentity("parent-1", "activity-1");
        await dispatchStore.SaveAsync(NewDispatch(identity, WorkflowDispatchMode.FireAndForget));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IWorkflowDispatchRedriveStore>()
                .RedriveAsync(new WorkflowDispatchRedriveRequest(identity.DispatchId, "request-1", RedriveAt))
                .AsTask());

        Assert.DoesNotContain("tenant-1", exception.Message, StringComparison.Ordinal);
        var unchanged = Assert.IsType<WorkflowDispatchRecord>(await dispatchStore.FindAsync(identity.DispatchId));
        Assert.Equal(WorkflowDispatchStatus.Pending, unchanged.Status);
        Assert.Equal(0, WorkflowDispatchLifecycle.ReadDeliveryGeneration(unchanged));
        Assert.Null(WorkflowDispatchLifecycle.ReadDeliveryRedriveRequestId(unchanged));
    }

    [Fact]
    public async Task Scoped_redrive_store_accepts_matching_tenant_and_delegates_atomically()
    {
        var fixture = await CreateFailureAsync();
        var guard = new ScopedWorkflowDispatchRedriveStore(
            fixture.Store,
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-1"))));

        var result = await guard.RedriveAsync(new WorkflowDispatchRedriveRequest(
            fixture.Identity.DispatchId,
            "scoped-request",
            RedriveAt));

        Assert.Equal(WorkflowDispatchRedriveDisposition.Accepted, result.Disposition);
        Assert.Equal(1, result.Generation);
    }

    [Fact]
    public async Task Scoped_redrive_store_rejects_mismatched_tenant_without_mutation()
    {
        var fixture = await CreateFailureAsync();
        var guard = new ScopedWorkflowDispatchRedriveStore(
            fixture.Store,
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("other-tenant"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.RedriveAsync(
            new WorkflowDispatchRedriveRequest(fixture.Identity.DispatchId, "scoped-request", RedriveAt)).AsTask());

        Assert.True(WorkflowDispatchLifecycle.RecordsEqual(
            fixture.FailedDispatch,
            (await fixture.DispatchStore.FindAsync(fixture.Identity.DispatchId))!));
    }

    [Fact]
    public async Task Scoped_redrive_store_validates_request_and_cancellation_before_mutation()
    {
        var fixture = await CreateFailureAsync();
        var guard = new ScopedWorkflowDispatchRedriveStore(
            fixture.Store,
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-1"))));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<ArgumentNullException>(() => guard.RedriveAsync(null!).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guard.RedriveAsync(
            new WorkflowDispatchRedriveRequest(fixture.Identity.DispatchId, "scoped-request", RedriveAt),
            cancellation.Token).AsTask());
        Assert.True(WorkflowDispatchLifecycle.RecordsEqual(
            fixture.FailedDispatch,
            (await fixture.DispatchStore.FindAsync(fixture.Identity.DispatchId))!));
    }

    [Fact]
    public void Redrive_transition_returns_not_found_without_mutation()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WorkflowDispatchRedriveTransitions.Evaluate(null!, dispatch: null, deadLetter: null));

        var transition = WorkflowDispatchRedriveTransitions.Evaluate(
            new WorkflowDispatchRedriveRequest("missing-dispatch", "request-1", RedriveAt),
            dispatch: null,
            deadLetter: null);

        Assert.Equal(WorkflowDispatchRedriveDisposition.NotFound, transition.Result.Disposition);
        Assert.Null(transition.Result.Status);
        Assert.False(transition.HasMutation);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("status")]
    [InlineData("outbox")]
    [InlineData("kind")]
    [InlineData("intent")]
    [InlineData("idempotency")]
    [InlineData("workflow")]
    [InlineData("activity")]
    [InlineData("metadata")]
    [InlineData("metadata-missing")]
    [InlineData("max-attempts")]
    [InlineData("retry-forever")]
    public async Task Redrive_transition_rejects_mismatched_dead_letter_contract(string mismatch)
    {
        var fixture = await CreateFailureAsync();
        var original = fixture.DeadLetter;
        var metadata = original.Intent.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (mismatch == "metadata")
            metadata[RuntimeMetadataKeys.DispatchId] = "dispatch:wrong";
        else if (mismatch == "metadata-missing")
            metadata.Remove(RuntimeMetadataKeys.DispatchId);
        var intent = new RuntimePostCommitIntent(
            mismatch == "intent" ? "intent:wrong" : original.Intent.IntentId,
            mismatch == "workflow" ? "workflow:wrong" : original.Intent.WorkflowExecutionId,
            mismatch == "kind" ? "Unsupported.Intent" : original.Intent.Kind,
            original.Intent.RecordedAt,
            mismatch == "activity" ? "activity:wrong" : original.Intent.ActivityExecutionId,
            mismatch == "idempotency" ? "idempotency:wrong" : original.Intent.IdempotencyKey,
            original.Intent.Payload,
            metadata);
        var retryPolicy = mismatch switch
        {
            "max-attempts" => RuntimePostCommitRetryPolicy.None,
            "retry-forever" => RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)),
            _ => original.RetryPolicy
        };
        RuntimePostCommitOutboxItem? candidate = mismatch == "missing"
            ? null
            : new RuntimePostCommitOutboxItem(
            mismatch == "outbox" ? "outbox:wrong" : original.OutboxItemId,
            intent,
            mismatch == "status" ? RuntimePostCommitOutboxStatus.FailedRetryable : original.Status,
            original.RecordedAt,
            original.AvailableAt,
            retryPolicy,
            original.DeliveryAttemptCount,
            original.DeliveringOwnerId,
            original.DeliveryStartedAt,
            original.DeliveredAt,
            original.LastFailureMessage,
            original.Metadata,
            original.DeliveryFencingToken,
            original.DeliveryVisibleAfter);

        var transition = WorkflowDispatchRedriveTransitions.Evaluate(
            new WorkflowDispatchRedriveRequest(fixture.Identity.DispatchId, "request-1", RedriveAt),
            fixture.FailedDispatch,
            candidate);

        Assert.Equal(WorkflowDispatchRedriveDisposition.NotEligible, transition.Result.Disposition);
        Assert.False(transition.HasMutation);
    }

    private static async Task<FailureFixture> CreateFailureAsync(
        WorkflowDispatchMode mode = WorkflowDispatchMode.FireAndForget,
        bool deliveryFailure = true,
        string intentKind = WorkflowDispatchLifecycle.StartChildIntentKind)
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var store = new InMemoryRuntimeCheckpointCommitStore(state: state);
        var dispatchStore = new InMemoryWorkflowDispatchStore(state);
        var identity = new WorkflowDispatchIdentity("parent-1", "activity-1");
        var pending = NewDispatch(identity, mode);
        var deadLetterItem = NewStartOutbox(identity, intentKind);

        await dispatchStore.SaveAsync(pending);
        await store.AddPendingForTestingAsync(deadLetterItem);
        var claim = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "worker-1",
            Now,
            TimeSpan.FromMinutes(1),
            1)));
        await store.RecordDeliveryResultAsync(
            claim,
            new RuntimePostCommitOutboxDeliveryResult(
                deadLetterItem.OutboxItemId,
                RuntimePostCommitOutboxStatus.FailedFinal,
                FailedAt,
                WorkflowDispatchLifecycle.ChildStartDeliveryFailedCode));
        var deadLetter = Assert.IsType<RuntimePostCommitOutboxItem>(await store.FindAsync(deadLetterItem.OutboxItemId));
        var failed = deliveryFailure
            ? WorkflowDispatchLifecycle.TransitionToDispatchFailed(
                pending,
                deadLetter.OutboxItemId,
                generation: 0,
                attemptCount: 1,
                firstAttemptAt: Now,
                failedAt: FailedAt)
            : WorkflowDispatchLifecycle.TransitionToDispatchFailed(pending, FailedAt);
        await dispatchStore.SaveAsync(failed);

        var redriveStore = new ScopedWorkflowDispatchRedriveStore(
            store,
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-1"))));
        return new(store, redriveStore, dispatchStore, identity, failed, deadLetter, claim);
    }

    private static WorkflowDispatchRecord NewDispatch(
        WorkflowDispatchIdentity identity,
        WorkflowDispatchMode mode) =>
        new(
            identity.DispatchId,
            "parent-1",
            "activity-1",
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:artifact"),
            new WorkflowExecutableSourceProvenance(
                "source-1",
                "WorkflowDefinitionVersion",
                "version-1",
                "1.0.0",
                "definition-1",
                "version-1",
                "1.0.0",
                "publication-1",
                "slot-1"),
            mode,
            WorkflowDispatchStatus.Pending,
            "correlation-1",
            "tenant-1",
            new WorkflowExecutionPartition("partition-1"),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot(
                "system-1",
                "initiator-1",
                new Dictionary<string, string> { ["authority-context"] = "preserved" }),
            [new WorkflowDispatchInputDescriptor("input-1", "System.String")],
            Now,
            Now,
            new Dictionary<string, string> { ["test.context"] = "dispatch-context" },
            dispatchNestingDepth: 3);

    private static RuntimePostCommitOutboxItem NewStartOutbox(
        WorkflowDispatchIdentity identity,
        string intentKind)
    {
        using var document = JsonDocument.Parse("""{"input":"payload-context"}""");
        var intent = new RuntimePostCommitIntent(
            identity.StartIntentId,
            "parent-1",
            intentKind,
            Now,
            "activity-1",
            identity.StartIdempotencyKey,
            document.RootElement.Clone(),
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.DispatchId] = identity.DispatchId,
                ["test.intent"] = "intent-context"
            });
        return new RuntimePostCommitOutboxItem(
            "commit-1:" + identity.StartIntentId,
            intent,
            RuntimePostCommitOutboxStatus.Pending,
            Now,
            Now,
            new RuntimePostCommitRetryPolicy(
                3,
                TimeSpan.FromSeconds(10),
                new Dictionary<string, string> { ["test.policy"] = "policy-context" }),
            metadata: new Dictionary<string, string> { ["test.outbox"] = "outbox-context" });
    }

    private sealed record FailureFixture(
        InMemoryRuntimeCheckpointCommitStore Store,
        IWorkflowDispatchRedriveStore RedriveStore,
        InMemoryWorkflowDispatchStore DispatchStore,
        WorkflowDispatchIdentity Identity,
        WorkflowDispatchRecord FailedDispatch,
        RuntimePostCommitOutboxItem DeadLetter,
        RuntimePostCommitOutboxClaim FinalClaim);

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
