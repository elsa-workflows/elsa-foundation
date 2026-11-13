using System.Reflection;
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Triggers;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfIngressCheckpointAuthorityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Physical_ingress_checkpoint_and_claimed_continuation_preserve_tenant_and_immutable_origin(bool distinctTenant)
    {
        var now = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var scope = new PersistenceScope("physical-shell");
        var tenant = distinctTenant ? "logical-tenant" : scope.Value;
        var partition = new WorkflowExecutionPartition(scope.Value);
        var access = new Access(scope);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var materials = new EfWorkflowExecutableStore(context, access);
        var identity = new WorkflowExecutableIdentity("ingress-material", "producer", "producer-v1", "1", "hash");
        var root = new ExecutableNode("root", "authored-root", "test", "1", "test", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var contract = new WorkflowExecutableInputContract(1, []);
        await materials.SaveAsync(new(identity, root, new Dictionary<string, WorkflowExecutableResumeTarget>(), now,
            new Dictionary<string, string>(), contract, [], IncidentStrategyBuiltIns.FaultReference));
        var queue = new EfSchedulerWorkQueueStore(context, access, new AdmissionCodec());
        var executions = new EfWorkflowExecutionStateStore(context, access, new AdmissionCodec());
        var leases = new WorkflowExecutableRootWriteLeaseManager(materials, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock);
        var sources = new EfWorkflowExecutableSourceReferenceStore(context, access, new AdmissionCodec(), clock);
        var outbox = new EfRuntimePostCommitOutboxStore(context, access, clock, sources, leases);
        var reader = new PhysicalSealedSourceReader(outbox);
        var ordinary = new WorkflowExecutionState("ordinary", identity, WorkflowExecutionStatus.Running, null, now, now,
            now, null, null, null, tenant, new Dictionary<string, string>());
        if (distinctTenant)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => executions.SaveAsync(ordinary).AsTask());
            Assert.Empty(await context.WorkflowExecutionStates.AsNoTracking().ToArrayAsync());
        }
        var actor = new WorkflowExecutionAuthoritySnapshot("standalone-trigger", "source-author");
        var schema = new ValueTypeDescriptor("Test.Payload", schemaVersion: 1);
        var input = JsonSerializer.SerializeToElement(new { });
        var fingerprint = RuntimeStandaloneIngressSource.ComputeRequestFingerprint(scope, tenant, "trigger", "test", "occurrence", schema, input, actor);
        var source = new RuntimeStandaloneIngressSource(1, "admission", "dispatch", now, scope, tenant, partition,
            "trigger", 1, 1, "test", "occurrence", fingerprint, schema, input, RuntimeIngressAdmissionPolicy.RequireAll, actor);
        var recipient = new RuntimeSubscriptionRecipient("recipient", "subscription", 1, 1, "logical-definition", "native", "slot", 1,
            "publication", identity, new("reference", "Published", "publication", "1", "logical-definition", "logical-v1", "1", "publication", "slot"),
            RuntimeIngressAdmission.ComputeInputContractFingerprint(contract), 1, 1, new Dictionary<string, JsonElement>(), actor,
            tenant, partition, new(1, scope, source.OutboxItemId, "subscription"));
        var delivery = new RuntimeSubscriptionDelivery(1, source.OutboxItemId, source.Fingerprint, scope, [recipient], [],
            [new(recipient.RecipientId, RuntimeRecipientPhase.Pending)]);
        var accepted = RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, delivery, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));
        using var actors = new InProcessWorkflowExecutionActorProvider(new UnusedExecutor());
        var target = new NativeStandaloneTriggerRuntimeTarget(materials, actors, reader, new WorkflowExecutableInputValidator(new EmptyTypes()),
            new AllowWorkflowExecutableStartPolicy(), new NativeIngressTargetEvidence(queue, executions, clock, executables: materials), access);
        var seal = await target.PrepareDispatchAsync(accepted, recipient);
        accepted = accepted.WithSubscriptionDelivery(delivery.WithProgress(Assert.Single(delivery.Progress).CommitDispatch(seal)));
        // A direct physical accepted-row fixture exercises the conditional writer, not source admission or HTTP acceptance.
        var rowId = (string)typeof(EfRuntimePostCommitOutboxStore).GetMethod("RowId", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [scope.Value, accepted.OutboxItemId])!;
        context.RuntimePostCommitOutbox.Add((RuntimePostCommitOutboxEntity)typeof(EfRuntimePostCommitOutboxStore)
            .GetMethod("ToEntity", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
                [accepted, scope.Value, rowId, 1L, RuntimePostCommitOutboxEfModule.SchemaVersion])!);
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var checkpoints = new EfRuntimeCheckpointCommitStore(context, access, clock, leases);
        var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), checkpoints,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []);
        var handler = new WorkflowCheckpointSchedulerWorkHandler(new EfActivityExecutionStateStore(context, access, new AdmissionCodec()), committer, null, clock,
            executions, materials, null, null, reader);
        var envelope = NativeIngressTargetEvidence.ReadEnvelope(seal);
        var start = new RuntimeSchedulerWorkItem(envelope.EnvelopeId, envelope.WorkflowExecutionId, envelope.Command.CommandId,
            envelope.Command.Kind, envelope.EnvelopeId, envelope.IdempotencyKey, envelope.EnqueuedAt, now, envelope.Sequence,
            envelope.Command.Payload, envelope.Command.Metadata, envelope.Metadata);
        var initial = NativeIngressStartLineage.Build(start, envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!, root.ExecutableNodeId);
        await handler.HandleAsync(initial);
        var prior = (await executions.FindAsync(recipient.StartIdentity.WorkflowExecutionId))!;
        Assert.Equal(tenant, prior.TenantId);
        if (distinctTenant) Assert.NotEqual(scope.Value, prior.TenantId);
        Assert.Equal(scope, prior.IngressOrigin!.Scope);
        var work = new RuntimeSchedulerWorkItem("continuation", prior.WorkflowExecutionId, "continue", WorkflowExecutionCommandKind.RunSchedulerWork,
            "continue-envelope", "continue-key", now, now);
        await queue.EnqueueAsync(work);
        var claim = (await queue.ClaimAsync(new(prior.WorkflowExecutionId, "worker", now, TimeSpan.FromMinutes(1))))!;
        var stripped = new RuntimeCheckpointCommit("stripped-commit", new("stripped-checkpoint", RuntimeCheckpointNames.PostCommitIntentRecorded,
            prior.WorkflowExecutionId, now, [], new Dictionary<string, string>()),
            new(new(prior.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, prior with { IngressOrigin = null }, new Dictionary<string, string>()),
                null, [], [], [], [], [], null, null, null, null, null, [ConsumedSchedulerWorkItem.FromClaim(claim)]), [], new Dictionary<string, string>());
        if (distinctTenant)
            await Assert.ThrowsAsync<InvalidOperationException>(() => checkpoints.CommitAsync(stripped, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => checkpoints.CommitAsync(stripped, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Equal(1, await context.RuntimeCheckpointCommits.CountAsync());
        Assert.True(prior.IngressOrigin.IsEquivalentTo((await executions.FindAsync(prior.WorkflowExecutionId))!.IngressOrigin));
        Assert.Equal(work.WorkItemId, Assert.Single((await queue.ListAsync(new(prior.WorkflowExecutionId))).Items).WorkItemId);
        var continuation = new RuntimeCheckpointCommit("continuation-commit", new("continuation-checkpoint", RuntimeCheckpointNames.PostCommitIntentRecorded,
            prior.WorkflowExecutionId, now, [], new Dictionary<string, string>()),
            new(new(prior.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, prior with { UpdatedAt = now.AddSeconds(1) }, new Dictionary<string, string>()),
                null, [], [], [], [], [], null, null, null, null, null, [ConsumedSchedulerWorkItem.FromClaim(claim)]), [], new Dictionary<string, string>());
        await checkpoints.CommitAsync(continuation, new(RuntimeCheckpointPersistenceMode.Immediate));
        var current = (await executions.FindAsync(prior.WorkflowExecutionId))!;
        Assert.Equal(tenant, current.TenantId); Assert.True(prior.IngressOrigin.IsEquivalentTo(current.IngressOrigin));
        Assert.Empty((await queue.ListAsync(new(prior.WorkflowExecutionId))).Items);
        Assert.Equal(2, await context.RuntimeCheckpointCommits.CountAsync());
        // The accepted workflow can still publish an ordinary saved-cohort event without independent subscribers.
        var eventSource = new RuntimePostCommitOutboxItem("workflow-event:send",
            new("send", current.WorkflowExecutionId, "publish", now, "activity", "event-key", JsonSerializer.SerializeToElement("event")),
            RuntimePostCommitOutboxStatus.Pending, now, now, new(4, TimeSpan.FromSeconds(1)), 0,
            null, null, null, null, null, 0, null, 1, scope, null);
        await outbox.SavePendingAsync(eventSource);
        var eventClaim = Assert.Single((await ((IRuntimePostCommitOutboxClaimStore)outbox).ClaimAsync(
            new("publisher", now, TimeSpan.FromMinutes(1), 10, workflowExecutionId: current.WorkflowExecutionId)))
            .Where(item => item.OutboxItemId == eventSource.OutboxItemId));
        var eventContext = new PostCommitDeliveryContext(eventClaim, scope);
        var cohort = new StimulusDeliverySnapshot(1, eventContext.SourceOutboxItemId, eventContext.IntentFingerprint,
            StimulusDeliveryScope.From(current, scope), now, "event", "named", [], [], StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion);
        var installed = await ((IStimulusDeliveryStore)outbox).InstallAsync(eventContext, cohort);
        Assert.Equal(tenant, installed.Scope.TenantId); Assert.Equal(scope, installed.Scope.PersistenceScope);
        var reopened = await ((IStimulusDeliveryStore)outbox).ReadSourceAsync(eventContext);
        Assert.True(installed.IsEquivalentTo(reopened.Claim.Item.RecipientSnapshot));
    }

    private sealed class Access(PersistenceScope scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(scope); }
    private sealed class PhysicalSealedSourceReader(IPostCommitOutboxLookupStore lookup) : IRuntimeIngressDeliveryStore
    {
        public async ValueTask<RuntimeCommittedRecipient> ReadCommittedRecipientAsync(RuntimeIngressCommandOrigin origin, CancellationToken cancellationToken = default) =>
            RuntimeIngressAdmissionValidation.Committed((await lookup.FindDurableAsync(origin.SourceOutboxItemId, cancellationToken))!, origin);
    }
    private sealed class UnusedExecutor : IWorkflowExecutionCommandExecutor
    {
        public ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<WorkflowExecutionCommandProcessResult>(new InvalidOperationException("This checkpoint fixture never dispatches an actor."));
    }
    private sealed class EmptyTypes : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias) { alias = ""; return false; }
        public bool TryGetType(string alias, out Type type) { type = typeof(object); return false; }
        public IEnumerable<Type> ListTypes() => [];
        public string GetAliasOrDefault(Type type) => type.Name;
        public Type GetTypeOrDefault(string alias) => typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type) => TryGetType(alias, out type);
    }
}
