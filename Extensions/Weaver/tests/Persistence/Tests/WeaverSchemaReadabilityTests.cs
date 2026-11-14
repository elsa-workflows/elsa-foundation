using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Persistence.EntityFrameworkCore;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Entities;
using Elsa.Weaver.Services;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Weaver.Persistence.Tests;

/// <summary>Reader-only expansion: real protected legacy aggregates, mixed readable stamps, and owner-first projections.</summary>
public sealed class WeaverSchemaReadabilityTests
{
    public static TheoryData<string> AggregateTables => new()
    {
        "elsa_weaver_sessions", "elsa_weaver_messages", "elsa_weaver_disclosure_snapshots",
        "elsa_weaver_context", "elsa_weaver_proposals", "elsa_weaver_call_keys"
    };

    [Theory]
    [MemberData(nameof(AggregateTables))]
    public async Task Each_readable_new_stamp_can_mix_with_real_protected_legacy_rows_without_rewriting_content(string table)
    {
        using var database = new WeaverSqliteDatabase();
        var store = database.RecordStore();
        await Seed(database);
        var before = (await store.LoadAsync("schema-session"))!;
        Assert.True(await database.ExecuteAsync($"UPDATE {table} SET SchemaVersion = '1.1.0'") > 0);
        var stored = await database.ReadAllTextAsync();

        var read = (await store.LoadAsync(before.Session.Id))!;

        Assert.Equal(WeaverInvocationIntegrity.Digest(before), WeaverInvocationIntegrity.Digest(read));
        Assert.Equal(before.Snapshots.Single().Canonical, read.Snapshots.Single().Canonical);
        Assert.Equal("schema-proposal", read.PausedTurn!.Turn.ProposalId);
        Assert.Equal("pending", Assert.Single(read.PendingAudits).Id);
        Assert.Equal(stored, await database.ReadAllTextAsync());
        Assert.Equal(["1.0.0", "1.1.0"], WeaverEfModule.Chain.ReadableVersions);
    }

    [Theory]
    [MemberData(nameof(AggregateTables))]
    public async Task Each_unknown_aggregate_stamp_remains_unavailable_with_skew_cause_and_no_settlement(string table)
    {
        using var database = new WeaverSqliteDatabase();
        var store = database.RecordStore();
        await Seed(database);
        Assert.True(await database.ExecuteAsync($"UPDATE {table} SET SchemaVersion = '9.0.0'") > 0);
        var stored = await database.ReadAllTextAsync();

        var error = await Assert.ThrowsAsync<WeaverSessionUnavailableException>(() => store.LoadAsync("schema-session"));

        var skew = Assert.IsType<EfSchemaVersionSkewException>(error.InnerException);
        Assert.Equal("Weaver", skew.Family);
        Assert.Equal("9.0.0", skew.Found);
        Assert.Equal(stored, await database.ReadAllTextAsync());
        Assert.Equal(new WeaverSessionOwner("actor-1", "tenant-1"), await store.FindOwnerAsync("schema-session"));
        Assert.Equal("schema-session", await store.FindProposalSessionAsync("schema-proposal"));
    }

    [Theory]
    [InlineData("elsa_weaver_sessions")]
    [InlineData("elsa_weaver_proposals")]
    public async Task Owned_summary_and_pending_count_validate_stamps_after_exact_owner_filter(string table)
    {
        using var database = new WeaverSqliteDatabase();
        var store = database.RecordStore();
        await Seed(database);
        Assert.True(await database.ExecuteAsync($"UPDATE {table} SET SchemaVersion = '9.0.0'") > 0);

        Assert.Empty((await store.ListOwnedAsync(new("other-actor", "tenant-1"))).Items);
        Assert.Empty((await store.ListOwnedAsync(new("actor-1", "other-tenant"))).Items);
        Assert.Empty((await store.ListOwnedAsync(new("actor-1 ", "tenant-1"))).Items);
        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => store.ListOwnedAsync(new("actor-1", "tenant-1")));
    }

    [Fact]
    public async Task An_unknown_proposal_format_cannot_be_silently_classified_as_a_zero_pending_count()
    {
        using var database = new WeaverSqliteDatabase();
        await Seed(database);
        await using (var db = database.Open())
            Assert.Equal(1, await db.Set<WeaverProposalRow>().ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, (int)WeaverActionProposalStatus.Executed)
                .SetProperty(x => x.SchemaVersion, "9.0.0")));
        var store = database.RecordStore();

        Assert.Empty((await store.ListOwnedAsync(new("other-actor", "tenant-1"))).Items);
        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => store.ListOwnedAsync(new("actor-1", "tenant-1")));
    }

    [Theory]
    [InlineData("elsa_weaver_audit")]
    [InlineData("elsa_weaver_audit_owners")]
    public async Task Audit_payloads_and_owned_associations_accept_both_readable_versions_and_refuse_only_owned_skew(string table)
    {
        using var database = new WeaverSqliteDatabase();
        var audit = database.AuditStore();
        Assert.True(await audit.AssociateAsync("schema-session", "actor-1", "tenant-1"));
        await audit.EmitAsync(Audit("first"));
        Assert.True(await database.ExecuteAsync($"UPDATE {table} SET SchemaVersion = '1.1.0'") > 0);
        Assert.Single((await audit.ListOwnedAsync(new("actor-1", "tenant-1", null, 10))).Events);
        Assert.True(await audit.AssociateAsync("schema-session", "actor-1", "tenant-1"));
        Assert.True(await database.ExecuteAsync($"UPDATE {table} SET SchemaVersion = '9.0.0'") > 0);
        var stored = await database.ReadAllTextAsync();

        Assert.Empty((await audit.ListOwnedAsync(new("other-actor", "tenant-1", null, 10))).Events);
        Assert.False(await audit.AssociateAsync("schema-session", "other-actor", "tenant-1"));
        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => audit.ListOwnedAsync(new("actor-1", "tenant-1", null, 10)));
        if (table == "elsa_weaver_audit_owners")
            await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => audit.AssociateAsync("schema-session", "actor-1", "tenant-1"));
        Assert.Equal(stored, await database.ReadAllTextAsync());
    }

    [Fact]
    public async Task An_existing_legacy_counter_keeps_its_stamp_and_a_skewed_counter_rolls_back_allocation()
    {
        using var database = new WeaverSqliteDatabase();
        var audit = database.AuditStore();
        await audit.EmitAsync(Audit("first"));
        await audit.EmitAsync(Audit("second"));
        await using var db = database.Open();
        var counter = await db.Set<WeaverAuditSequenceRow>().AsNoTracking().SingleAsync();
        Assert.Equal(2, counter.Last);
        Assert.Equal("1.0.0", counter.SchemaVersion);
        Assert.Contains("1.0.0", WeaverEfModule.Chain.ReadableVersions);
        Assert.Equal("1.1.0", WeaverEfModule.Chain.CurrentVersion);

        Assert.True(await database.ExecuteAsync("UPDATE elsa_weaver_audit_sequence SET SchemaVersion = '9.0.0'") > 0);
        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => audit.EmitAsync(Audit("rejected")));
        var unchanged = await db.Set<WeaverAuditSequenceRow>().AsNoTracking().SingleAsync();
        Assert.Equal(counter.Last, unchanged.Last);
        Assert.Equal(2, await db.Set<WeaverAuditRow>().CountAsync());

        Assert.True(await database.ExecuteAsync("UPDATE elsa_weaver_audit_sequence SET SchemaVersion = '1.0.0'") > 0);
        await audit.EmitAsync(Audit("third"));
        var next = await db.Set<WeaverAuditSequenceRow>().AsNoTracking().SingleAsync();
        Assert.Equal(3, next.Last);
        Assert.Equal("1.0.0", next.SchemaVersion);
    }

    [Fact]
    public async Task Missing_counter_reconstruction_refuses_unknown_audit_rows_without_allocating_a_position()
    {
        using var database = new WeaverSqliteDatabase();
        var audit = database.AuditStore();
        await audit.EmitAsync(Audit("first"));
        await using var db = database.Open();
        Assert.Equal(1, await db.Set<WeaverAuditSequenceRow>().ExecuteDeleteAsync());
        Assert.True(await database.ExecuteAsync("UPDATE elsa_weaver_audit SET SchemaVersion = '9.0.0'") > 0);

        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => audit.EmitAsync(Audit("rejected")));

        Assert.Equal(0, await db.Set<WeaverAuditSequenceRow>().CountAsync());
        Assert.Equal(1, await db.Set<WeaverAuditRow>().CountAsync());
        Assert.True(await database.ExecuteAsync("UPDATE elsa_weaver_audit SET SchemaVersion = '1.0.0'") > 0);
        await audit.EmitAsync(Audit("second"));
        Assert.Equal(2, (await db.Set<WeaverAuditSequenceRow>().AsNoTracking().SingleAsync()).Last);
    }

    private static async Task Seed(WeaverSqliteDatabase database)
    {
        const string id = "schema-session";
        const string canonical = "{\"text\":\"café \\u2028\",\"amount\":1.50}";
        var digest = WeaverInvocationIntegrity.DigestCanonical(canonical);
        var record = new WeaverSessionRecord(Records.Session(id))
        {
            Messages = [new(Records.Message(id, "schema-message"))],
            Snapshots = [new(digest, canonical)], Context = [Records.Attachment("schema-context")],
            Proposals = [new(Records.Proposal(id, "schema-proposal", WeaverActionProposalStatus.AwaitingApproval))],
            Keys = [new(WeaverSessionKeyKinds.Call, "schema-call", "schema-proposal")],
            PausedTurn = new(new(id, "schema-turn", "schema-proposal", "schema-call", "tool", [], 1)),
            PendingAudits = [Audit("pending")]
        };
        Assert.True((await database.RecordStore().CreateAsync(record, "schema-owner")).Succeeded);
    }

    private static WeaverAuditEvent Audit(string id) => new(id, WeaverAuditEventKind.MessageAccepted, "schema-session", "actor-1",
        "An owned legacy event.", DateTimeOffset.UtcNow, new Dictionary<string, string>());
}
