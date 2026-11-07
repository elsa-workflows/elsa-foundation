using Elsa.Agent.Core.Models;
using Elsa.Agent.Persistence.EntityFrameworkCore;
using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Agent.Services;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Persistence.Tests;

/// <summary>Reader-only expansion: real protected legacy aggregates, mixed readable stamps, and owner-first projections.</summary>
public sealed class AgentSchemaReadabilityTests
{
    public static TheoryData<string> AggregateTables => new()
    {
        "elsa_agent_sessions", "elsa_agent_messages", "elsa_agent_disclosure_snapshots",
        "elsa_agent_context", "elsa_agent_proposals", "elsa_agent_call_keys"
    };

    [Theory]
    [MemberData(nameof(AggregateTables))]
    public async Task Each_readable_new_stamp_can_mix_with_real_protected_legacy_rows_without_rewriting_content(string table)
    {
        using var database = new AgentSqliteDatabase();
        var store = database.RecordStore();
        await Seed(database);
        var before = (await store.LoadAsync("schema-session"))!;
        Assert.True(await database.ExecuteAsync($"UPDATE {table} SET SchemaVersion = '1.1.0'") > 0);
        var stored = await database.ReadAllTextAsync();

        var read = (await store.LoadAsync(before.Session.Id))!;

        Assert.Equal(AgentInvocationIntegrity.Digest(before), AgentInvocationIntegrity.Digest(read));
        Assert.Equal(before.Snapshots.Single().Canonical, read.Snapshots.Single().Canonical);
        Assert.Equal("schema-proposal", read.PausedTurn!.Turn.ProposalId);
        Assert.Equal("pending", Assert.Single(read.PendingAudits).Id);
        Assert.Equal(stored, await database.ReadAllTextAsync());
        Assert.Equal(["1.0.0", "1.1.0"], AgentEfModule.Chain.ReadableVersions);
    }

    [Theory]
    [MemberData(nameof(AggregateTables))]
    public async Task Each_unknown_aggregate_stamp_remains_unavailable_with_skew_cause_and_no_settlement(string table)
    {
        using var database = new AgentSqliteDatabase();
        var store = database.RecordStore();
        await Seed(database);
        Assert.True(await database.ExecuteAsync($"UPDATE {table} SET SchemaVersion = '9.0.0'") > 0);
        var stored = await database.ReadAllTextAsync();

        var error = await Assert.ThrowsAsync<AgentSessionUnavailableException>(() => store.LoadAsync("schema-session"));

        var skew = Assert.IsType<EfSchemaVersionSkewException>(error.InnerException);
        Assert.Equal("Agent", skew.Family);
        Assert.Equal("9.0.0", skew.Found);
        Assert.Equal(stored, await database.ReadAllTextAsync());
        Assert.Equal(new AgentSessionOwner("actor-1", "tenant-1"), await store.FindOwnerAsync("schema-session"));
        Assert.Equal("schema-session", await store.FindProposalSessionAsync("schema-proposal"));
    }

    [Theory]
    [InlineData("elsa_agent_sessions")]
    [InlineData("elsa_agent_proposals")]
    public async Task Owned_summary_and_pending_count_validate_stamps_after_exact_owner_filter(string table)
    {
        using var database = new AgentSqliteDatabase();
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
        using var database = new AgentSqliteDatabase();
        await Seed(database);
        await using (var db = database.Open())
            Assert.Equal(1, await db.Set<AgentProposalRow>().ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, (int)AgentActionProposalStatus.Executed)
                .SetProperty(x => x.SchemaVersion, "9.0.0")));
        var store = database.RecordStore();

        Assert.Empty((await store.ListOwnedAsync(new("other-actor", "tenant-1"))).Items);
        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => store.ListOwnedAsync(new("actor-1", "tenant-1")));
    }

    [Theory]
    [InlineData("elsa_agent_audit")]
    [InlineData("elsa_agent_audit_owners")]
    public async Task Audit_payloads_and_owned_associations_accept_both_readable_versions_and_refuse_only_owned_skew(string table)
    {
        using var database = new AgentSqliteDatabase();
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
        if (table == "elsa_agent_audit_owners")
            await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => audit.AssociateAsync("schema-session", "actor-1", "tenant-1"));
        Assert.Equal(stored, await database.ReadAllTextAsync());
    }

    [Fact]
    public async Task An_existing_legacy_counter_keeps_its_stamp_and_a_skewed_counter_rolls_back_allocation()
    {
        using var database = new AgentSqliteDatabase();
        var audit = database.AuditStore();
        await audit.EmitAsync(Audit("first"));
        await audit.EmitAsync(Audit("second"));
        await using var db = database.Open();
        var counter = await db.Set<AgentAuditSequenceRow>().AsNoTracking().SingleAsync();
        Assert.Equal(2, counter.Last);
        Assert.Equal("1.0.0", counter.SchemaVersion);
        Assert.Contains("1.0.0", AgentEfModule.Chain.ReadableVersions);
        Assert.Equal("1.1.0", AgentEfModule.Chain.CurrentVersion);

        Assert.True(await database.ExecuteAsync("UPDATE elsa_agent_audit_sequence SET SchemaVersion = '9.0.0'") > 0);
        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => audit.EmitAsync(Audit("rejected")));
        var unchanged = await db.Set<AgentAuditSequenceRow>().AsNoTracking().SingleAsync();
        Assert.Equal(counter.Last, unchanged.Last);
        Assert.Equal(2, await db.Set<AgentAuditRow>().CountAsync());

        Assert.True(await database.ExecuteAsync("UPDATE elsa_agent_audit_sequence SET SchemaVersion = '1.0.0'") > 0);
        await audit.EmitAsync(Audit("third"));
        var next = await db.Set<AgentAuditSequenceRow>().AsNoTracking().SingleAsync();
        Assert.Equal(3, next.Last);
        Assert.Equal("1.0.0", next.SchemaVersion);
    }

    [Fact]
    public async Task Missing_counter_reconstruction_refuses_unknown_audit_rows_without_allocating_a_position()
    {
        using var database = new AgentSqliteDatabase();
        var audit = database.AuditStore();
        await audit.EmitAsync(Audit("first"));
        await using var db = database.Open();
        Assert.Equal(1, await db.Set<AgentAuditSequenceRow>().ExecuteDeleteAsync());
        Assert.True(await database.ExecuteAsync("UPDATE elsa_agent_audit SET SchemaVersion = '9.0.0'") > 0);

        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => audit.EmitAsync(Audit("rejected")));

        Assert.Equal(0, await db.Set<AgentAuditSequenceRow>().CountAsync());
        Assert.Equal(1, await db.Set<AgentAuditRow>().CountAsync());
        Assert.True(await database.ExecuteAsync("UPDATE elsa_agent_audit SET SchemaVersion = '1.0.0'") > 0);
        await audit.EmitAsync(Audit("second"));
        Assert.Equal(2, (await db.Set<AgentAuditSequenceRow>().AsNoTracking().SingleAsync()).Last);
    }

    private static async Task Seed(AgentSqliteDatabase database)
    {
        const string id = "schema-session";
        const string canonical = "{\"text\":\"café \\u2028\",\"amount\":1.50}";
        var digest = AgentInvocationIntegrity.DigestCanonical(canonical);
        var record = new AgentSessionRecord(Records.Session(id))
        {
            Messages = [new(Records.Message(id, "schema-message"))],
            Snapshots = [new(digest, canonical)], Context = [Records.Attachment("schema-context")],
            Proposals = [new(Records.Proposal(id, "schema-proposal", AgentActionProposalStatus.AwaitingApproval))],
            Keys = [new(AgentSessionKeyKinds.Call, "schema-call", "schema-proposal")],
            PausedTurn = new(new(id, "schema-turn", "schema-proposal", "schema-call", "tool", [], 1)),
            PendingAudits = [Audit("pending")]
        };
        Assert.True((await database.RecordStore().CreateAsync(record, "schema-owner")).Succeeded);
    }

    private static AgentAuditEvent Audit(string id) => new(id, AgentAuditEventKind.MessageAccepted, "schema-session", "actor-1",
        "An owned legacy event.", DateTimeOffset.UtcNow, new Dictionary<string, string>());
}
