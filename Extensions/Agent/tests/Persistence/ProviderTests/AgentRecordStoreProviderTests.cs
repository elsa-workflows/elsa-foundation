using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Persistence.EntityFrameworkCore;
using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Agent.Persistence.EntityFrameworkCore.Services;
using Elsa.Agent.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Persistence.ProviderTests;

/// <summary>
/// #3441 T028: the actual migrations and the record store on PostgreSQL 16, SQL Server 2022 and MySQL 8.4, with the
/// database's own clock, competing writers on separate connections and ordinal ordering.
/// </summary>
public sealed class AgentRecordStoreProviderTests
{
    private const string Owner = "owner-a";

    [SkippableFact]
    public Task Record_store_on_postgresql() => ProviderDatabase.RunAsync("PostgreSql", connection => RunAsync("PostgreSql", connection));

    [SkippableFact]
    public Task Record_store_on_sql_server() => ProviderDatabase.RunAsync("SqlServer", connection => RunAsync("SqlServer", connection));

    [SkippableFact]
    public Task Record_store_on_mysql() => ProviderDatabase.RunAsync("MySql", connection => RunAsync("MySql", connection));

    private static async Task RunAsync(string provider, string connection)
    {
        var codec = new AgentRecordCodec(new DefaultSecretValueProtector(new KeyRing()));
        using var contexts = Contexts(provider, connection);
        await using (var scope = contexts.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            await db.Database.MigrateAsync();
            Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        }
        var store = Store(contexts, codec);

        // Round-trip with byte-identical canonical text and fingerprints.
        var canonical = "{\"a\":1.50,\"b\":\"café \\u2028 \u2028 ñ 😀\",\"c\":[true,null]}";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var session = Session("session-1");
        var created = await store.CreateAsync(new(session)
        {
            Messages = [new(Message(session.Id, "message-1") with { DeliveryFingerprint = "delivery-1" }) { DisclosureDigests = [digest], DisclosureHistoryFingerprint = "history-1" }],
            Snapshots = [new(digest, canonical)],
            Keys = [new(AgentSessionKeyKinds.Call, new string('k', 900), "proposal-1")]
        }, Owner);
        Assert.Equal(new AgentSessionFence(Owner, 1), created.Fence);
        var before = DateTimeOffset.UtcNow;
        Assert.InRange(created.LeaseExpiresAt!.Value, before.AddSeconds(25), before.AddSeconds(35));
        using (var other = Contexts(provider, connection))
        {
            var loaded = (await Store(other, codec).LoadAsync(session.Id))!;
            Assert.Equal(canonical, Assert.Single(loaded.Snapshots).Canonical);
            Assert.Equal(digest, loaded.Snapshots[0].Digest);
            Assert.Equal("delivery-1", loaded.Messages[0].Message.DeliveryFingerprint);
            Assert.Equal("history-1", loaded.Messages[0].DisclosureHistoryFingerprint);
            Assert.Equal(new string('k', 900), Assert.Single(loaded.Keys).Key);
        }

        // Owner, fence and database-time lease.
        Assert.Equal(AgentSessionRecordOutcome.OwnedElsewhere, (await store.AcquireOwnershipAsync(session.Id, "owner-b")).Outcome);
        await store.ReleaseOwnershipAsync(session.Id, created.Fence!);
        var takeover = await store.AcquireOwnershipAsync(session.Id, "owner-b");
        Assert.Equal(new AgentSessionFence("owner-b", 2), takeover.Fence);
        Assert.Equal(AgentSessionRecordOutcome.OwnershipLost, (await store.WriteAsync(new(session.Id, created.Fence!) { Messages = [new(Message(session.Id, "stale"))] })).Outcome);
        var fence = takeover.Fence!;

        // Competing writers on separate connections serialize on the session; exactly one proposal decision wins.
        Assert.True((await store.WriteAsync(new(session.Id, fence) { Proposals = [new(Proposal(session.Id, "proposal-1"))] })).Succeeded);
        var writers = Enumerable.Range(0, 6).Select(_ => Contexts(provider, connection)).ToArray();
        try
        {
            var messages = await Task.WhenAll(writers.SelectMany((pool, writer) => Enumerable.Range(0, 3).Select(index =>
                Store(pool, codec).WriteAsync(new(session.Id, fence) { Messages = [new(Message(session.Id, $"message-{writer}-{index}"))] }))));
            Assert.All(messages, x => Assert.True(x.Succeeded));
            var decisions = await Task.WhenAll(writers.Select((pool, writer) => Store(pool, codec).WriteAsync(new(session.Id, fence)
            {
                Proposals = [new(Proposal(session.Id, "proposal-1") with { Status = writer % 2 == 0 ? AgentActionProposalStatus.Approved : AgentActionProposalStatus.Denied }) { Version = 1 }],
                Keys = [new(AgentSessionKeyKinds.Invocation, "invocation-" + writer, "proposal-1")]
            })));
            Assert.Single(decisions, x => x.Succeeded);
            Assert.Equal(5, decisions.Count(x => x.Outcome == AgentSessionRecordOutcome.VersionConflict));
        }
        finally
        {
            foreach (var pool in writers) pool.Dispose();
        }
        var recorded = (await store.LoadAsync(session.Id))!;
        Assert.Equal(1 + 18, recorded.Messages.Count);
        Assert.Equal(2, Assert.Single(recorded.Proposals).Version);
        Assert.Equal(2, recorded.Keys.Count);
        Assert.Equal(session.Id, await store.FindProposalSessionAsync("proposal-1"));
        var conflict = await store.WriteAsync(new(session.Id, fence) { Keys = [new(AgentSessionKeyKinds.Call, new string('k', 900), "proposal-2")] });
        Assert.Equal(AgentSessionRecordOutcome.KeyConflict, conflict.Outcome);

        // Owner list: ordinal identity order for equal update times, owner filter before the limit.
        var at = DateTimeOffset.UtcNow;
        foreach (var id in new[] { "mine-B", "mine-a" })
            await store.CreateAsync(new(Session(id) with { UpdatedAt = at, Workspace = "workspace-1" }), Owner);
        await store.CreateAsync(new(Session("foreign") with { ActorId = "ACTOR-1", UpdatedAt = at.AddMinutes(1), Workspace = "workspace-1" }), Owner);
        var first = await store.ListOwnedAsync(new("actor-1", "tenant-1", "workspace-1", Take: 1));
        var second = await store.ListOwnedAsync(new("actor-1", "tenant-1", "workspace-1", Take: 1, Before: first.Next));
        Assert.Equal(["mine-a", "mine-B"], first.Items.Concat(second.Items).Select(x => x.Id));
        Assert.Null(second.Next);

        // Durable audit with its owner association.
        var audit = Audit(contexts, codec);
        Assert.True(await audit.AssociateAsync(session.Id, "actor-1", "tenant-1"));
        await Task.WhenAll(Enumerable.Range(0, 6).Select(index => audit.EmitAsync(new($"audit-{index}", AgentAuditEventKind.MessageAccepted, session.Id, "actor-1",
            "Summary", at, new Dictionary<string, string>()))));
        var owned = await audit.ListOwnedAsync(new("actor-1", "tenant-1", session.Id, 10));
        Assert.Equal(6, owned.Events.Count);
        Assert.Empty((await audit.ListOwnedAsync(new("actor-1", "TENANT-1", session.Id, 10))).Events);

        // Many sessions recovering at once append their audits together from separate connections: every append succeeds
        // and the append order stays distinct and gap-free.
        var appenders = Enumerable.Range(0, 24).Select(_ => Contexts(provider, connection)).ToArray();
        try
        {
            await Task.WhenAll(appenders.Select((pool, index) => Audit(pool, codec).EmitAsync(new($"burst-{index}", AgentAuditEventKind.ProposalFailed,
                $"recovered-{index}", "actor-1", "Recovered", at, new Dictionary<string, string> { ["settledBy"] = "recovery" }))));
        }
        finally
        {
            foreach (var pool in appenders) pool.Dispose();
        }
        await using (var scope = contexts.CreateAsyncScope())
        {
            var sequences = await scope.ServiceProvider.GetRequiredService<AgentDbContext>().Set<Elsa.Agent.Persistence.EntityFrameworkCore.Entities.AgentAuditRow>()
                .OrderBy(x => x.Sequence).Select(x => x.Sequence).ToListAsync();
            Assert.Equal(Enumerable.Range(1, 6 + 24).Select(x => (long)x), sequences);
        }

        // Owner filters are exact: a trailing space is a different actor, also under SQL Server's PAD SPACE comparison.
        await store.CreateAsync(new(Session("padded") with { ActorId = "actor-2 ", UpdatedAt = at, Workspace = "workspace-2" }), Owner);
        await store.CreateAsync(new(Session("unpadded") with { ActorId = "actor-3", UpdatedAt = at, Workspace = "workspace-2" }), Owner);
        Assert.Empty((await store.ListOwnedAsync(new("actor-2", "tenant-1", "workspace-2", Take: 10))).Items);
        Assert.Empty((await store.ListOwnedAsync(new("actor-3 ", "tenant-1", "workspace-2", Take: 10))).Items);
        Assert.Empty((await store.ListOwnedAsync(new("actor-2", "tenant-1", "workspace-2 ", Take: 10))).Items);
        Assert.Equal(["padded"], (await store.ListOwnedAsync(new("actor-2 ", "tenant-1", "workspace-2", Take: 10))).Items.Select(x => x.Id));
        Assert.True(await audit.AssociateAsync("padded", "actor-2 ", "tenant-1"));
        Assert.True(await audit.AssociateAsync("unpadded", "actor-3", "tenant-1 "));
        await audit.EmitAsync(new("padded-audit", AgentAuditEventKind.MessageAccepted, "padded", "actor-2 ", "Summary", at, new Dictionary<string, string>()));
        await audit.EmitAsync(new("unpadded-audit", AgentAuditEventKind.MessageAccepted, "unpadded", "actor-3", "Summary", at, new Dictionary<string, string>()));
        Assert.Empty((await audit.ListOwnedAsync(new("actor-2", "tenant-1", null, 10))).Events);
        Assert.Empty((await audit.ListOwnedAsync(new("actor-3", "tenant-1", null, 10))).Events);
        Assert.Equal(["padded-audit"], (await audit.ListOwnedAsync(new("actor-2 ", "tenant-1", null, 10))).Events.Select(x => x.Id));

        await VerifySchemaAdmissionAsync(provider, connection, codec);

        // Unreadable state is unavailable and leaves the owner columns readable.
        await using (var scope = contexts.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentDbContext>().Set<Elsa.Agent.Persistence.EntityFrameworkCore.Entities.AgentMessageRow>()
                .Where(x => x.MessageId == "message-1").ExecuteUpdateAsync(x => x.SetProperty(row => row.Payload, "not-ciphertext"));
        Assert.Equal("unreadable", (await Assert.ThrowsAsync<AgentSessionUnavailableException>(() => store.LoadAsync(session.Id))).Reason);
        Assert.Equal(new AgentSessionOwner("actor-1", "tenant-1"), await store.FindOwnerAsync(session.Id));
    }

    private static async Task VerifySchemaAdmissionAsync(string provider, string connection, AgentRecordCodec codec)
    {
        const string id = "schema-provider-session";
        var gates = new EfSchemaFinalizationGates();
        using var gateServices = new ServiceCollection().AddSingleton(gates).BuildServiceProvider();
        using var contexts = Contexts(provider, connection, gateServices);
        var gate = new EfSchemaModuleGate(EfSchemaModuleFamilies.ForContext(typeof(AgentDbContext))!, null, new(), new());
        gates.Register(typeof(AgentDbContext), gate);
        await using (var scope = contexts.CreateAsyncScope())
            await gate.ActivateAsync(scope.ServiceProvider.GetRequiredService<AgentDbContext>());
        Assert.Equal("1.0.0", gate.StateOf("Agent")!.WriteVersion);
        var store = Store(contexts, codec);
        var audit = Audit(contexts, codec);
        var created = await store.CreateAsync(new(Session(id))
        {
            Messages = [new(Message(id, "schema-legacy-message"))],
            Proposals = [new(Proposal(id, "schema-proposal"))]
        }, Owner);
        Assert.True(created.Succeeded);
        var performed = new AgentProposalRecord(Proposal(id, "schema-proposal") with { Status = AgentActionProposalStatus.Performed }) { Version = 1 };
        var write = new AgentSessionRecordWrite(id, created.Fence!) { Proposals = [performed] };
        var before = JsonSerializer.Serialize(await store.LoadAsync(id));
        var refusal = await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => store.WriteAsync(write));
        Assert.Equal(("Agent", "1.0.0", "1.1.0"), (refusal.Family, refusal.WriteVersion, refusal.RequiredVersion));
        Assert.Equal(before, JsonSerializer.Serialize(await store.LoadAsync(id)));
        await audit.EmitAsync(Event("schema-legacy-audit"));
        await using (var scope = contexts.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            Assert.Equal("1.0.0", (await db.Set<AgentSessionRow>().AsNoTracking().SingleAsync(x => x.SessionId == id)).SchemaVersion);
            await EfSchemaFinalizationTestSupport.FinalizeAsync(new(db), "Agent", ["1.0.0", "1.1.0"], "1.1.0");
            await gate.RefreshAsync(db);
        }
        Assert.Equal("1.1.0", gate.StateOf("Agent")!.WriteVersion);

        // These are the same store/gate instances that refused the held-format write.
        const string canonical = "{\"amount\":1.50,\"text\":\"café \\u2028\"}";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        write = write with
        {
            Messages = [new(Message(id, "schema-current-message"))],
            Context = [new("schema-context", "test", null, "Context", "test.context", AgentContextSensitivity.Internal,
                "scope", "Summary", JsonSerializer.SerializeToElement(new { opaque = true }), new Dictionary<string, string>())],
            Snapshots = [new(digest, canonical)],
            Keys = [new(AgentSessionKeyKinds.Call, "schema-call", "schema-proposal")]
        };
        Assert.True((await store.WriteAsync(write)).Succeeded);
        var loaded = (await store.LoadAsync(id))!;
        Assert.Equal(2, loaded.Version);
        Assert.Equal(AgentActionProposalStatus.Performed, Assert.Single(loaded.Proposals).Proposal.Status);
        Assert.Equal(canonical, Assert.Single(loaded.Snapshots).Canonical);
        Assert.Equal(digest, loaded.Snapshots[0].Digest);
        Assert.Equal(2, loaded.Messages.Count);
        await audit.EmitAsync(Event("schema-current-audit"));
        Assert.True(await audit.AssociateAsync(id, "actor-1", "tenant-1"));

        // Session-only content saves must still pass the shared interceptor, without a second version increment.
        Assert.True((await store.WriteAsync(new(id, created.Fence!)
        {
            PausedTurn = new(new(id, "turn", "schema-proposal", "schema-call", "tool", [], 1))
        })).Succeeded);
        Assert.True((await store.WriteAsync(new(id, created.Fence!) { PendingAudits = [Event("schema-pending")] })).Succeeded);
        loaded = (await store.LoadAsync(id))!;
        Assert.Equal(4, loaded.Version);
        Assert.Equal("schema-proposal", loaded.PausedTurn!.Turn.ProposalId);
        Assert.Equal("schema-pending", Assert.Single(loaded.PendingAudits).Id);
        await using (var scope = contexts.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            Assert.Equal("1.1.0", (await db.Set<AgentSessionRow>().AsNoTracking().SingleAsync(x => x.SessionId == id)).SchemaVersion);
            Assert.Equal("1.0.0", (await db.Set<AgentMessageRow>().AsNoTracking().SingleAsync(x => x.MessageId == "schema-legacy-message")).SchemaVersion);
            Assert.Equal("1.1.0", (await db.Set<AgentMessageRow>().AsNoTracking().SingleAsync(x => x.MessageId == "schema-current-message")).SchemaVersion);
            Assert.Equal("1.1.0", (await db.Set<AgentContextRow>().AsNoTracking().SingleAsync(x => x.SessionId == id)).SchemaVersion);
            Assert.Equal("1.1.0", (await db.Set<AgentDisclosureSnapshotRow>().AsNoTracking().SingleAsync(x => x.SessionId == id)).SchemaVersion);
            Assert.Equal("1.1.0", (await db.Set<AgentProposalRow>().AsNoTracking().SingleAsync(x => x.SessionId == id)).SchemaVersion);
            Assert.Equal("1.1.0", (await db.Set<AgentCallKeyRow>().AsNoTracking().SingleAsync(x => x.SessionId == id)).SchemaVersion);
            Assert.Equal("1.0.0", (await db.Set<AgentAuditRow>().AsNoTracking().SingleAsync(x => x.EventId == "schema-legacy-audit")).SchemaVersion);
            Assert.Equal("1.1.0", (await db.Set<AgentAuditRow>().AsNoTracking().SingleAsync(x => x.EventId == "schema-current-audit")).SchemaVersion);
            Assert.Equal("1.1.0", (await db.Set<AgentAuditOwnerRow>().AsNoTracking().SingleAsync(x => x.SessionId == id)).SchemaVersion);
            Assert.Equal("1.0.0", (await db.Set<AgentAuditSequenceRow>().AsNoTracking().SingleAsync()).SchemaVersion);
        }

        // An ordinary ungated direct consumer cannot lower the stamp adopted by the owning gate.
        using var ungated = Contexts(provider, connection);
        before = JsonSerializer.Serialize(loaded);
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => Store(ungated, codec).WriteAsync(new(id, created.Fence!)
        { Messages = [new(Message(id, "must-not-write"))] }));
        Assert.Equal(before, JsonSerializer.Serialize(await store.LoadAsync(id)));

        AgentAuditEvent Event(string eventId) => new(eventId, AgentAuditEventKind.MessageAccepted, id, "actor-1", "Recorded",
            DateTimeOffset.UtcNow, new Dictionary<string, string>());
    }

    private static EfAgentAuditStore Audit(ServiceProvider contexts, AgentRecordCodec codec)
        => new(contexts.GetRequiredService<IServiceScopeFactory>(), static scoped => scoped.GetRequiredService<AgentDbContext>(), codec);

    private static EfAgentSessionRecordStore Store(ServiceProvider contexts, AgentRecordCodec codec)
        => new(contexts.GetRequiredService<IServiceScopeFactory>(), static scoped => scoped.GetRequiredService<AgentDbContext>(), codec, TimeSpan.FromSeconds(30));

    private static ServiceProvider Contexts(string provider, string connection, ServiceProvider? gateServices = null)
    {
        var binding = EfModuleBinding.For(typeof(AgentDbContext));
        var services = new ServiceCollection();
        var add = binding.Select<Action<IServiceCollection>>(provider,
            Add<AgentSqliteDbContext>, Add<AgentSqlServerDbContext>, Add<AgentPostgreSqlDbContext>, Add<AgentMySqlDbContext>);
        add(services);
        return services.BuildServiceProvider();

        void Add<TContext>(IServiceCollection collection) where TContext : AgentDbContext
        {
            collection.AddDbContext<TContext>((sp, builder) =>
            {
                binding.Apply(builder, sp, provider, connection, null);
                if (gateServices is not null) builder.UseApplicationServiceProvider(gateServices);
            });
            collection.AddScoped<AgentDbContext>(scoped => scoped.GetRequiredService<TContext>());
        }
    }

    private static AgentSession Session(string id) => new(id, "Title", "tenant-1", "actor-1", "conversation-1", "provider", "explain",
        AgentPolicy.Default, AgentSessionStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(8),
        new Dictionary<string, string>()) { PolicyRevision = 1 };

    private static AgentMessage Message(string sessionId, string id) => new(id, sessionId, AgentRole.User, "hello", AgentMessageStatus.Pending,
        null, DateTimeOffset.UtcNow, null, null, [], [], []);

    private static AgentActionProposal Proposal(string sessionId, string id) => new(id, sessionId, null, "tool", "tool-invocation", "Title", "Summary",
        AgentRisk.ReviewRequired, "revision-1", [], [], [], null, [], "test.resource", "resource-1", true, AgentActionProposalStatus.AwaitingApproval,
        null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class KeyRing : ISecretKeyRing
    {
        private readonly SecretEncryptionKey _key = new("provider-test", RandomNumberGenerator.GetBytes(32));
        public SecretEncryptionKey ActiveKey => _key;
        public bool TryGetKey(string keyId, out SecretEncryptionKey key) { key = _key; return keyId == _key.KeyId; }
    }
}
