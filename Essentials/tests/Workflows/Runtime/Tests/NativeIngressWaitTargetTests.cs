using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class NativeIngressWaitTargetTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Saved_wait_command_preserves_existing_owner_and_exact_generation_with_deterministic_detached_input()
    {
        using var fixture = await Fixture.CreateAsync();
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Wait);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        Assert.True(seal.IsEquivalentTo(await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Wait)));
        Assert.Empty((await fixture.Queue.ListAsync(new("already-running"))).Items);
        var envelope = RuntimeIngressWaitCommand.ReadEnvelope(seal);
        Assert.Equal("already-running", envelope.WorkflowExecutionId);
        Assert.NotEqual(fixture.Root.StartIdentity.WorkflowExecutionId, envelope.WorkflowExecutionId);
        Assert.Equal(fixture.Wait.Identity.CommandId, envelope.Command.CommandId);
        Assert.Equal(Now, envelope.EnqueuedAt);
        var payload = envelope.Command.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!;
        Assert.Equal(fixture.Wait.Identity.Generation, payload.ExpectedBookmarkResume!.RegistrationCapture!.Generation);
        Assert.Equal("18446744073709551615", payload.Input!.Value.GetProperty("number").GetRawText());
        Assert.NotNull(payload.IngressWaitOrigin);
        Assert.Null(payload.TestRunDelivery);
        Assert.Equal(fixture.Wait.Identity.IdempotencyKey, payload.TriggerDelivery!.DeduplicationKey);
    }

    [Fact]
    public async Task Only_an_exact_physical_resume_queue_row_is_positive_acceptance()
    {
        using var fixture = await Fixture.CreateAsync();
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Wait); fixture.Seal(seal);
        Assert.Null(await fixture.Target.ReconcileAsync(fixture.Item, fixture.Wait, seal));
        var envelope = RuntimeIngressWaitCommand.ReadEnvelope(seal);
        await fixture.Queue.EnqueueAsync(WorkItem(envelope));
        var receipt = await fixture.Target.ReconcileAsync(fixture.Item, fixture.Wait, seal);
        Assert.Equal(RuntimeIngressWaitAcceptanceKind.SchedulerCommand, receipt!.Kind);
        Assert.Equal(envelope.EnvelopeId, receipt.EvidenceId);
        await fixture.Queue.DeleteAsync(envelope.WorkflowExecutionId, envelope.EnvelopeId);
        var altered = new RuntimeSchedulerWorkItem(envelope.EnvelopeId, envelope.WorkflowExecutionId, envelope.Command.CommandId,
            WorkflowExecutionCommandKind.ResumeBookmark, envelope.EnvelopeId, envelope.IdempotencyKey, envelope.EnqueuedAt,
            Now, payload: JsonSerializer.SerializeToElement(new { falsePayload = true }));
        await fixture.Queue.EnqueueAsync(altered);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Target.ReconcileAsync(fixture.Item, fixture.Wait, seal).AsTask());
    }

    [Fact]
    public async Task Missing_bookmark_stays_owed_but_a_physically_replaced_generation_is_typed_retirement()
    {
        using var fixture = await Fixture.CreateAsync();
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Wait); fixture.Seal(seal);
        await fixture.Bookmarks.DeleteAsync("already-running", "wait");
        Assert.Null(await fixture.Target.ReconcileAsync(fixture.Item, fixture.Wait, seal));
        await fixture.Bookmarks.SaveAsync(fixture.Bookmark with { Payload = JsonSerializer.SerializeToElement(new { replacement = true }) });
        var receipt = await fixture.Target.ReconcileAsync(fixture.Item, fixture.Wait, seal);
        Assert.Equal(RuntimeIngressWaitAcceptanceKind.RetiredGeneration, receipt!.Kind);
        Assert.Equal(RuntimeIngressWaitRetirementKind.ReplacedGeneration, receipt.Retirement!.Kind);
        Assert.True(receipt.Retirement.ObservedGeneration > fixture.Wait.Identity.Generation);
        Assert.Equal("already-running", receipt.Identity.WorkflowExecutionId);
    }

    [Fact]
    public async Task Wait_selection_does_not_change_when_progress_advances_and_legacy_resume_wire_has_no_ingress_arm()
    {
        using var fixture = await Fixture.CreateAsync();
        var selection = fixture.Item.SubscriptionDelivery!.WaitDelivery!;
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Wait);
        var changed = selection.WithProgress(selection.Progress.Single().CommitDispatch(seal));
        Assert.Equal(selection.SelectionFingerprint, changed.SelectionFingerprint);
        Assert.False(changed.IsSettled);
        Assert.Throws<ArgumentException>(() => new RuntimeIngressWaitRecipient(
            new(1, fixture.Scope, fixture.Item.OutboxItemId, "a-new-execution", "wait", fixture.Wait.Identity.Generation), fixture.Wait.Expected));
        var old = new RuntimeResumeBookmarkCommandPayload(fixture.Material, "wait", "activity", "node", "resume", "http", "route", null, "reason");
        Assert.DoesNotContain("IngressWaitOrigin", JsonSerializer.Serialize(old));
    }

    [Fact]
    public async Task Wait_dispatch_has_no_original_request_services_and_an_actor_ack_is_not_a_physical_receipt()
    {
        using var fixture = await Fixture.CreateAsync();
        var seal = await fixture.Target.PrepareDispatchAsync(fixture.Item, fixture.Wait); fixture.Seal(seal);
        await fixture.Target.DispatchAsync(fixture.Item, fixture.Wait, seal);
        Assert.Equal(1, fixture.Executor.Calls);
        Assert.Null(fixture.Executor.Options!.AmbientServices);
        Assert.Null(await fixture.Target.ReconcileAsync(fixture.Item, fixture.Wait, seal));
        fixture.Ingress.Current = null;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Target.DispatchAsync(fixture.Item, fixture.Wait, seal).AsTask());
        Assert.Equal(1, fixture.Executor.Calls);
    }

    private static RuntimeSchedulerWorkItem WorkItem(WorkflowExecutionCommandEnvelope envelope) => new(envelope.EnvelopeId,
        envelope.WorkflowExecutionId, envelope.Command.CommandId, envelope.Command.Kind, envelope.EnvelopeId, envelope.IdempotencyKey,
        envelope.EnqueuedAt, Now, envelope.Sequence, envelope.Command.Payload, envelope.Command.Metadata, envelope.Metadata);

    private sealed class Fixture : IDisposable
    {
        internal readonly PersistenceScope Scope = new("physical");
        internal readonly WorkflowExecutableIdentity Material = new("artifact", "definition", "version", "1", "hash");
        internal readonly FakeTimeProvider Clock = new(Now);
        internal readonly InMemoryWorkflowSchedulerWorkQueue Queue = new();
        internal readonly InMemoryWorkflowExecutionStateStore Executions = new();
        internal readonly InMemoryActivityExecutionStateStore Activities = new();
        internal readonly InMemoryBookmarkStateStore Bookmarks = new();
        internal readonly Executor Executor = new();
        internal readonly Ingress Ingress = new();
        private readonly InProcessWorkflowExecutionActorProvider actors;
        internal RuntimePostCommitOutboxItem Item = null!;
        internal RuntimeSubscriptionRecipient Root = null!;
        internal RuntimeIngressWaitRecipient Wait = null!;
        internal BookmarkState Bookmark = null!;
        internal NativeIngressWaitTarget Target = null!;
        private Fixture() => actors = new(Executor);
        internal static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            var actor = new WorkflowExecutionAuthoritySnapshot("standalone-trigger", "requester");
            var payload = JsonSerializer.SerializeToElement(new { payload = new { value = 1 } }); var schema = new ValueTypeDescriptor("Test.Payload", schemaVersion: 1);
            var request = RuntimeStandaloneIngressSource.ComputeRequestFingerprint(fixture.Scope, "actual-tenant", "trigger", "http", "key", schema, payload, actor);
            var source = new RuntimeStandaloneIngressSource(1, "admission", "dispatch", Now, fixture.Scope, "actual-tenant", new("physical"), "trigger", 1, 1,
                "http", "key", request, schema, payload, RuntimeIngressAdmissionPolicy.RequireAll, actor);
            fixture.Root = new("root", "subscription", 1, 1, "definition", "native", "slot", 1, "activation", fixture.Material,
                new("reference", "Published", "publication", "1", "definition", "version", "1", "activation", "slot"), new string('a', 64),
                1, 1, new Dictionary<string, JsonElement>(), actor, "actual-tenant", new("physical"), new(1, fixture.Scope, source.OutboxItemId, "subscription"));
            var execution = new WorkflowExecutionState("already-running", fixture.Material, WorkflowExecutionStatus.Running, null, Now, Now, Now,
                null, null, null, "actual-tenant", new Dictionary<string, string>()) { Partition = new("physical"), RunKind = WorkflowRunKind.PublishedRun };
            await fixture.Executions.SaveAsync(execution);
            fixture.Bookmark = new("wait", "already-running", "activity", "node", "resume", "http", "route", null, new Dictionary<string, string>(), Now, null);
            await fixture.Bookmarks.SaveAsync(fixture.Bookmark);
            var observation = (await fixture.Bookmarks.ObserveAsync("already-running", "wait"))!;
            var expected = StimulusResumeRecipient.From(observation, execution, fixture.Scope);
            fixture.Wait = new(new(1, fixture.Scope, source.OutboxItemId, "already-running", "wait", observation.Generation), expected);
            await fixture.Activities.SaveAsync(new(new("activity", "already-running", "node", "authored", "test", "1"), ActivityExecutionStatus.Suspended,
                null, 1, Now, Now, null, null, null, null, null, ActivitySchedulingProvenance.Empty, null, ["wait"], [], 0, 0, new Dictionary<string, string>()));
            var candidate = new RuntimeIngressWaitCandidate("http", "route", JsonDocument.Parse("{\"number\":18446744073709551615}").RootElement,
                new Dictionary<string, string>(), new("Test.Wait", schemaVersion: 1), "test");
            var waits = new RuntimeIngressWaitDelivery(1, source.OutboxItemId, source.Fingerprint, fixture.Scope, Now, candidate, [fixture.Wait],
                [new(fixture.Wait.WaitId, RuntimeIngressWaitPhase.Pending)]);
            var delivery = new RuntimeSubscriptionDelivery(1, source.OutboxItemId, source.Fingerprint, fixture.Scope, [fixture.Root], [],
                [new("root", RuntimeRecipientPhase.Pending)], waitDelivery: waits);
            fixture.Item = RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, delivery, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));
            var evidence = new NativeIngressWaitTargetEvidence(fixture.Queue, fixture.Executions, fixture.Activities, fixture.Bookmarks, fixture.Clock);
            fixture.Target = new(fixture.actors, fixture.Ingress, evidence, new Access(fixture.Scope));
            return fixture;
        }
        internal void Seal(RuntimeIngressWaitDispatchSeal seal)
        {
            var delivery = Item.SubscriptionDelivery!;
            var waits = delivery.WaitDelivery!;
            Item = Item.WithSubscriptionDelivery(delivery.WithWaitDelivery(waits.WithProgress(waits.Progress.Single().CommitDispatch(seal))));
            Ingress.Current = new(Item, Wait, seal);
        }
        public void Dispose() => actors.Dispose();
    }
    private sealed class Access(PersistenceScope scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(scope); }
    private sealed class Ingress : IRuntimeIngressDeliveryStore
    {
        internal RuntimeCommittedIngressWait? Current;
        public ValueTask<RuntimeCommittedIngressWait> ReadCommittedWaitAsync(RuntimeIngressWaitOrigin origin, CancellationToken cancellationToken = default) =>
            Current is { } actual && actual.Seal.Origin == origin ? ValueTask.FromResult(actual) :
                ValueTask.FromException<RuntimeCommittedIngressWait>(new InvalidDataException("There is no matching physical wait seal."));
    }
    private sealed class Executor : IWorkflowExecutionCommandExecutor
    {
        internal int Calls; internal WorkflowExecutionCommandDispatchOptions? Options;
        public ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default) =>
            ProcessAsync(envelope, WorkflowExecutionCommandDispatchOptions.Default, cancellationToken);
        public ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, WorkflowExecutionCommandDispatchOptions options, CancellationToken cancellationToken = default)
        { Calls++; Options = options; return ValueTask.FromResult(WorkflowExecutionCommandProcessResult.NoDrain); }
    }
}
