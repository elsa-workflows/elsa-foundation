using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Tests;

public sealed class SelectedTestTransportTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(WorkflowExecutionCommandKind.Start)]
    [InlineData(WorkflowExecutionCommandKind.Checkpoint)]
    public async Task Selected_admission_is_format_fenced_before_a_predecessor_reader_can_decode_or_lease_it(WorkflowExecutionCommandKind kind)
    {
        await using var fixture = await Fixture.CreateAsync(gated: false);
        await using var context = fixture.Open();
        var transport = fixture.Transport(context);
        await transport.SendAsync("execution", Start(Entry(), kind: kind), Now);
        var row = await context.CommandTransportItems.AsNoTracking().SingleAsync();
        var head = await context.CommandStreamHeads.AsNoTracking().SingleAsync();
        Assert.Equal("2.0.0", row.SchemaVersion);
        Assert.Equal("2.0.0", head.SchemaVersion);
        // The predecessor store checks this exact shared reader contract before its identity/decode/lease path.
        var predecessor = EfSchemaChain.For(new(ExecutionCommandTransportEfModule.SchemaFamily,
            "Workflows.Runtime.Distributed.CommandTransport", "1.0.0", typeof(ExecutionCommandTransportEfModule).Assembly));
        var decoded = false;
        var claimed = false;
        Assert.Throws<EfSchemaVersionSkewException>(() =>
        {
            EfSchemaVersion.EnsureReadable(predecessor, head.SchemaVersion);
            EfSchemaVersion.EnsureReadable(predecessor, row.SchemaVersion);
            decoded = JsonSerializer.Deserialize<ExecutionCommandTransportItem>(row.PayloadJson, Json) is not null;
            claimed = true;
        });
        Assert.False(decoded);
        Assert.False(claimed);
        Assert.Null(row.LeaseOwnerId);
        Assert.Equal(1, row.Revision);
    }

    [Fact]
    public async Task Owned_transport_restart_preserves_entry_and_never_downgrades_continuations_leases_release_or_empty_head()
    {
        await using var fixture = await Fixture.CreateAsync(gated: false);
        var entry = Entry();
        await using (var first = fixture.Open()) await fixture.Transport(first).SendAsync("execution", Start(entry), Now);
        string itemId;
        await using (var reopened = fixture.Open())
        {
            var transport = fixture.Transport(reopened);
            var leased = Assert.Single(await transport.LeaseAsync("execution", "node-a", Now, TimeSpan.FromMinutes(1), 1));
            itemId = leased.TransportItemId;
            var restored = leased.Envelope.Command.Payload!.Value.GetProperty("testEntry").Deserialize<WorkflowTestEntry>(Json)!;
            Assert.True(entry.Matches(restored));
            Assert.Equal("selected", restored.ExecutableNodeId);
            Assert.Equal("2.0.0", (await reopened.CommandTransportItems.AsNoTracking().SingleAsync()).SchemaVersion);
            Assert.True(await transport.ReleaseLeaseAsync("execution", itemId, "node-a", leased.LeaseToken!.Value, Now));
            Assert.Equal("2.0.0", (await reopened.CommandTransportItems.AsNoTracking().SingleAsync()).SchemaVersion);
        }
        await using (var survivor = fixture.Open())
        {
            var transport = fixture.Transport(survivor);
            var leased = Assert.Single(await transport.LeaseAsync("execution", "node-b", Now, TimeSpan.FromMinutes(1), 1));
            Assert.True(await transport.AckAsync("execution", itemId, "node-b", leased.LeaseToken!.Value, Now));
            Assert.Equal("2.0.0", (await survivor.CommandStreamHeads.AsNoTracking().SingleAsync()).SchemaVersion);
            await transport.SendAsync("execution", Control(), Now);
            Assert.Equal("2.0.0", (await survivor.CommandTransportItems.AsNoTracking().SingleAsync()).SchemaVersion);
            Assert.Equal("2.0.0", (await survivor.CommandStreamHeads.AsNoTracking().SingleAsync()).SchemaVersion);
        }
    }

    [Fact]
    public async Task A_predecessor_stamped_selected_payload_is_corrupt_and_cannot_be_claimed()
    {
        await using var fixture = await Fixture.CreateAsync(gated: false);
        await using var context = fixture.Open();
        await fixture.Transport(context).SendAsync("execution", Start(Entry()), Now);
        var row = await context.CommandTransportItems.SingleAsync();
        row.SchemaVersion = "1.0.0";
        await context.SaveChangesAsync();
        await using var reopened = fixture.Open();
        var error = await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport(reopened).LeaseAsync("execution", "node", Now, TimeSpan.FromMinutes(1), 1).AsTask());
        Assert.IsType<InvalidDataException>(error.InnerException);
        await using var verify = fixture.Open();
        var unchanged = await verify.CommandTransportItems.AsNoTracking().SingleAsync();
        Assert.Null(unchanged.LeaseOwnerId);
        Assert.Equal(1, unchanged.Revision);
    }

    [Fact]
    public async Task Ordinary_test_drive_with_scope_keeps_its_predecessor_stream_format()
    {
        await using var fixture = await Fixture.CreateAsync(gated: false);
        await using var context = fixture.Open();
        await fixture.Transport(context).SendAsync("execution", Start(Entry(), selected: false), Now);
        Assert.Equal("1.0.0", (await context.CommandTransportItems.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Equal("1.0.0", (await context.CommandStreamHeads.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Single(await fixture.Transport(context).LeaseAsync("execution", "node", Now, TimeSpan.FromMinutes(1), 1));
    }

    [Fact]
    public async Task Arbitrary_business_payload_names_do_not_declare_selected_entry_control()
    {
        await using var fixture = await Fixture.CreateAsync(gated: false);
        await using var context = fixture.Open();
        var envelope = new WorkflowExecutionCommandEnvelope("event-envelope", "execution",
            new("event", "execution", WorkflowExecutionCommandKind.GeneratedEvent, Now,
                JsonSerializer.SerializeToElement(new { testEntry = "business data", testContext = new { value = 1 } }), new Dictionary<string, string>()),
            "event", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, Now, partition: new("partition"));
        await fixture.Transport(context).SendAsync("execution", envelope, Now);
        Assert.Equal("1.0.0", (await context.CommandTransportItems.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Single(await fixture.Transport(context).LeaseAsync("execution", "node", Now, TimeSpan.FromMinutes(1), 1));
    }

    [Theory]
    [InlineData(WorkflowExecutionCommandKind.Start)]
    [InlineData(WorkflowExecutionCommandKind.Checkpoint)]
    public async Task Nested_business_input_names_do_not_declare_selected_entry_control(WorkflowExecutionCommandKind kind)
    {
        await using var fixture = await Fixture.CreateAsync(gated: false);
        await using var context = fixture.Open();
        var envelope = new WorkflowExecutionCommandEnvelope("ordinary-envelope", "execution",
            new("ordinary", "execution", kind, Now, JsonSerializer.SerializeToElement(new
            { inputs = new { testEntry = "business data", testContext = new { value = 1 } } }), new Dictionary<string, string>()),
            "ordinary", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, Now, partition: new("partition"));
        await fixture.Transport(context).SendAsync("execution", envelope, Now);
        Assert.Equal("1.0.0", (await context.CommandTransportItems.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Equal("1.0.0", (await context.CommandStreamHeads.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Single(await fixture.Transport(context).LeaseAsync("execution", "node", Now, TimeSpan.FromMinutes(1), 1));
    }

    [Fact]
    public async Task A_live_predecessor_gate_refuses_selected_append_and_claim_without_durable_mutation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var context = fixture.Open())
        {
            await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => fixture.Transport(context).SendAsync("execution", Start(Entry()), Now).AsTask());
            Assert.Empty(await context.CommandStreamHeads.AsNoTracking().ToArrayAsync());
            Assert.Empty(await context.CommandTransportItems.AsNoTracking().ToArrayAsync());
        }
        // A newer writer's row is visible, but a host still writing v1 cannot lease and downgrade it.
        await using (var peer = fixture.Open(ungated: true)) await fixture.Transport(peer).SendAsync("execution", Start(Entry()), Now);
        await using (var context = fixture.Open())
            await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => fixture.Transport(context).LeaseAsync("execution", "node", Now, TimeSpan.FromMinutes(1), 1).AsTask());
        await using var verify = fixture.Open();
        var unchanged = await verify.CommandTransportItems.AsNoTracking().SingleAsync();
        Assert.Equal("2.0.0", unchanged.SchemaVersion);
        Assert.Null(unchanged.LeaseOwnerId);
        Assert.Equal(1, unchanged.Revision);
    }

    [Fact]
    public async Task Provider_qualification_requires_the_actual_transport_context_scope_and_live_admitted_gate()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Open();
        var access = new Accessor("partition");
        var actual = fixture.Transport(context);
        var boundary = new EfExecutionCommandTransportTestBoundary(context, access, actual);
        Assert.False(boundary.CanTransportTestEntry(Entry(), actual));
        await fixture.FinalizeAsync();
        Assert.True(boundary.CanTransportTestEntry(Entry(), actual));
        Assert.False(boundary.CanTransportTestEntry(Entry(), fixture.Transport(context)));
        Assert.False(boundary.CanTransportTestEntry(Entry("other"), actual));
        await using var otherContext = fixture.Open();
        Assert.False(new EfExecutionCommandTransportTestBoundary(otherContext, access, actual).CanTransportTestEntry(Entry(), actual));
        await using var ungated = fixture.Open(ungated: true);
        var ungatedTransport = fixture.Transport(ungated);
        Assert.False(new EfExecutionCommandTransportTestBoundary(ungated, access, ungatedTransport).CanTransportTestEntry(Entry(), ungatedTransport));
        await actual.SendAsync("execution", Start(Entry()), Now);
        Assert.Equal("2.0.0", (await context.CommandTransportItems.AsNoTracking().SingleAsync()).SchemaVersion);
    }

    [Theory]
    [InlineData("boundary-privileged")]
    [InlineData("transport-privileged")]
    [InlineData("transport-other-scope")]
    [InlineData("tenant-mismatch")]
    [InlineData("tenant-whitespace")]
    public async Task Provider_qualification_refuses_privilege_or_tenant_scope_mismatch_on_the_actual_transport(string mismatch)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.FinalizeAsync();
        await using var context = fixture.Open();
        var ordinary = new Accessor("partition");
        IPersistenceAccessContextAccessor selectedAccess = mismatch == "transport-privileged" ? new PrivilegedAccessor() :
            mismatch == "transport-other-scope" ? new Accessor("other") : ordinary;
        var actual = new EfExecutionCommandTransport(context, selectedAccess);
        var boundary = new EfExecutionCommandTransportTestBoundary(context,
            mismatch == "boundary-privileged" ? new PrivilegedAccessor() : ordinary, actual);
        var entry = Entry();
        if (mismatch is "tenant-mismatch" or "tenant-whitespace") entry = new(entry.ContextId, entry.ContextFingerprint, entry.Executable,
            entry.ExecutableNodeId, new("scope", Now.AddMinutes(10), mismatch == "tenant-whitespace" ? " partition " : "other", new("partition")), entry.Authority);
        Assert.False(boundary.CanTransportTestEntry(entry, actual));
    }

    [Theory]
    [InlineData("append")]
    [InlineData("lease")]
    [InlineData("release")]
    [InlineData("ack")]
    [InlineData("mixed-format-ack")]
    public async Task A_predecessor_gate_refuses_mutations_of_a_protected_stream_without_changing_its_claim(string operation)
    {
        await using var fixture = await Fixture.CreateAsync();
        ExecutionCommandTransportItem lease;
        await using (var peer = fixture.Open(ungated: true))
        {
            var transport = fixture.Transport(peer);
            await transport.SendAsync("execution", Start(Entry()), Now);
            lease = Assert.Single(await transport.LeaseAsync("execution", "owner", Now, TimeSpan.FromMinutes(1), 1));
        }
        if (operation == "mixed-format-ack")
        {
            await using var peer = fixture.Open(ungated: true);
            await peer.CommandStreamHeads.ExecuteUpdateAsync(set => set.SetProperty(x => x.SchemaVersion, "1.0.0"));
        }
        await using var context = fixture.Open();
        var current = fixture.Transport(context);
        var before = await context.CommandStreamHeads.AsNoTracking().SingleAsync();
        Func<Task> mutate = operation switch
        {
            "append" => async () => { await current.SendAsync("execution", Control(), Now); },
            "lease" => async () => { await current.LeaseAsync("execution", "successor", Now.AddMinutes(2), TimeSpan.FromMinutes(1), 1); },
            "release" => async () => { await current.ReleaseLeaseAsync("execution", lease.TransportItemId, "owner", lease.LeaseToken!.Value, Now); },
            _ => async () => { await current.AckAsync("execution", lease.TransportItemId, "owner", lease.LeaseToken!.Value, Now); }
        };
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(mutate);
        await using var verify = fixture.Open();
        var saved = await verify.CommandTransportItems.AsNoTracking().SingleAsync();
        var head = await verify.CommandStreamHeads.AsNoTracking().SingleAsync();
        Assert.Equal("2.0.0", saved.SchemaVersion);
        Assert.Equal("owner", saved.LeaseOwnerId);
        Assert.Equal(lease.LeaseToken, saved.LeaseToken);
        Assert.Equal(before.Revision, head.Revision);
        Assert.Equal(before.PendingCount, head.PendingCount);
    }

    private sealed class PrivilegedAccessor : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.PrivilegedScoped(new("partition"), new("owned test")); }

    private static WorkflowTestEntry Entry(string partition = "partition") => new("context", "fingerprint",
        new("artifact", "definition", "version", "1.0.0", "sha256:exact"), "selected",
        new("scope", Now.AddMinutes(10), partition, new(partition)), new("tests", "tests"));

    private static WorkflowExecutionCommandEnvelope Start(WorkflowTestEntry entry, bool selected = true,
        WorkflowExecutionCommandKind kind = WorkflowExecutionCommandKind.Start)
    {
        // Transport-only protocol data. This slice does not add an authorized runtime start API.
        var payload = new { pinnedExecutable = entry.Executable, requestedArtifactId = entry.Executable.ArtifactId,
            runKind = WorkflowRunKind.TestRun, testScope = entry.Scope, testEntry = selected ? entry : null };
        return new("start-envelope", "execution", new("start", "execution", kind, Now,
            JsonSerializer.SerializeToElement(payload, Json), new Dictionary<string, string>()), "start", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, Now, partition: entry.Scope.Partition);
    }

    private static WorkflowExecutionCommandEnvelope Control() => new("control-envelope", "execution",
        new("cancel", "execution", WorkflowExecutionCommandKind.Cancel, Now, null, new Dictionary<string, string>()), "cancel", WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
        Now, partition: new("partition"));

    private sealed class Accessor(string scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new(scope)); }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TemporarySqliteDatabase database = new("selected-test-transport");
        private readonly EfSchemaFinalizationGates gates = new();
        private readonly ServiceProvider services;
        private readonly bool gated;
        public EfSchemaModuleGate Gate { get; }
        private Fixture(bool gated)
        {
            this.gated = gated;
            services = new ServiceCollection().AddSingleton(gates).BuildServiceProvider();
            Gate = new(EfSchemaModuleFamilies.ForContext(typeof(ExecutionCommandTransportDbContext))!, new SingleCurrentMember(),
                new EfSchemaFinalizationObservations(), new());
        }
        public static async Task<Fixture> CreateAsync(bool gated = true)
        {
            var fixture = new Fixture(gated);
            await using var context = fixture.Open(ungated: true);
            await context.Database.EnsureCreatedAsync();
            if (gated)
            {
                var store = new EfSchemaFinalizationStore(context);
                var record = await store.GetOrCreateAsync(ExecutionCommandTransportEfModule.SchemaFamily, "1.0.0", ["1.0.0", "2.0.0"], SchemaFinalizationActor.OfOperator("test"));
                await store.PlaceHoldAsync(ExecutionCommandTransportEfModule.SchemaFamily, record.Revision, null, "predecessor rollout", "test", ["1.0.0", "2.0.0"]);
                await fixture.Gate.ActivateAsync(context);
                fixture.gates.Register(typeof(ExecutionCommandTransportDbContext), fixture.Gate);
            }
            return fixture;
        }
        public ExecutionCommandTransportSqliteDbContext Open(bool ungated = false)
        {
            var options = new DbContextOptionsBuilder<ExecutionCommandTransportSqliteDbContext>().UseSqlite(database.ConnectionString, sqlite => sqlite.MaxBatchSize(1));
            if (gated && !ungated) { options.UseApplicationServiceProvider(services); EfSchemaWriteGateInterceptor.EnsureAdded(options); }
            return new(options.Options);
        }
        public EfExecutionCommandTransport Transport(ExecutionCommandTransportDbContext context) => new(context, new Accessor("partition"));
        public async Task FinalizeAsync()
        {
            await using var context = Open();
            var store = new EfSchemaFinalizationStore(context);
            var record = (await store.FindAsync(ExecutionCommandTransportEfModule.SchemaFamily))!;
            await store.ReleaseHoldAsync(ExecutionCommandTransportEfModule.SchemaFamily, record.Revision, null, "test");
            await Gate.EvaluateAsync(context);
            await Gate.RefreshAsync(context);
            Assert.Equal("2.0.0", Gate.StateOf(ExecutionCommandTransportEfModule.SchemaFamily)!.WriteVersion);
        }
        public async ValueTask DisposeAsync() { await services.DisposeAsync(); await database.DisposeAsync(); }
    }

    private sealed class SingleCurrentMember : IEfSchemaFleet
    {
        public EfSchemaFleetStanding GetLocalStanding() => new(new("host", "incarnation"), false);
        public ValueTask PublishAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<EfSchemaFleetAnswer> CountAsync(string family, string version, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public ValueTask<EfSchemaFleetAnswer> CountObservingAsync(string family, IReadOnlyList<string> versions, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public TimeSpan SettleMargin => TimeSpan.Zero;
        public IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
}
