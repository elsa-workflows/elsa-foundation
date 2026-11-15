using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Stores;
using Elsa.Weaver.Services;
using Elsa.Weaver.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Weaver.Persistence.Tests;

/// <summary>#3441 T028: the EF record store keeps the default store's contract on SQLite with the actual migrations.</summary>
public sealed class EfWeaverSessionRecordStoreTests
{
    private const string Owner = "owner-a";

    [Fact]
    public void The_restart_scenarios_in_this_assembly_run_over_the_EF_store()
    {
        using var harness = new WeaverRestartHarness();
        Assert.IsType<EfWeaverSessionRecordStore>(harness.Store);
        Assert.True(harness.Store.SupportsFencing);
    }

    [Fact]
    public void A_restart_scenario_disposes_its_database_when_it_ends()
    {
        string path;
        Elsa.Weaver.Core.Contracts.IWeaverSessionRecordStore store;
        using (var harness = new WeaverRestartHarness())
        {
            store = harness.Store;
            path = EfRestartComposition.DatabaseOf(store).Path;
            Assert.True(File.Exists(path));
        }

        // The database file and its context provider are gone, and the composition no longer tracks them.
        Assert.False(File.Exists(path));
        Assert.Throws<KeyNotFoundException>(() => EfRestartComposition.DatabaseOf(store));
    }

    [Fact]
    public async Task A_key_repeated_within_one_create_keeps_its_first_value()
    {
        using var database = new WeaverSqliteDatabase();
        var store = database.RecordStore();
        var session = Records.Session("session-1");
        Assert.True((await store.CreateAsync(new(session)
        {
            Keys = [new(WeaverSessionKeyKinds.Call, "call-1", "first"), new(WeaverSessionKeyKinds.Call, "call-1", "second")]
        }, Owner)).Succeeded);

        Assert.Equal("first", Assert.Single((await store.LoadAsync(session.Id))!.Keys).Value);
    }

    [Fact]
    public async Task A_new_store_over_the_same_database_reads_every_part_with_byte_identical_digests_and_fingerprints()
    {
        using var database = new WeaverSqliteDatabase();
        var clock = new RestartTestClock();
        // Canonical text with escapes, non-ASCII and a separator character, digested exactly as the owner does.
        var canonical = "{\"a\":1.50,\"b\":\"café \\u2028 \u2028 ñ\",\"c\":[true,null]}";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var session = Records.Session("session-1") with { Workspace = "workspace-1", ContinuedFrom = "session-0", PolicyRevision = 3 };
        var message = new WeaverMessageRecord(Records.Message(session.Id, "message-1") with
        {
            DeliveryFingerprint = "delivery-fingerprint-1", StatusReason = null,
            Context = [Records.Attachment("attachment-in-message")]
        }) { DisclosureDigests = [digest, digest], DisclosureHistoryFingerprint = "history-fingerprint-1" };
        var attachment = Records.Attachment(new string('x', 300));
        var proposal = new WeaverProposalRecord(Records.Proposal(session.Id, "proposal-1", WeaverActionProposalStatus.AwaitingApproval))
        { AcknowledgementDigest = "acknowledgement-1" };
        var paused = new WeaverPausedTurnRecord(new WeaverPausedTurn(session.Id, "turn-1", "proposal-1", "call-1", "tool", [], 1)
        { DisclosureHistoryFingerprint = "pause-fingerprint-1" }) { DisclosureDigests = [digest] };
        var keys = new WeaverSessionUniqueKey[]
        {
            new(WeaverSessionKeyKinds.Call, session.Id + ":turn-1:" + new string('k', 600), "proposal-1"),
            new(WeaverSessionKeyKinds.Invocation, "invocation-1", "proposal-1"),
            new(WeaverSessionKeyKinds.TurnMutation, "turn-1", "call-key-1"),
            new(WeaverSessionKeyKinds.CallInput, "call-key-1", "input-digest-1")
        };

        var created = await database.RecordStore(clock).CreateAsync(new(session)
        {
            Messages = [message], Snapshots = [new(digest, canonical)], Context = [attachment], PausedTurn = paused, Proposals = [proposal], Keys = keys
        }, Owner);
        Assert.Equal(new WeaverSessionFence(Owner, 1), created.Fence);
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromSeconds(30), created.LeaseExpiresAt);

        // Another store instance over its own connections: nothing is shared but the database.
        using var restarted = database.NewContexts();
        var loaded = (await database.RecordStore(clock, contexts: restarted).LoadAsync(session.Id))!;

        Assert.Equal(WeaverInvocationIntegrity.Digest(session), WeaverInvocationIntegrity.Digest(loaded.Session));
        Assert.Equal(WeaverInvocationIntegrity.Digest(message), WeaverInvocationIntegrity.Digest(Assert.Single(loaded.Messages)));
        Assert.Equal("delivery-fingerprint-1", loaded.Messages[0].Message.DeliveryFingerprint);
        Assert.Equal("history-fingerprint-1", loaded.Messages[0].DisclosureHistoryFingerprint);
        Assert.Equal([digest, digest], loaded.Messages[0].DisclosureDigests!);
        var snapshot = Assert.Single(loaded.Snapshots);
        Assert.Equal(digest, snapshot.Digest);
        Assert.Equal(canonical, snapshot.Canonical);
        Assert.Equal(digest, WeaverInvocationIntegrity.DigestCanonical(snapshot.Canonical));
        Assert.Equal(WeaverInvocationIntegrity.Digest(attachment), WeaverInvocationIntegrity.Digest(Assert.Single(loaded.Context)));
        Assert.Equal(WeaverInvocationIntegrity.Digest(paused), WeaverInvocationIntegrity.Digest(loaded.PausedTurn));
        Assert.Equal("pause-fingerprint-1", loaded.PausedTurn!.Turn.DisclosureHistoryFingerprint);
        var recordedProposal = Assert.Single(loaded.Proposals);
        Assert.Equal(1, recordedProposal.Version);
        Assert.Equal(WeaverInvocationIntegrity.Digest(proposal with { Version = 1 }), WeaverInvocationIntegrity.Digest(recordedProposal));
        Assert.Equal(keys, loaded.Keys);
        Assert.Equal(new WeaverSessionFence(Owner, 1), loaded.Owner);
        Assert.Equal(1, loaded.Version);
    }

    [Fact]
    public async Task Writes_upsert_messages_and_proposals_and_record_snapshots_context_and_keys_once_in_first_insertion_order()
    {
        using var database = new WeaverSqliteDatabase();
        var store = database.RecordStore(new RestartTestClock());
        var session = Records.Session("session-1");
        var fence = (await store.CreateAsync(new(session), Owner)).Fence!;

        Assert.True((await store.WriteAsync(new(session.Id, fence)
        {
            Messages = [new(Records.Message(session.Id, "message-1")), new(Records.Message(session.Id, "message-2"))],
            Snapshots = [new("digest-1", "{\"v\":1}")], Context = [Records.Attachment("attachment-1")],
            Keys = [new(WeaverSessionKeyKinds.Call, "call-1", "proposal-1")]
        })).Succeeded);
        Assert.True((await store.WriteAsync(new(session.Id, fence)
        {
            Session = session with { Title = "Renamed", Status = WeaverSessionStatus.Expired },
            Messages = [new(Records.Message(session.Id, "message-1") with { Content = "edited", Status = WeaverMessageStatus.Cancelled, StatusReason = "withdrawn" }),
                new(Records.Message(session.Id, "message-3"))],
            // A digest or attachment already recorded keeps its recorded value; a repeated key with the same value is idempotent.
            Snapshots = [new("digest-1", "{\"v\":\"other\"}"), new("digest-2", "{\"v\":2}")],
            Context = [Records.Attachment("attachment-1") with { Label = "changed" }, Records.Attachment("attachment-2")],
            Keys = [new(WeaverSessionKeyKinds.Call, "call-1", "proposal-1"), new(WeaverSessionKeyKinds.CallInput, "call-1", "input-1")]
        })).Succeeded);

        var loaded = (await store.LoadAsync(session.Id))!;
        Assert.Equal(["message-1", "message-2", "message-3"], loaded.Messages.Select(x => x.Message.Id));
        Assert.Equal("edited", loaded.Messages[0].Message.Content);
        Assert.Equal("withdrawn", loaded.Messages[0].Message.StatusReason);
        Assert.Equal(["digest-1", "digest-2"], loaded.Snapshots.Select(x => x.Digest));
        Assert.Equal("{\"v\":1}", loaded.Snapshots[0].Canonical);
        Assert.Equal(["Label", "Label"], loaded.Context.Select(x => x.Label));
        Assert.Equal([WeaverSessionKeyKinds.Call, WeaverSessionKeyKinds.CallInput], loaded.Keys.Select(x => x.Kind));
        Assert.Equal("Renamed", loaded.Session.Title);
        Assert.Equal(WeaverSessionStatus.Expired, loaded.Session.Status);
        Assert.Equal(3, loaded.Version);
    }

    [Fact]
    public async Task A_conflicting_key_or_a_proposal_of_another_session_refuses_the_whole_write()
    {
        using var database = new WeaverSqliteDatabase();
        var store = database.RecordStore(new RestartTestClock());
        var first = (await store.CreateAsync(new(Records.Session("session-1")), Owner)).Fence!;
        var second = (await store.CreateAsync(new(Records.Session("session-2")), Owner)).Fence!;
        Assert.True((await store.WriteAsync(new("session-1", first)
        {
            Keys = [new(WeaverSessionKeyKinds.Call, "call-1", "proposal-1")],
            Proposals = [new(Records.Proposal("session-1", "proposal-1", WeaverActionProposalStatus.AwaitingApproval))]
        })).Succeeded);

        var conflict = await store.WriteAsync(new("session-1", first)
        {
            Messages = [new(Records.Message("session-1", "message-1"))],
            Keys = [new(WeaverSessionKeyKinds.CallInput, "call-1", "input-1"), new(WeaverSessionKeyKinds.Call, "call-1", "proposal-2")]
        });
        Assert.Equal(WeaverSessionRecordOutcome.KeyConflict, conflict.Outcome);
        Assert.Equal(new WeaverSessionUniqueKey(WeaverSessionKeyKinds.Call, "call-1", "proposal-1"), Assert.Single(conflict.ConflictingKeys));
        // The same key is free in another session; a proposal identity is not.
        Assert.True((await store.WriteAsync(new("session-2", second) { Keys = [new(WeaverSessionKeyKinds.Call, "call-1", "proposal-9")] })).Succeeded);
        var foreign = await store.WriteAsync(new("session-2", second)
        {
            Messages = [new(Records.Message("session-2", "message-1"))],
            Proposals = [new(Records.Proposal("session-2", "proposal-1", WeaverActionProposalStatus.AwaitingApproval))]
        });
        Assert.Equal(WeaverSessionRecordOutcome.AlreadyExists, foreign.Outcome);
        Assert.Equal(WeaverSessionRecordOutcome.AlreadyExists,
            (await store.CreateAsync(new(Records.Session("session-3")) { Proposals = [new(Records.Proposal("session-3", "proposal-1", WeaverActionProposalStatus.AwaitingApproval))] }, Owner)).Outcome);
        Assert.Equal(WeaverSessionRecordOutcome.AlreadyExists, (await store.CreateAsync(new(Records.Session("session-1")), Owner)).Outcome);

        var one = (await store.LoadAsync("session-1"))!;
        Assert.Empty(one.Messages);
        Assert.Equal(new WeaverSessionUniqueKey(WeaverSessionKeyKinds.Call, "call-1", "proposal-1"), Assert.Single(one.Keys));
        Assert.Empty((await store.LoadAsync("session-2"))!.Messages);
        Assert.Null(await store.LoadAsync("session-3"));
        Assert.Equal("session-1", await store.FindProposalSessionAsync("proposal-1"));
        Assert.Null(await store.FindProposalSessionAsync("proposal-unknown"));
    }

    [Fact]
    public async Task A_proposal_record_changes_only_from_the_version_its_writer_read()
    {
        using var database = new WeaverSqliteDatabase();
        var store = database.RecordStore(new RestartTestClock());
        var fence = (await store.CreateAsync(new(Records.Session("session-1")), Owner)).Fence!;
        var proposal = Records.Proposal("session-1", "proposal-1", WeaverActionProposalStatus.AwaitingApproval);

        Assert.True((await store.WriteAsync(new("session-1", fence) { Proposals = [new(proposal)] })).Succeeded);
        // A second writer that also decided on "not yet recorded" loses.
        Assert.Equal(WeaverSessionRecordOutcome.VersionConflict,
            (await store.WriteAsync(new("session-1", fence) { Proposals = [new(proposal with { Status = WeaverActionProposalStatus.Denied })] })).Outcome);
        Assert.True((await store.WriteAsync(new("session-1", fence) { Proposals = [new(proposal with { Status = WeaverActionProposalStatus.Approved }) { Version = 1 }] })).Succeeded);
        Assert.Equal(WeaverSessionRecordOutcome.VersionConflict,
            (await store.WriteAsync(new("session-1", fence) { Proposals = [new(proposal with { Status = WeaverActionProposalStatus.Denied }) { Version = 1 }] })).Outcome);

        var recorded = Assert.Single((await store.LoadAsync("session-1"))!.Proposals);
        Assert.Equal(2, recorded.Version);
        Assert.Equal(WeaverActionProposalStatus.Approved, recorded.Proposal.Status);
    }

    [Fact]
    public async Task Concurrent_writers_serialize_on_the_session_and_exactly_one_proposal_decision_wins()
    {
        using var database = new WeaverSqliteDatabase();
        var clock = new RestartTestClock();
        var fence = (await database.RecordStore(clock).CreateAsync(new(Records.Session("session-1"))
        {
            Proposals = [new(Records.Proposal("session-1", "proposal-1", WeaverActionProposalStatus.AwaitingApproval))]
        }, Owner)).Fence!;
        // Separate store instances with separate connections, as competing request threads or processes would be.
        var writers = Enumerable.Range(0, 6).Select(_ => database.NewContexts()).ToArray();
        try
        {
            var messages = await Task.WhenAll(writers.SelectMany((contexts, writer) => Enumerable.Range(0, 3).Select(index =>
                database.RecordStore(clock, contexts: contexts).WriteAsync(new("session-1", fence) { Messages = [new(Records.Message("session-1", $"message-{writer}-{index}"))] }))));
            Assert.All(messages, x => Assert.True(x.Succeeded));
            var decisions = await Task.WhenAll(writers.Select((contexts, writer) => database.RecordStore(clock, contexts: contexts).WriteAsync(new("session-1", fence)
            {
                Proposals = [new(Records.Proposal("session-1", "proposal-1", writer % 2 == 0 ? WeaverActionProposalStatus.Approved : WeaverActionProposalStatus.Denied)) { Version = 1 }]
            })));
            Assert.Single(decisions, x => x.Succeeded);
            Assert.Equal(5, decisions.Count(x => x.Outcome == WeaverSessionRecordOutcome.VersionConflict));

            var loaded = (await database.RecordStore(clock).LoadAsync("session-1"))!;
            Assert.Equal(18, loaded.Messages.Count);
            Assert.Equal(2, Assert.Single(loaded.Proposals).Version);
            Assert.Equal(1 + 18 + 1, loaded.Version);
        }
        finally
        {
            foreach (var contexts in writers) contexts.Dispose();
        }
    }

    [Fact]
    public async Task Ownership_follows_the_owner_fence_and_lease_and_a_write_extends_the_lease()
    {
        using var database = new WeaverSqliteDatabase();
        var clock = new RestartTestClock();
        var store = database.RecordStore(clock);
        var other = database.RecordStore(clock, contexts: database.NewContexts());
        var fence = (await store.CreateAsync(new(Records.Session("session-1")), Owner)).Fence!;

        var elsewhere = await other.AcquireOwnershipAsync("session-1", "owner-b");
        Assert.Equal(WeaverSessionRecordOutcome.OwnedElsewhere, elsewhere.Outcome);
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromSeconds(30), elsewhere.LeaseExpiresAt);
        Assert.Equal(WeaverSessionRecordOutcome.NotFound, (await other.AcquireOwnershipAsync("session-unknown", "owner-b")).Outcome);
        Assert.Equal(WeaverSessionRecordOutcome.NotFound, (await store.WriteAsync(new("session-unknown", fence))).Outcome);

        // A write extends the owner's lease from the store's time.
        clock.Advance(TimeSpan.FromSeconds(20));
        var written = await store.WriteAsync(new("session-1", fence) { Messages = [new(Records.Message("session-1", "message-1"))] });
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromSeconds(30), written.LeaseExpiresAt);
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(WeaverSessionRecordOutcome.OwnedElsewhere, (await other.AcquireOwnershipAsync("session-1", "owner-b")).Outcome);

        // The owner re-acquires its own lapsed lease without moving the fence; a write re-acquires it as well.
        clock.Advance(TimeSpan.FromHours(1));
        Assert.True((await store.WriteAsync(new("session-1", fence) { Messages = [new(Records.Message("session-1", "message-2"))] })).Succeeded);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(fence, (await store.AcquireOwnershipAsync("session-1", Owner)).Fence);

        // After the lease lapses another owner takes over with a new fence; the old fence then changes nothing.
        clock.Advance(TimeSpan.FromSeconds(31));
        var takeover = await other.AcquireOwnershipAsync("session-1", "owner-b");
        Assert.Equal(new WeaverSessionFence("owner-b", 2), takeover.Fence);
        Assert.Equal(WeaverSessionRecordOutcome.OwnershipLost,
            (await store.WriteAsync(new("session-1", fence) { Messages = [new(Records.Message("session-1", "message-3"))] })).Outcome);
        await store.ReleaseOwnershipAsync("session-1", fence);
        Assert.Equal(WeaverSessionRecordOutcome.OwnedElsewhere, (await store.AcquireOwnershipAsync("session-1", Owner)).Outcome);

        // A release by the current owner lets the next owner in at once.
        await other.ReleaseOwnershipAsync("session-1", takeover.Fence!);
        var loaded = (await store.LoadAsync("session-1"))!;
        Assert.Null(loaded.LeaseExpiresAt);
        Assert.Equal(["message-1", "message-2"], loaded.Messages.Select(x => x.Message.Id));
        Assert.Equal(new WeaverSessionFence(Owner, 3), (await store.AcquireOwnershipAsync("session-1", Owner)).Fence);
    }

    [Fact]
    public async Task The_lease_is_measured_in_database_time()
    {
        var clockReads = new ClockReadCounter();
        using var database = new WeaverSqliteDatabase();
        using var contexts = WeaverSqliteDatabase.Contexts(database.ConnectionString, builder => builder.AddInterceptors(clockReads));
        // No process clock: the store asks the database.
        var store = database.RecordStore(contexts: contexts);
        var before = DateTimeOffset.UtcNow;
        var created = await store.CreateAsync(new(Records.Session("session-1")), Owner);
        var written = await store.WriteAsync(new("session-1", created.Fence!) { Messages = [new(Records.Message("session-1", "message-1"))] });
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(2, clockReads.Count);
        // SQLite's clock has millisecond precision.
        Assert.InRange(written.LeaseExpiresAt!.Value, before.AddSeconds(30).AddMilliseconds(-1), after.AddSeconds(30).AddMilliseconds(1));
        Assert.True(written.LeaseExpiresAt >= created.LeaseExpiresAt);
    }

    [Fact]
    public async Task The_owned_list_scopes_by_owner_and_workspace_before_its_order_cursor_and_limit_without_decoding_payloads()
    {
        using var database = new WeaverSqliteDatabase();
        var store = database.RecordStore(new RestartTestClock());
        var start = DateTimeOffset.UtcNow;
        for (var index = 0; index < 3; index++)
            await store.CreateAsync(new(Records.Session($"mine-{index}") with { UpdatedAt = start.AddMinutes(index), Workspace = "workspace-1" }), Owner);
        // Equal update times order by session identity, ordinally.
        await store.CreateAsync(new(Records.Session("mine-B") with { UpdatedAt = start.AddMinutes(-1), Workspace = "workspace-1" }), Owner);
        await store.CreateAsync(new(Records.Session("mine-a") with { UpdatedAt = start.AddMinutes(-1), Workspace = "workspace-1" }), Owner);
        await store.CreateAsync(new(Records.Session("other-workspace") with { UpdatedAt = start.AddMinutes(10), Workspace = "workspace-2" }), Owner);
        await store.CreateAsync(new(Records.Session("foreign-actor") with { ActorId = "actor-2", UpdatedAt = start.AddMinutes(20), Workspace = "workspace-1" }), Owner);
        await store.CreateAsync(new(Records.Session("foreign-tenant") with { TenantId = "tenant-2", UpdatedAt = start.AddMinutes(30), Workspace = "workspace-1" }), Owner);
        await store.WriteAsync(new("mine-1", new(Owner, 1)) { Proposals = [new(Records.Proposal("mine-1", "proposal-1", WeaverActionProposalStatus.AwaitingApproval)),
            new(Records.Proposal("mine-1", "proposal-2", WeaverActionProposalStatus.Edited)), new(Records.Proposal("mine-1", "proposal-3", WeaverActionProposalStatus.Executed))] });
        // A corrupt payload does not affect the list: it reads plain columns only.
        await database.ExecuteAsync("UPDATE elsa_weaver_sessions SET Payload = 'not-ciphertext' WHERE SessionId = 'mine-0'");

        var pages = new List<WeaverSessionSummaryPage>();
        string? next = null;
        do
        {
            var page = await store.ListOwnedAsync(new("actor-1", "tenant-1", "workspace-1", Take: 1, Before: next));
            pages.Add(page);
            next = page.Next;
        } while (next is not null);

        Assert.Equal(["mine-2", "mine-1", "mine-0", "mine-a", "mine-B"], pages.Select(x => Assert.Single(x.Items).Id));
        Assert.Equal(2, pages[1].Items[0].PendingReviewCount);
        Assert.Equal("Title", pages[2].Items[0].Title);
        Assert.Equal(start.AddMinutes(2).UtcTicks, pages[0].Items[0].UpdatedAt.UtcTicks);
        Assert.Equal(6, (await store.ListOwnedAsync(new("actor-1", "tenant-1"))).Items.Count);
        Assert.Equal(new WeaverSessionOwner("actor-1", "tenant-1"), await store.FindOwnerAsync("mine-0"));
        Assert.Null(await store.FindOwnerAsync("unknown"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ListOwnedAsync(new("actor-1", "tenant-1", Take: WeaverSessionOwnerQuery.MaxTake + 1)));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ListOwnedAsync(new("actor-1", "tenant-1", Take: 0)));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ListOwnedAsync(new(" ", "tenant-1")));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ListOwnedAsync(new("actor-1", "tenant-1", Before: "not-a-cursor")));
    }

    private sealed class ClockReadCounter : DbCommandInterceptor
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("weaver.utc-clock", StringComparison.Ordinal)) Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}

internal static class Records
{
    public static WeaverSession Session(string id) => new(id, "Title", "tenant-1", "actor-1", "conversation-1", "provider", "explain",
        WeaverPolicy.Default, WeaverSessionStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(8),
        new Dictionary<string, string> { ["origin"] = "test" }) { PolicyRevision = 1 };

    public static WeaverMessage Message(string sessionId, string id, string content = "hello") => new(id, sessionId, WeaverRole.User, content,
        WeaverMessageStatus.Pending, null, DateTimeOffset.UtcNow, null, null, [], [], []) { RequestId = "request-1", HasDisclosureProvenance = true };

    public static WeaverContextAttachment Attachment(string id, object? content = null) => new(id, "test", null, "Label", "test.kind",
        WeaverContextSensitivity.Internal, "scope", "Summary", content ?? new { value = 1.50, text = "kept" }, new Dictionary<string, string> { ["ref"] = "1" });

    public static WeaverActionProposal Proposal(string sessionId, string id, WeaverActionProposalStatus status) => new(id, sessionId, null, "tool",
        "tool-invocation", "Title", "Summary", WeaverRisk.ReviewRequired, "revision-1", [], [], [], null, [], "test.resource", "resource-1", true,
        status, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
