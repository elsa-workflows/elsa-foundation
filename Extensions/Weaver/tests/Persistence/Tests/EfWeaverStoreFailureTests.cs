using System.Text.Json;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Services;
using Elsa.Weaver.Tests;
using Elsa.Persistence.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Weaver.Persistence.Tests;

/// <summary>
/// #3441 T028: unreadable, corrupt or foreign recorded state is FR-018 unavailable with its cause kept, and every
/// infrastructure failure leaves the store wrapped (§2.23.5). None of them changes the recorded state.
/// </summary>
public sealed class EfWeaverStoreFailureTests
{
    private const string Owner = "owner-a";

    public static TheoryData<string, string, bool> Tampering() => new()
    {
        { "tampered session ciphertext", "UPDATE elsa_weaver_sessions SET Payload = substr(Payload, 1, length(Payload) - 6) || 'AAAAAA'", false },
        { "a payload that is not ciphertext", "UPDATE elsa_weaver_messages SET Payload = 'plain text'", false },
        { "ciphertext moved to another row", "UPDATE elsa_weaver_messages SET Payload = (SELECT Payload FROM elsa_weaver_messages WHERE MessageId = 'message-1') WHERE MessageId = 'message-2'", false },
        { "a plain column that disagrees with its record", "UPDATE elsa_weaver_messages SET Status = 99 WHERE MessageId = 'message-1'", false },
        { "a snapshot whose recorded size disagrees", "UPDATE elsa_weaver_disclosure_snapshots SET Size = Size + 1", false },
        { "snapshot text moved to another digest of the same size", "UPDATE elsa_weaver_disclosure_snapshots SET Canonical = (SELECT Canonical FROM elsa_weaver_disclosure_snapshots WHERE Digest = 'digest-1') WHERE Digest = 'digest-2'", false },
        { "a key whose hash disagrees", "UPDATE elsa_weaver_call_keys SET Key = 'call-2'", false },
        { "a schema version this build does not read", "UPDATE elsa_weaver_proposals SET SchemaVersion = '9.0.0'", true }
    };

    [Theory]
    [MemberData(nameof(Tampering))]
    public async Task Unreadable_state_is_unavailable_with_its_cause_and_changes_nothing(string _, string tamper, bool schemaSkew)
    {
        using var database = new WeaverSqliteDatabase();
        var store = database.RecordStore(new RestartTestClock());
        await SeedAsync(store);
        Assert.True(await database.ExecuteAsync(tamper) > 0);
        var before = await database.ReadAllTextAsync();

        var failure = await Assert.ThrowsAsync<WeaverSessionUnavailableException>(() => store.LoadAsync("session-1"));

        Assert.Equal("unreadable", failure.Reason);
        Assert.Equal("session-1", failure.SessionId);
        // Shared chain readability distinguishes a future format from corrupt authenticated content.
        if (schemaSkew)
        {
            var skew = Assert.IsType<EfSchemaVersionSkewException>(failure.InnerException);
            Assert.Equal("Weaver", skew.Family);
            Assert.Equal("9.0.0", skew.Found);
        }
        else Assert.IsType<InvalidDataException>(failure.InnerException);
        Assert.Equal(before, await database.ReadAllTextAsync());
        // Authorization still reads the plain owner columns.
        Assert.Equal(new WeaverSessionOwner("actor-1", "tenant-1"), await store.FindOwnerAsync("session-1"));
    }

    [Fact]
    public async Task Records_protected_with_keys_this_host_does_not_hold_are_unavailable()
    {
        using var database = new WeaverSqliteDatabase();
        await SeedAsync(database.RecordStore(new RestartTestClock()));
        var foreignKeys = new WeaverRecordCodec(new Elsa.Secrets.Services.DefaultSecretValueProtector(new TestKeyRing()));

        var failure = await Assert.ThrowsAsync<WeaverSessionUnavailableException>(() =>
            database.RecordStore(new RestartTestClock(), foreignKeys).LoadAsync("session-1"));

        Assert.Equal("unreadable", failure.Reason);
        Assert.NotNull(failure.InnerException!.InnerException);
    }

    [Fact]
    public async Task Authenticated_text_that_is_not_the_recorded_type_is_unavailable_with_the_JSON_failure_inside()
    {
        using var database = new WeaverSqliteDatabase();
        var store = database.RecordStore(new RestartTestClock());
        await SeedAsync(store);
        var forged = database.Codec.ProtectText(WeaverRecordCodec.MessagePurpose, "session-1", "message-1", "{\"Message\":");
        await using (var db = database.Open())
            await db.Database.ExecuteSqlAsync($"UPDATE elsa_weaver_messages SET Payload = {forged} WHERE MessageId = 'message-1'");

        var failure = await Assert.ThrowsAsync<WeaverSessionUnavailableException>(() => store.LoadAsync("session-1"));

        Assert.IsType<JsonException>(failure.InnerException!.InnerException);
    }

    [Fact]
    public async Task The_owner_hydrates_unreadable_state_as_unavailable_and_gives_back_the_lease_it_took()
    {
        using var database = new WeaverSqliteDatabase();
        using var harness = new WeaverRestartHarness(clock => database.RecordStore(clock));
        var session = await harness.Host.CreateAsync();
        await harness.Host.PostAsync(session, "hello");
        await database.ExecuteAsync("UPDATE elsa_weaver_messages SET Payload = 'not-ciphertext'");
        var before = await database.ReadAllTextAsync();

        var host = harness.Restart();
        var failure = await Assert.ThrowsAsync<WeaverSessionUnavailableException>(() => host.Sessions.FindAsync(session.Id));

        Assert.Equal("unreadable", failure.Reason);
        Assert.IsType<InvalidDataException>(failure.InnerException);
        // Nothing was settled: the only change is the lease this hydration took and then released.
        Assert.Equal(before.Where(x => x.Column != "OwnerToken"), (await database.ReadAllTextAsync()).Where(x => x.Column != "OwnerToken"));
        await using var db = database.Open();
        Assert.Null(await db.Database.SqlQueryRaw<long?>("SELECT LeaseExpiresAtUtcTicks AS Value FROM elsa_weaver_sessions").SingleAsync());
    }

    [Fact]
    public async Task A_failed_database_write_is_wrapped_with_its_cause_and_applies_nothing()
    {
        using var database = new WeaverSqliteDatabase();
        var clock = new RestartTestClock();
        var fence = (await database.RecordStore(clock).CreateAsync(new(Records.Session("session-1")), Owner)).Fence!;
        using var failing = WeaverSqliteDatabase.Contexts(database.ConnectionString, builder => builder.AddInterceptors(new FailingSave()));

        var failure = await Assert.ThrowsAsync<WeaverContributionException>(() => database.RecordStore(clock, contexts: failing)
            .WriteAsync(new("session-1", fence) { Messages = [new(Records.Message("session-1", "message-1"))] }));

        Assert.IsType<DbUpdateException>(failure.InnerException);
        Assert.Contains("session-1", failure.Message, StringComparison.Ordinal);
        var recorded = (await database.RecordStore(clock).LoadAsync("session-1"))!;
        Assert.Empty(recorded.Messages);
        Assert.Equal(1, recorded.Version);
    }

    [Fact]
    public async Task An_unavailable_database_and_an_unrecordable_value_are_wrapped_with_their_cause()
    {
        using var database = new WeaverSqliteDatabase();
        var clock = new RestartTestClock();
        var store = database.RecordStore(clock);
        var fence = (await store.CreateAsync(new(Records.Session("session-1")), Owner)).Fence!;
        var cycle = new Cycle();
        cycle.Self = cycle;

        var encoding = await Assert.ThrowsAsync<WeaverContributionException>(() =>
            store.WriteAsync(new("session-1", fence) { Context = [Records.Attachment("attachment-1", cycle)] }));
        Assert.IsType<JsonException>(encoding.InnerException);

        using var missing = WeaverSqliteDatabase.Contexts($"Data Source={Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "absent.db")};Mode=ReadOnly");
        var unavailable = await Assert.ThrowsAsync<WeaverContributionException>(() => database.RecordStore(clock, contexts: missing).LoadAsync("session-1"));
        Assert.IsType<SqliteException>(unavailable.InnerException);
        Assert.Empty((await store.LoadAsync("session-1"))!.Context);
    }

    private static async Task SeedAsync(Elsa.Weaver.Core.Contracts.IWeaverSessionRecordStore store)
    {
        var session = Records.Session("session-1");
        await store.CreateAsync(new(session)
        {
            Messages = [new(Records.Message(session.Id, "message-1")), new(Records.Message(session.Id, "message-2"))],
            Snapshots = [new("digest-1", "{\"v\":1}"), new("digest-2", "{\"v\":2}")],
            Proposals = [new(Records.Proposal(session.Id, "proposal-1", WeaverActionProposalStatus.AwaitingApproval))],
            Keys = [new(WeaverSessionKeyKinds.Call, "call-1", "proposal-1")]
        }, Owner);
    }

    private sealed class Cycle
    {
        public Cycle? Self { get; set; }
    }

    private sealed class FailingSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => throw new DbUpdateException("The database refused the write.");
    }
}
