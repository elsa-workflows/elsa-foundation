using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Constants;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class StimulusDeliverySchemaGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 1, 0, 0, TimeSpan.Zero);
    private static readonly PersistenceScope Scope = new("tenant");
    private const string Family = RuntimePostCommitOutboxEfModule.SchemaFamily;

    [Fact]
    public async Task Actual_admitted_gate_preserves_v1_ordinary_checkpoint_and_delivery_but_refuses_protocol()
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Open();
        await new EfRuntimeCheckpointCommitStore(context, new Accessor()).CommitAsync(Commit(Pending("ordinary", false)), new(RuntimeCheckpointPersistenceMode.Immediate));
        var store = new EfRuntimePostCommitOutboxStore(context, new Accessor());
        var claim = Assert.Single(await store.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 1)));
        Assert.NotNull(await store.RenewClaimAsync(claim, Now.AddSeconds(1), TimeSpan.FromMinutes(2)));
        await store.CompleteClaimAsync(new(claim, new(claim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(2))));
        var row = await context.RuntimePostCommitOutbox.AsNoTracking().SingleAsync();
        Assert.Equal("1.0.0", row.SchemaVersion);
        Assert.DoesNotContain("recipientSnapshot", row.ContentJson);
        Assert.DoesNotContain("stimulusDeliveryProtocolVersion", row.ContentJson);
        Assert.DoesNotContain("stimulusDeliverySourceScope", row.ContentJson);
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new Accessor()).CommitAsync(Commit(Pending("new-protocol", true)), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        await using var verify = fixture.Open();
        Assert.Single(await verify.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Single(await verify.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Equal("1.0.0", fixture.Gate.StateOf(Family)!.WriteVersion);
    }

    [Fact]
    public async Task Same_host_observes_supported_finalization_and_writes_v2_without_restart()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Finalize();
        await using var context = fixture.Open();
        await new EfRuntimeCheckpointCommitStore(context, new Accessor()).CommitAsync(Commit(Pending("new-protocol", true)), new(RuntimeCheckpointPersistenceMode.Immediate));
        var row = await context.RuntimePostCommitOutbox.AsNoTracking().SingleAsync();
        Assert.Equal("2.0.0", row.SchemaVersion);
        var source = (await new EfRuntimePostCommitOutboxStore(context, new Accessor()).FindAsync("new-protocol:send"))!;
        Assert.Equal(1, source.StimulusDeliveryProtocolVersion); Assert.Equal(Scope, source.StimulusDeliverySourceScope);
        Assert.Null(source.RecipientSnapshot); // protected before first selection, not merely after install.
        Assert.Equal("2.0.0", fixture.Gate.StateOf(Family)!.WriteVersion);
    }

    [Fact]
    public async Task Generic_required_source_scope_needs_finalized_v2_without_declaring_recipient_protocol()
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Open();
        var original = Pending("generic", false);
        var scoped = new RuntimePostCommitOutboxItem(original.OutboxItemId, original.Intent, original.Status, original.RecordedAt,
            original.AvailableAt, original.RetryPolicy, 0, null, null, null, null, null, 0, null, null, Scope, null);
        var store = new EfRuntimePostCommitOutboxStore(context, new Accessor());
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => store.SavePendingAsync(scoped).AsTask());
        Assert.Empty(await context.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync());
        await fixture.Finalize();
        await store.SavePendingAsync(scoped);
        var saved = (await store.FindDurableAsync(scoped.OutboxItemId))!;
        Assert.Equal(Scope, saved.StimulusDeliverySourceScope);
        Assert.Null(saved.StimulusDeliveryProtocolVersion); Assert.Null(saved.RecipientSnapshot);
        Assert.Equal("2.0.0", (await context.RuntimePostCommitOutbox.AsNoTracking().SingleAsync()).SchemaVersion);
    }

    [Fact]
    public async Task Actual_backfill_uses_declared_checked_rewriter_and_preserves_legacy_absence()
    {
        await using var fixture = await Fixture.Create();
        string physical;
        await using (var seed = fixture.Open())
        {
            await new EfRuntimePostCommitOutboxStore(seed, new Accessor()).SavePendingAsync(Pending("legacy", false));
            physical = (await seed.RuntimePostCommitOutbox.AsNoTracking().SingleAsync()).Id;
        }
        await fixture.Finalize();
        var backfill = new EfSchemaBackfill(fixture.Gate, fixture.Fleet, new() { BatchPause = TimeSpan.Zero, SettleMargin = TimeSpan.Zero });
        await backfill.RunOnceAsync(async (action, cancellation) =>
        {
            await using var context = fixture.Open();
            using var services = new ServiceCollection().AddSingleton<RuntimeDbContext>(context).BuildServiceProvider();
            await action(new(services, context));
        });
        await using var verify = fixture.Open();
        var row = await verify.RuntimePostCommitOutbox.AsNoTracking().SingleAsync();
        Assert.Equal(physical, row.Id); Assert.Equal("2.0.0", row.SchemaVersion); Assert.Equal(2, row.Revision);
        var item = (await new EfRuntimePostCommitOutboxStore(verify, new Accessor()).FindAsync("legacy:send"))!;
        Assert.Null(item.StimulusDeliveryProtocolVersion); Assert.Null(item.StimulusDeliverySourceScope); Assert.Null(item.RecipientSnapshot);
    }

    [Fact]
    public async Task Newer_row_is_never_downgraded_while_current_gate_still_observes_v1()
    {
        await using var fixture = await Fixture.Create();
        // A separate direct provider models a row already committed by a newer finalized peer. This host has stale
        // observations; it must confirm finalization before rewriting, rather than lowering that row's stamp.
        await using (var newer = fixture.Open(ungated: true))
            await new EfRuntimePostCommitOutboxStore(newer, new Accessor()).SavePendingAsync(Pending("peer", false));
        await using var context = fixture.Open();
        var store = new EfRuntimePostCommitOutboxStore(context, new Accessor());
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() =>
            store.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 1)).AsTask());
        await using var verify = fixture.Open();
        var row = await verify.RuntimePostCommitOutbox.AsNoTracking().SingleAsync();
        Assert.Equal("2.0.0", row.SchemaVersion); Assert.Equal(1, row.Revision);
    }

    [Fact]
    public async Task Scheduler_family_hold_preserves_native_v1_and_refuses_model_before_any_write()
    {
        const string schedulerFamily = "RuntimeSchedulerWork";
        await using var fixture = await Fixture.Create(schedulerFamily);
        await using var context = fixture.Open();
        var store = new EfSchedulerWorkQueueStore(context, new Accessor(), new NoTokens());
        RuntimeSchedulerWorkItem Input(string id, WorkflowExecutionCommandKind kind) =>
            new(id, "execution", id, kind, id, id, Now, Now);
        await store.EnqueueAsync(Input("native", WorkflowExecutionCommandKind.RunSchedulerWork));
        Assert.Equal("1.0.0", (await context.SchedulerWorkItems.AsNoTracking().SingleAsync()).SchemaVersion);
        var claim = await store.ClaimAsync(new("execution", "owner", Now, TimeSpan.FromMinutes(1)));
        Assert.NotNull(claim);
        Assert.True((await store.CompleteClaimAsync(claim)).Succeeded);
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() =>
            store.EnqueueAsync(Input("model", WorkflowExecutionCommandKind.ExecuteModel)).AsTask());
        await using (var verify = fixture.Open()) Assert.Empty(await verify.SchedulerWorkItems.ToArrayAsync());
        await fixture.Finalize();
        await store.EnqueueAsync(Input("model", WorkflowExecutionCommandKind.ExecuteModel));
        Assert.Equal(RuntimeSchedulerWorkEfModule.SchemaVersion, (await context.SchedulerWorkItems.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Equal("1.0.0", fixture.Gate.StateOf(RuntimeOperationalStateEfModule.SchemaFamily)!.WriteVersion);
    }

    private sealed class NoTokens : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public byte[] Decode(string purpose, string token) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_resume_header_touch_uses_the_admitted_version_without_rewriting_native_content(bool finalize)
    {
        await using var fixture = await Fixture.Create(RuntimeWorkflowExecutionEfModule.SchemaFamily);
        var pin = new WorkflowExecutableIdentity("artifact", "definition", "version", "1.0.0", "hash");
        var workflow = new WorkflowExecutionState("target", pin, WorkflowExecutionStatus.Running, null,
            Now, Now, Now, null, null, null, Scope.Value, new Dictionary<string, string>())
            { RunKind = WorkflowRunKind.PublishedRun, Partition = new(Scope.Value) };
        var wait = new BookmarkState("wait", "target", "activity", "node", "resume", "Event", "named", null,
            new Dictionary<string, string>(), Now, null);
        var suspended = new ActivityExecutionState(new("activity", "target", "node", "node", "Test.Activity", "1"),
            ActivityExecutionStatus.Suspended, null, 1, Now, Now, null, null, null, null, null,
            ActivitySchedulingProvenance.From("target", null, null, null, null, null, null, "fixture"), null,
            ["wait"], [], 0, 0, new Dictionary<string, string>());
        string content; long originalRevision;
        await using (var seed = fixture.Open())
        {
            await new EfWorkflowExecutionStateStore(seed, new Accessor(), new NoTokens()).SaveAsync(workflow);
            await new EfActivityExecutionStateStore(seed, new Accessor(), new NoTokens()).SaveAsync(suspended);
            await new EfBookmarkStateStore(seed, new Accessor()).SaveAsync(wait);
            var row = await seed.WorkflowExecutionStates.AsNoTracking().SingleAsync();
            Assert.Equal("1.0.0", row.SchemaVersion); content = row.ContentJson; originalRevision = row.Revision;
        }
        if (finalize) await fixture.Finalize();
        await using var context = fixture.Open();
        var bookmark = new EfBookmarkStateStore(context, new Accessor());
        var expected = StimulusResumeRecipient.From((await bookmark.ObserveAsync("target", "wait"))!, workflow, Scope);
        context.ChangeTracker.Clear();
        var claim = suspended with { Status = ActivityExecutionStatus.Running,
            Attempts = [new("claim", "activity", 1, ActivityAttemptReason.Resume, Now)],
            Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.ActivityAttemptActivationClaim] = "claim" } };
        var commit = new RuntimeCheckpointCommit("resume", new("resume", "ExactBookmarkClaim", "target", Now, ["activity"], new Dictionary<string, string>()),
            new(null, null, [new("activity", RuntimeStateChangeOperation.Upsert, claim, new Dictionary<string, string>())],
                [new("wait", RuntimeStateChangeOperation.Delete, wait, new Dictionary<string, string>())], [], [], []),
            [], new Dictionary<string, string>()) { ExpectedBookmarkResume = expected, RetainSchedulerWork = true };
        await new EfRuntimeCheckpointCommitStore(context, new Accessor()).CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
        var written = await context.WorkflowExecutionStates.AsNoTracking().SingleAsync();
        Assert.Equal(finalize ? "2.0.0" : "1.0.0", written.SchemaVersion);
        Assert.Equal(content, written.ContentJson); Assert.Equal(checked(originalRevision + 1), written.Revision);
        Assert.Null(await bookmark.FindAsync("target", "wait"));
        Assert.Equal(ActivityExecutionStatus.Running, (await new EfActivityExecutionStateStore(context, new Accessor(), new NoTokens()).FindAsync("target", "activity"))!.Status);
    }

    [Fact]
    public async Task Scheduler_peer_v2_row_is_not_downgraded_by_a_host_still_observing_v1()
    {
        await using var fixture = await Fixture.Create("RuntimeSchedulerWork");
        await using (var peer = fixture.Open(ungated: true))
            await new EfSchedulerWorkQueueStore(peer, new Accessor(), new NoTokens()).EnqueueAsync(
                new("model", "execution", "command", WorkflowExecutionCommandKind.ExecuteModel, "envelope", "key", Now, Now));
        await using var context = fixture.Open();
        var store = new EfSchedulerWorkQueueStore(context, new Accessor(), new NoTokens());
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() =>
            store.ClaimAsync(new("execution", "owner", Now, TimeSpan.FromMinutes(1))).AsTask());
        await using var verify = fixture.Open();
        var row = await verify.SchedulerWorkItems.AsNoTracking().SingleAsync();
        Assert.Equal("2.0.0", row.SchemaVersion);
        Assert.Equal(1, row.Revision);
        Assert.Equal(0, row.ClaimToken);
    }

    private static RuntimePostCommitOutboxItem Pending(string commit, bool protocol) => new(commit + ":send",
        new("send", "publisher", "publish", Now, null, "key", null), RuntimePostCommitOutboxStatus.Pending,
        Now, Now, null, 0, null, null, null, null, null, 0, null, protocol ? 1 : null, protocol ? Scope : null, null);
    private static RuntimeCheckpointCommit Commit(RuntimePostCommitOutboxItem item) => new(item.OutboxItemId + ":commit",
        new("checkpoint", "EmptyCheckpoint", "publisher", Now, [], new Dictionary<string, string>()),
        new(null, null, [], [], [], [], [], postCommitOutbox: [new(item.OutboxItemId, RuntimeStateChangeOperation.Upsert, item, new Dictionary<string, string>())]),
        [], new Dictionary<string, string>());
    private sealed class Accessor : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Scope); }
    private sealed class SingleCurrentMember : IEfSchemaFleet
    {
        public EfSchemaFleetStanding GetLocalStanding() => new(new("host", "incarnation"), false);
        public ValueTask PublishAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<EfSchemaFleetAnswer> CountAsync(string family, string version, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public ValueTask<EfSchemaFleetAnswer> CountObservingAsync(string family, IReadOnlyList<string> versions, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public TimeSpan SettleMargin => TimeSpan.Zero;
        public IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly EfSchemaFinalizationGates gates = new();
        private readonly ServiceProvider services;
        public IEfSchemaFleet Fleet { get; } = new SingleCurrentMember();
        public EfSchemaModuleGate Gate { get; }
        private readonly string family;
        private Fixture(string family)
        {
            this.family = family;
            services = new ServiceCollection().AddSingleton(gates).BuildServiceProvider();
            Gate = new(EfSchemaModuleFamilies.ForContext(typeof(RuntimeDbContext))!, Fleet,
                new EfSchemaFinalizationObservations(), new());
        }
        public static async Task<Fixture> Create(string family = Family)
        {
            var fixture = new Fixture(family); await fixture.connection.OpenAsync();
            await using var context = fixture.Open(); await context.Database.EnsureCreatedAsync();
            var store = new EfSchemaFinalizationStore(context);
            var record = await store.GetOrCreateAsync(family, "1.0.0", ["1.0.0", "2.0.0"], SchemaFinalizationActor.OfOperator("test"));
            await store.PlaceHoldAsync(family, record.Revision, null, "controlled v1 rollout", "test", ["1.0.0", "2.0.0"]);
            await fixture.Gate.ActivateAsync(context); fixture.gates.Register(typeof(RuntimeDbContext), fixture.Gate);
            return fixture;
        }
        public RuntimeSqliteDbContext Open(bool ungated = false)
        {
            var builder = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection);
            if (!ungated) { builder.UseApplicationServiceProvider(services); EfSchemaWriteGateInterceptor.EnsureAdded(builder); }
            return new(builder.Options);
        }
        public async Task Finalize()
        {
            await using var context = Open(); var store = new EfSchemaFinalizationStore(context);
            var record = (await store.FindAsync(family))!;
            await store.ReleaseHoldAsync(family, record.Revision, null, "test");
            await Gate.EvaluateAsync(context); await Gate.RefreshAsync(context);
            Assert.Equal(Gate.Families.Chains.Single(chain => chain.Family == family).CurrentVersion, Gate.StateOf(family)!.WriteVersion);
        }
        public async ValueTask DisposeAsync() { await services.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
