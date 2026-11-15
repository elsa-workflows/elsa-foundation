using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Persistence.EntityFrameworkCore;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Entities;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Services;
using Elsa.Weaver.Services;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Weaver.Persistence.Tests;

/// <summary>The three shared proof algorithms, exposed through normal public xUnit discovery without widening production types.</summary>
public sealed class WeaverEffectsV1CanonicalTests : IAsyncLifetime
{
    private readonly WeaverEffectsV1Proof _proof = new();
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
        var fixtures = EfSchemaUpcasterFixtureSupport.LoadAll(WeaverEfModule.Chain, "1.0.0", "1.1.0");
        Assert.Equal(WeaverEfModule.Chain.ContentColumns.Select(x => x.Entity).Distinct().OrderBy(x => x.FullName), fixtures.Select(x => x.Entity).OrderBy(x => x.FullName));
        var session = Assert.Single(fixtures, x => x.Entity == typeof(WeaverSessionRow));
        Assert.Equal(3, session.Source.Columns.Count);
        Assert.All(session.Source.Columns.Values, value => Assert.NotNull(value));
    }
}

// The actual upcaster is internal; only the nongeneric forwarding runner is a public discoverable test class.
internal sealed class WeaverEffectsV1Proof() : EfSchemaUpcasterProof<WeaverEffectsV1Upcaster, WeaverFixtureValue>(WeaverEfModule.SchemaFamily, new WeaverFixtureProofStore());

internal sealed class WeaverFixtureProofStore : IEfSchemaUpcasterProofStore<WeaverFixtureValue>
{
    public EfSchemaChain Chain => WeaverEfModule.Chain;

    public async Task<WeaverFixtureValue> ReadAsync(EfSchemaUpcasterFixture fixture, string stamp, EfSchemaRowContent row)
    {
        Assert.Equal(fixture.Entity, row.Entity);
        using var database = new WeaverSqliteDatabase();
        var codec = WeaverSchemaFixtureSeed.Codec;
        var records = database.RecordStore(WeaverSchemaFixtureSeed.Clock, codec);
        var audit = database.AuditStore(codec);
        Assert.True((await records.CreateAsync(WeaverSchemaFixtureSeed.Record(), WeaverSchemaFixtureSeed.Owner)).Succeeded);
        await audit.EmitAsync(WeaverSchemaFixtureSeed.Audit());
        await using (var db = database.Open())
        {
            var entity = await WeaverSchemaFixtureSeed.FindRowAsync(db, fixture.Entity);
            var entry = db.Entry(entity);
            foreach (var column in row.Columns) entry.Property(column.Key).CurrentValue = column.Value;
            entry.Property("SchemaVersion").CurrentValue = stamp;
            await db.SaveChangesAsync(); // This deliberately ungated fixture installation preserves the exact supplied row/stamp.
            db.ChangeTracker.Clear();
            var installed = db.Entry(await WeaverSchemaFixtureSeed.FindRowAsync(db, fixture.Entity));
            Assert.Equal(stamp, installed.Property("SchemaVersion").CurrentValue);
            Assert.All(row.Columns, column => Assert.Equal(column.Value, installed.Property(column.Key).CurrentValue));
        }
        var before = await database.ReadAllTextAsync();
        object value = fixture.Entity == typeof(WeaverAuditRow)
            ? Assert.Single(await audit.ListAsync(WeaverSchemaFixtureSeed.SessionId))
            : (await records.LoadAsync(WeaverSchemaFixtureSeed.SessionId))!;
        Assert.Equal(before, await database.ReadAllTextAsync());
        if (value is WeaverSessionRecord loaded)
        {
            var message = Assert.Single(loaded.Messages);
            Assert.False(message.Message.Error!.OwnerUnavailable);
            var proposal = Assert.Single(loaded.Proposals);
            Assert.Null(proposal.Proposal.PreparedInvocation!.RequiredPrivilegeClasses);
            Assert.Null(proposal.Result!.Receipt!.ModuleCode);
            Assert.Equal(WeaverInvocationIntegrity.PreparedDigest(proposal.Proposal.PreparedInvocation), proposal.Proposal.PreparedInvocation.ApprovalDigest);
            var snapshot = Assert.Single(loaded.Snapshots);
            Assert.Equal(WeaverInvocationIntegrity.DigestCanonical(snapshot.Canonical), snapshot.Digest);
            var history = new[] { WeaverDisclosureHistory.Rebuild(snapshot)! };
            Assert.NotNull(history[0]);
            Assert.Equal(WeaverInvocationIntegrity.Digest(history), message.DisclosureHistoryFingerprint);
            Assert.True(WeaverDisclosureHistory.AssistantHistoryMatches(message.Message with { DisclosureHistory = history }));
            Assert.True(WeaverDisclosureHistory.PauseHistoryMatches(loaded.PausedTurn!.Turn with { DisclosureHistory = history }));
        }
        return new(fixture.Entity, value);
    }

    public EfSchemaRowContent WriteAt(EfSchemaUpcasterFixture fixture, WeaverFixtureValue value, string version)
    {
        Assert.Equal("1.0.0", version);
        Assert.Equal(fixture.Entity, value.Entity);
        var codec = WeaverSchemaFixtureSeed.Codec;
        const string id = WeaverSchemaFixtureSeed.SessionId;
        if (value.Entity == typeof(WeaverAuditRow))
        {
            var audit = Assert.IsType<WeaverAuditEvent>(value.Value);
            return new(value.Entity, [("Payload", codec.Protect(WeaverRecordCodec.AuditPurpose, audit.SessionId ?? "", audit.Id, audit))]);
        }
        var record = Assert.IsType<WeaverSessionRecord>(value.Value);
        if (value.Entity == typeof(WeaverSessionRow))
            return new(value.Entity,
            [
                ("Payload", codec.Protect(WeaverRecordCodec.SessionPurpose, id, "session", record.Session)),
                ("PausedTurnPayload", record.PausedTurn is null ? null : codec.Protect(WeaverRecordCodec.PausedTurnPurpose, id, "paused-turn", record.PausedTurn)),
                ("PendingAuditsPayload", record.PendingAudits.Count == 0 ? null : codec.Protect(WeaverRecordCodec.PendingAuditsPurpose, id, "pending-audits", record.PendingAudits.ToArray()))
            ]);
        if (value.Entity == typeof(WeaverMessageRow))
        {
            var message = Assert.Single(record.Messages);
            return new(value.Entity, [("Payload", codec.Protect(WeaverRecordCodec.MessagePurpose, id, message.Message.Id, message))]);
        }
        if (value.Entity == typeof(WeaverContextRow))
        {
            var context = Assert.Single(record.Context);
            return new(value.Entity, [("Payload", codec.Protect(WeaverRecordCodec.ContextPurpose, id, WeaverRecordCodec.Hash(context.Id), context))]);
        }
        if (value.Entity == typeof(WeaverProposalRow))
        {
            // These frozen pairs were captured from creates: Version0 is the writer's input, Version1 the loaded token.
            // Reconstruct only that concurrency input; every retained outcome/member stays intact.
            var proposal = Assert.Single(record.Proposals) with { Version = 0 };
            return new(value.Entity, [("Payload", codec.Protect(WeaverRecordCodec.ProposalPurpose, id, proposal.Proposal.Id, proposal))]);
        }
        throw new InvalidOperationException("The fixture does not name an Weaver content table.");
    }


}

/// <summary>Complete hydrated graphs compared structurally, including ordered lists, nested JSON and concurrency metadata.</summary>
internal sealed class WeaverFixtureValue(Type entity, object value) : IEquatable<WeaverFixtureValue>
{
    public Type Entity { get; } = entity;
    public object Value { get; } = value;
    public bool Equals(WeaverFixtureValue? other) => other is not null && Entity == other.Entity
        && JsonNode.DeepEquals(JsonSerializer.SerializeToNode(Value), JsonSerializer.SerializeToNode(other.Value));
    public override bool Equals(object? obj) => obj is WeaverFixtureValue other && Equals(other);
    public override int GetHashCode() => Entity.GetHashCode();
    public override string ToString() => Entity.Name + " synthetic complete hydrated graph";
}

public sealed class WeaverFixtureValueTests
{
    [Fact]
    public void Independent_hydration_compares_complete_nested_values_and_rejects_member_order_content_or_metadata_loss()
    {
        var original = WeaverSchemaFixtureSeed.Record() with
        {
            Context = [Records.Attachment("first", JsonSerializer.SerializeToElement(new { nested = new { values = new[] { "a", "b" } } })), Records.Attachment("second")],
            Owner = new("fixture-owner", 1), Version = 1, LeaseExpiresAt = WeaverSchemaFixtureSeed.At.AddSeconds(30)
        };
        var clone = JsonSerializer.Deserialize<WeaverSessionRecord>(JsonSerializer.Serialize(original))!;
        var expected = new WeaverFixtureValue(typeof(WeaverSessionRow), original);
        var independentlyRead = new WeaverFixtureValue(typeof(WeaverSessionRow), clone);
        Assert.NotSame(original.Context, clone.Context);
        Assert.Equal(expected, independentlyRead);
        Assert.Equal(expected.GetHashCode(), independentlyRead.GetHashCode());
        WeaverFixtureValue Value(WeaverSessionRecord value) => new(typeof(WeaverSessionRow), value);
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
