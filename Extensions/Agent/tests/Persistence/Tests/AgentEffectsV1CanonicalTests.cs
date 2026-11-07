using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Persistence.EntityFrameworkCore;
using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Agent.Persistence.EntityFrameworkCore.Services;
using Elsa.Agent.Services;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Persistence.Tests;

/// <summary>The three shared proof algorithms, exposed through normal public xUnit discovery without widening production types.</summary>
public sealed class AgentEffectsV1CanonicalTests : IAsyncLifetime
{
    private readonly AgentEffectsV1Proof _proof = new();
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _proof.DisposeAsync().AsTask();

    [Fact]
    public void Upcasting_each_source_fixture_yields_its_expected_fixture()
        => _proof.Upcasting_each_source_fixture_yields_its_expected_fixture();

    [Fact]
    public Task Writing_each_expected_value_at_the_source_version_reproduces_its_source_fixture()
        => _proof.Writing_each_expected_value_at_the_source_version_reproduces_its_source_fixture();

    [Fact]
    public Task Reading_each_source_fixture_through_the_store_equals_reading_its_expected_fixture()
        => _proof.Reading_each_source_fixture_through_the_store_equals_reading_its_expected_fixture();

    [Fact]
    public void Frozen_pairs_cover_every_declared_content_table_and_all_three_session_columns()
    {
        var fixtures = EfSchemaUpcasterFixtureSupport.LoadAll(AgentEfModule.Chain, "1.0.0", "1.1.0");
        Assert.Equal(AgentEfModule.Chain.ContentColumns.Select(x => x.Entity).Distinct().OrderBy(x => x.FullName), fixtures.Select(x => x.Entity).OrderBy(x => x.FullName));
        var session = Assert.Single(fixtures, x => x.Entity == typeof(AgentSessionRow));
        Assert.Equal(3, session.Source.Columns.Count);
        Assert.All(session.Source.Columns.Values, value => Assert.NotNull(value));
    }
}

// The actual upcaster is internal; only the nongeneric forwarding runner is a public discoverable test class.
internal sealed class AgentEffectsV1Proof() : EfSchemaUpcasterProof<AgentEffectsV1Upcaster, AgentFixtureValue>(AgentEfModule.SchemaFamily, new AgentFixtureProofStore());

internal sealed class AgentFixtureProofStore : IEfSchemaUpcasterProofStore<AgentFixtureValue>
{
    public EfSchemaChain Chain => AgentEfModule.Chain;

    public async Task<AgentFixtureValue> ReadAsync(EfSchemaUpcasterFixture fixture, string stamp, EfSchemaRowContent row)
    {
        Assert.Equal(fixture.Entity, row.Entity);
        using var database = new AgentSqliteDatabase();
        var codec = AgentSchemaFixtureSeed.Codec;
        var records = database.RecordStore(AgentSchemaFixtureSeed.Clock, codec);
        var audit = database.AuditStore(codec);
        Assert.True((await records.CreateAsync(AgentSchemaFixtureSeed.Record(), AgentSchemaFixtureSeed.Owner)).Succeeded);
        await audit.EmitAsync(AgentSchemaFixtureSeed.Audit());
        await using (var db = database.Open())
        {
            var entity = await AgentSchemaFixtureSeed.FindRowAsync(db, fixture.Entity);
            var entry = db.Entry(entity);
            foreach (var column in row.Columns) entry.Property(column.Key).CurrentValue = column.Value;
            entry.Property("SchemaVersion").CurrentValue = stamp;
            await db.SaveChangesAsync(); // This deliberately ungated fixture installation preserves the exact supplied row/stamp.
            db.ChangeTracker.Clear();
            var installed = db.Entry(await AgentSchemaFixtureSeed.FindRowAsync(db, fixture.Entity));
            Assert.Equal(stamp, installed.Property("SchemaVersion").CurrentValue);
            Assert.All(row.Columns, column => Assert.Equal(column.Value, installed.Property(column.Key).CurrentValue));
        }
        var before = await database.ReadAllTextAsync();
        object value = fixture.Entity == typeof(AgentAuditRow)
            ? Assert.Single(await audit.ListAsync(AgentSchemaFixtureSeed.SessionId))
            : (await records.LoadAsync(AgentSchemaFixtureSeed.SessionId))!;
        Assert.Equal(before, await database.ReadAllTextAsync());
        if (value is AgentSessionRecord loaded)
        {
            var message = Assert.Single(loaded.Messages);
            Assert.False(message.Message.Error!.OwnerUnavailable);
            var proposal = Assert.Single(loaded.Proposals);
            Assert.Null(proposal.Proposal.PreparedInvocation!.RequiredPrivilegeClasses);
            Assert.Null(proposal.Result!.Receipt!.ModuleCode);
            Assert.Equal(AgentInvocationIntegrity.PreparedDigest(proposal.Proposal.PreparedInvocation), proposal.Proposal.PreparedInvocation.ApprovalDigest);
            var snapshot = Assert.Single(loaded.Snapshots);
            Assert.Equal(AgentInvocationIntegrity.DigestCanonical(snapshot.Canonical), snapshot.Digest);
            var history = new[] { AgentDisclosureHistory.Rebuild(snapshot)! };
            Assert.NotNull(history[0]);
            Assert.Equal(AgentInvocationIntegrity.Digest(history), message.DisclosureHistoryFingerprint);
            Assert.True(AgentDisclosureHistory.AssistantHistoryMatches(message.Message with { DisclosureHistory = history }));
            Assert.True(AgentDisclosureHistory.PauseHistoryMatches(loaded.PausedTurn!.Turn with { DisclosureHistory = history }));
        }
        return new(fixture.Entity, value);
    }

    public EfSchemaRowContent WriteAt(EfSchemaUpcasterFixture fixture, AgentFixtureValue value, string version)
    {
        Assert.Equal("1.0.0", version);
        Assert.Equal(fixture.Entity, value.Entity);
        var codec = AgentSchemaFixtureSeed.Codec;
        const string id = AgentSchemaFixtureSeed.SessionId;
        if (value.Entity == typeof(AgentAuditRow))
        {
            var audit = Assert.IsType<AgentAuditEvent>(value.Value);
            return new(value.Entity, [("Payload", codec.Protect(AgentRecordCodec.AuditPurpose, audit.SessionId ?? "", audit.Id, audit))]);
        }
        var record = Assert.IsType<AgentSessionRecord>(value.Value);
        if (value.Entity == typeof(AgentSessionRow))
            return new(value.Entity,
            [
                ("Payload", codec.Protect(AgentRecordCodec.SessionPurpose, id, "session", record.Session)),
                ("PausedTurnPayload", record.PausedTurn is null ? null : codec.Protect(AgentRecordCodec.PausedTurnPurpose, id, "paused-turn", record.PausedTurn)),
                ("PendingAuditsPayload", record.PendingAudits.Count == 0 ? null : codec.Protect(AgentRecordCodec.PendingAuditsPurpose, id, "pending-audits", record.PendingAudits.ToArray()))
            ]);
        if (value.Entity == typeof(AgentMessageRow))
        {
            var message = Assert.Single(record.Messages);
            return new(value.Entity, [("Payload", codec.Protect(AgentRecordCodec.MessagePurpose, id, message.Message.Id, message))]);
        }
        if (value.Entity == typeof(AgentContextRow))
        {
            var context = Assert.Single(record.Context);
            return new(value.Entity, [("Payload", codec.Protect(AgentRecordCodec.ContextPurpose, id, AgentRecordCodec.Hash(context.Id), context))]);
        }
        if (value.Entity == typeof(AgentProposalRow))
        {
            // These frozen pairs were captured from creates: Version0 is the writer's input, Version1 the loaded token.
            // Reconstruct only that concurrency input; every retained outcome/member stays intact.
            var proposal = Assert.Single(record.Proposals) with { Version = 0 };
            return new(value.Entity, [("Payload", codec.Protect(AgentRecordCodec.ProposalPurpose, id, proposal.Proposal.Id, proposal))]);
        }
        throw new InvalidOperationException("The fixture does not name an Agent content table.");
    }


}

/// <summary>Complete hydrated graphs compared structurally, including ordered lists, nested JSON and concurrency metadata.</summary>
internal sealed class AgentFixtureValue(Type entity, object value) : IEquatable<AgentFixtureValue>
{
    public Type Entity { get; } = entity;
    public object Value { get; } = value;
    public bool Equals(AgentFixtureValue? other) => other is not null && Entity == other.Entity
        && JsonNode.DeepEquals(JsonSerializer.SerializeToNode(Value), JsonSerializer.SerializeToNode(other.Value));
    public override bool Equals(object? obj) => obj is AgentFixtureValue other && Equals(other);
    public override int GetHashCode() => Entity.GetHashCode();
    public override string ToString() => Entity.Name + " synthetic complete hydrated graph";
}

public sealed class AgentFixtureValueTests
{
    [Fact]
    public void Independent_hydration_compares_complete_nested_values_and_rejects_member_order_content_or_metadata_loss()
    {
        var original = AgentSchemaFixtureSeed.Record() with
        {
            Context = [Records.Attachment("first", JsonSerializer.SerializeToElement(new { nested = new { values = new[] { "a", "b" } } })), Records.Attachment("second")],
            Owner = new("fixture-owner", 1), Version = 1, LeaseExpiresAt = AgentSchemaFixtureSeed.At.AddSeconds(30)
        };
        var clone = JsonSerializer.Deserialize<AgentSessionRecord>(JsonSerializer.Serialize(original))!;
        var expected = new AgentFixtureValue(typeof(AgentSessionRow), original);
        var independentlyRead = new AgentFixtureValue(typeof(AgentSessionRow), clone);
        Assert.NotSame(original.Context, clone.Context);
        Assert.Equal(expected, independentlyRead);
        Assert.Equal(expected.GetHashCode(), independentlyRead.GetHashCode());
        AgentFixtureValue Value(AgentSessionRecord value) => new(typeof(AgentSessionRow), value);
        Assert.NotEqual(expected, Value(clone with { Context = clone.Context.Reverse().ToArray() }));
        Assert.NotEqual(expected, Value(clone with
        {
            Context = [clone.Context[0] with { Content = JsonSerializer.SerializeToElement(new { nested = new { values = new[] { "b", "a" } } }) }, clone.Context[1]]
        }));
        Assert.NotEqual(expected, Value(clone with { Version = 2 }));
        Assert.NotEqual(expected, Value(clone with { Owner = clone.Owner! with { Fence = 2 } }));
        Assert.NotEqual(expected, Value(clone with { LeaseExpiresAt = clone.LeaseExpiresAt!.Value.AddSeconds(1) }));
        Assert.NotEqual(expected, Value(clone with { PendingAudits = [clone.PendingAudits[0] with { Summary = "Changed" }] }));
        Assert.NotEqual(expected, Value(clone with
        {
            Proposals = [clone.Proposals[0] with { Proposal = clone.Proposals[0].Proposal with
                { PreparedInvocation = clone.Proposals[0].Proposal.PreparedInvocation! with { ApprovalDigest = "changed" } } }]
        }));
        Assert.NotEqual(expected, Value(clone with
        {
            Snapshots = [clone.Snapshots[0] with { Canonical = clone.Snapshots[0].Canonical.Replace("1.50", "1.5", StringComparison.Ordinal) }]
        }));
    }
}
