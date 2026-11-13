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

    private static RuntimeSchedulerWorkItem StartItem(WorkflowExecutionCommandEnvelope envelope) => new(envelope.EnvelopeId,
        envelope.WorkflowExecutionId, envelope.Command.CommandId, envelope.Command.Kind, envelope.EnvelopeId,
        envelope.IdempotencyKey, envelope.EnqueuedAt, envelope.EnqueuedAt.AddSeconds(1), envelope.Sequence,
        envelope.Command.Payload, envelope.Command.Metadata, envelope.Metadata);

    private sealed class Fixture : IDisposable
    {
        internal readonly FakeTimeProvider Clock = new(Now);
        internal readonly InMemoryWorkflowExecutableStore Executables = new();
        internal readonly InMemoryWorkflowSchedulerWorkQueue Queue = new();
        internal readonly InMemoryWorkflowExecutionStateStore Executions = new();
        internal readonly WitnessExecutor Executor = new();
        internal readonly FakeIngress Ingress = new();
        internal readonly InProcessWorkflowExecutionActorProvider Actors;
        internal RuntimePostCommitOutboxItem Item = null!;
        internal RuntimeSubscriptionRecipient Recipient = null!;
        internal NativeStandaloneTriggerRuntimeTarget Target = null!;
        internal WorkflowStartSchedulerWorkHandler StartHandler = null!;
        internal WorkflowCheckpointSchedulerWorkHandler CheckpointHandler = null!;
        private Fixture() => Actors = new(Executor);
        internal static async Task<Fixture> CreateAsync()
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
            var fingerprint = RuntimeStandaloneIngressSource.ComputeRequestFingerprint(scope, "tenant", "trigger", "test", "occurrence", schema, payload, authority);
            var source = new RuntimeStandaloneIngressSource(1, "admission", "dispatch", Now, scope, "tenant", partition,
                "trigger", 1, 1, "test", "occurrence", fingerprint, schema, payload, RuntimeIngressAdmissionPolicy.RequireAll, authority);
            fixture.Recipient = new("recipient", "subscription", 1, 1, "definition", "native", "slot", 1, "activation", identity,
                new("reference", "Published", "publication", "1", "definition", "version", "1", "activation", "slot"),
                RuntimeIngressAdmission.ComputeInputContractFingerprint(contract), 1, 1,
                new Dictionary<string, JsonElement> { ["AccountId"] = JsonSerializer.SerializeToElement("business-value") }, authority,
                "tenant", partition, new(1, scope, source.OutboxItemId, "subscription"));
            var delivery = new RuntimeSubscriptionDelivery(1, source.OutboxItemId, source.Fingerprint, scope, [fixture.Recipient], [],
                [new(fixture.Recipient.RecipientId, RuntimeRecipientPhase.Pending)]);
            fixture.Item = RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, delivery, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));
            var evidence = new NativeIngressTargetEvidence(fixture.Queue, fixture.Executions, fixture.Clock, executables: fixture.Executables);
            fixture.Target = new(fixture.Executables, fixture.Actors, fixture.Ingress, new WorkflowExecutableInputValidator(new StringTypes()),
                new AllowWorkflowExecutableStartPolicy(), evidence, new Access(scope));
            fixture.StartHandler = new(fixture.Executables, fixture.Queue, new RuntimeExecutionIdGenerator(fixture.Clock), fixture.Clock,
                fixture.Executions, null, null, null, null, fixture.Ingress);
            var activities = new InMemoryActivityExecutionStateStore();
            var checkpointStore = new InMemoryRuntimeCheckpointCommitStore(fixture.Executables,
                new InMemoryWorkflowExecutableSourceReferenceStore(fixture.Executables, fixture.Clock), fixture.Executions, activities,
                durableValueStateStore: new InMemoryDurableValueStateStore(), operationalStateStore: fixture.Executions.ExecutionLivenessStateStore,
                state: fixture.Executions.CheckpointState, timeProvider: fixture.Clock, persistenceAccessContextAccessor: new Access(scope),
                rootWriteLeaseManager: new WorkflowExecutableRootWriteLeaseManager(fixture.Executables,
                    Options.Create(new WorkflowExecutableGarbageCollectionOptions()), fixture.Clock));
            var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), checkpointStore,
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
