using System.Security.Cryptography;
using System.Text;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Persistence.EntityFrameworkCore;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Services;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Weaver.Persistence.ProviderTests;

/// <summary>
/// #3441 T028: the actual migrations and the record store on PostgreSQL 16, SQL Server 2022 and MySQL 8.4, with the
/// database's own clock, competing writers on separate connections and ordinal ordering.
/// </summary>
public sealed class WeaverRecordStoreProviderTests
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
        var codec = new WeaverRecordCodec(new DefaultSecretValueProtector(new KeyRing()));
        using var contexts = Contexts(provider, connection);
        await using (var scope = contexts.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeaverDbContext>();
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
            Keys = [new(WeaverSessionKeyKinds.Call, new string('k', 900), "proposal-1")]
        }, Owner);
        Assert.Equal(new WeaverSessionFence(Owner, 1), created.Fence);
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
        Assert.Equal(WeaverSessionRecordOutcome.OwnedElsewhere, (await store.AcquireOwnershipAsync(session.Id, "owner-b")).Outcome);
        await store.ReleaseOwnershipAsync(session.Id, created.Fence!);
        var takeover = await store.AcquireOwnershipAsync(session.Id, "owner-b");
        Assert.Equal(new WeaverSessionFence("owner-b", 2), takeover.Fence);
        Assert.Equal(WeaverSessionRecordOutcome.OwnershipLost, (await store.WriteAsync(new(session.Id, created.Fence!) { Messages = [new(Message(session.Id, "stale"))] })).Outcome);
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
                Proposals = [new(Proposal(session.Id, "proposal-1") with { Status = writer % 2 == 0 ? WeaverActionProposalStatus.Approved : WeaverActionProposalStatus.Denied }) { Version = 1 }],
                Keys = [new(WeaverSessionKeyKinds.Invocation, "invocation-" + writer, "proposal-1")]
            })));
            Assert.Single(decisions, x => x.Succeeded);
            Assert.Equal(5, decisions.Count(x => x.Outcome == WeaverSessionRecordOutcome.VersionConflict));
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
        var conflict = await store.WriteAsync(new(session.Id, fence) { Keys = [new(WeaverSessionKeyKinds.Call, new string('k', 900), "proposal-2")] });
        Assert.Equal(WeaverSessionRecordOutcome.KeyConflict, conflict.Outcome);

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
        await Task.WhenAll(Enumerable.Range(0, 6).Select(index => audit.EmitAsync(new($"audit-{index}", WeaverAuditEventKind.MessageAccepted, session.Id, "actor-1",
            "Summary", at, new Dictionary<string, string>()))));
        var owned = await audit.ListOwnedAsync(new("actor-1", "tenant-1", session.Id, 10));
        Assert.Equal(6, owned.Events.Count);
        Assert.Empty((await audit.ListOwnedAsync(new("actor-1", "TENANT-1", session.Id, 10))).Events);

        // Many sessions recovering at once append their audits together from separate connections: every append succeeds
        // and the append order stays distinct and gap-free.
        var appenders = Enumerable.Range(0, 24).Select(_ => Contexts(provider, connection)).ToArray();
        try
        {
            await Task.WhenAll(appenders.Select((pool, index) => Audit(pool, codec).EmitAsync(new($"burst-{index}", WeaverAuditEventKind.ProposalFailed,
                $"recovered-{index}", "actor-1", "Recovered", at, new Dictionary<string, string> { ["settledBy"] = "recovery" }))));
        }
        finally
        {
            foreach (var pool in appenders) pool.Dispose();
        }
        await using (var scope = contexts.CreateAsyncScope())
        {
            var sequences = await scope.ServiceProvider.GetRequiredService<WeaverDbContext>().Set<Elsa.Weaver.Persistence.EntityFrameworkCore.Entities.WeaverAuditRow>()
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
        await audit.EmitAsync(new("padded-audit", WeaverAuditEventKind.MessageAccepted, "padded", "actor-2 ", "Summary", at, new Dictionary<string, string>()));
        await audit.EmitAsync(new("unpadded-audit", WeaverAuditEventKind.MessageAccepted, "unpadded", "actor-3", "Summary", at, new Dictionary<string, string>()));
        Assert.Empty((await audit.ListOwnedAsync(new("actor-2", "tenant-1", null, 10))).Events);
        Assert.Empty((await audit.ListOwnedAsync(new("actor-3", "tenant-1", null, 10))).Events);
        Assert.Equal(["padded-audit"], (await audit.ListOwnedAsync(new("actor-2 ", "tenant-1", null, 10))).Events.Select(x => x.Id));

        // Unreadable state is unavailable and leaves the owner columns readable.
        await using (var scope = contexts.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<WeaverDbContext>().Set<Elsa.Weaver.Persistence.EntityFrameworkCore.Entities.WeaverMessageRow>()
                .Where(x => x.MessageId == "message-1").ExecuteUpdateAsync(x => x.SetProperty(row => row.Payload, "not-ciphertext"));
        Assert.Equal("unreadable", (await Assert.ThrowsAsync<WeaverSessionUnavailableException>(() => store.LoadAsync(session.Id))).Reason);
        Assert.Equal(new WeaverSessionOwner("actor-1", "tenant-1"), await store.FindOwnerAsync(session.Id));
    }

    private static EfWeaverAuditStore Audit(ServiceProvider contexts, WeaverRecordCodec codec)
        => new(contexts.GetRequiredService<IServiceScopeFactory>(), static scoped => scoped.GetRequiredService<WeaverDbContext>(), codec);

    private static EfWeaverSessionRecordStore Store(ServiceProvider contexts, WeaverRecordCodec codec)
        => new(contexts.GetRequiredService<IServiceScopeFactory>(), static scoped => scoped.GetRequiredService<WeaverDbContext>(), codec, TimeSpan.FromSeconds(30));

    private static ServiceProvider Contexts(string provider, string connection)
    {
        var binding = EfModuleBinding.For(typeof(WeaverDbContext));
        var services = new ServiceCollection();
        var add = binding.Select<Action<IServiceCollection>>(provider,
            Add<WeaverSqliteDbContext>, Add<WeaverSqlServerDbContext>, Add<WeaverPostgreSqlDbContext>, Add<WeaverMySqlDbContext>);
        add(services);
        return services.BuildServiceProvider();

        void Add<TContext>(IServiceCollection collection) where TContext : WeaverDbContext
        {
            collection.AddDbContext<TContext>((sp, builder) => binding.Apply(builder, sp, provider, connection, null));
            collection.AddScoped<WeaverDbContext>(scoped => scoped.GetRequiredService<TContext>());
        }
    }

    private static WeaverSession Session(string id) => new(id, "Title", "tenant-1", "actor-1", "conversation-1", "provider", "explain",
        WeaverPolicy.Default, WeaverSessionStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(8),
        new Dictionary<string, string>()) { PolicyRevision = 1 };

    private static WeaverMessage Message(string sessionId, string id) => new(id, sessionId, WeaverRole.User, "hello", WeaverMessageStatus.Pending,
        null, DateTimeOffset.UtcNow, null, null, [], [], []);

    private static WeaverActionProposal Proposal(string sessionId, string id) => new(id, sessionId, null, "tool", "tool-invocation", "Title", "Summary",
        WeaverRisk.ReviewRequired, "revision-1", [], [], [], null, [], "test.resource", "resource-1", true, WeaverActionProposalStatus.AwaitingApproval,
        null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class KeyRing : ISecretKeyRing
    {
        private readonly SecretEncryptionKey _key = new("provider-test", RandomNumberGenerator.GetBytes(32));
        public SecretEncryptionKey ActiveKey => _key;
        public bool TryGetKey(string keyId, out SecretEncryptionKey key) { key = _key; return keyId == _key.KeyId; }
    }
}
