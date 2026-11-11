using System.Text.Json;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;

namespace Elsa.Weaver.Tests;

/// <summary>#3441 T004: the default record store round-trips aggregates and enforces owner, fence, lease and keys.</summary>
public sealed class WeaverSessionRecordStoreTests
{
    private const string Owner = "owner-a";

    [Fact]
    public async Task A_load_returns_fresh_copies_that_round_trip_every_recorded_part()
    {
        var store = new InMemoryWeaverSessionRecordStore(new ProposalTestClock());
        var session = Session("session-1");
        var message = Message(session.Id, "message-1") with
        {
            Context = [new("attachment-1", "test", null, "Label", "test.kind", WeaverContextSensitivity.Internal, "scope", "Summary",
                new { value = 1.50, text = "kept" }, new Dictionary<string, string> { ["ref"] = "1" })],
            DisclosureHistory = []
        };
        Assert.True((await store.CreateAsync(new(session) { Messages = [new(message)] }, Owner)).Succeeded);

        var first = (await store.LoadAsync(session.Id))!;
        var second = (await store.LoadAsync(session.Id))!;

        Assert.NotSame(session, first.Session);
        Assert.NotSame(first.Messages[0], second.Messages[0]);
        Assert.Equal(WeaverInvocationIntegrity.Digest(session), WeaverInvocationIntegrity.Digest(first.Session));
        Assert.Equal(WeaverInvocationIntegrity.Digest(message), WeaverInvocationIntegrity.Digest(first.Messages[0].Message));
        // Opaque module content is recorded as JSON, never as the caller's object.
        Assert.IsType<JsonElement>(first.Messages[0].Message.Context.Single().Content);
        Assert.Equal(new WeaverSessionFence(Owner, 1), first.Owner);
    }

    /// <summary>A key repeated within one create keeps its first value, as a write does and as the EF store records it.</summary>
    [Fact]
    public async Task A_key_repeated_within_one_create_keeps_its_first_value()
    {
        var store = new InMemoryWeaverSessionRecordStore(new ProposalTestClock());
        var session = Session("session-1");
        Assert.True((await store.CreateAsync(new(session)
        {
            Keys = [new(WeaverSessionKeyKinds.Call, "call-1", "first"), new(WeaverSessionKeyKinds.Call, "call-1", "second")]
        }, Owner)).Succeeded);

        Assert.Equal("first", Assert.Single((await store.LoadAsync(session.Id))!.Keys).Value);
    }

    [Fact]
    public async Task A_write_under_a_stale_fence_or_a_conflicting_key_changes_nothing()
    {
        var clock = new ProposalTestClock();
        var store = new InMemoryWeaverSessionRecordStore(clock);
        var session = Session("session-1");
        var fence = (await store.CreateAsync(new(session), Owner)).Fence!;
        Assert.True((await store.WriteAsync(new(session.Id, fence) { Keys = [new(WeaverSessionKeyKinds.Call, "call-1", "proposal-1")] })).Succeeded);

        // A live lease belongs to its owner; after it lapses, a takeover moves the fence.
        Assert.Equal(WeaverSessionRecordOutcome.OwnedElsewhere, (await store.AcquireOwnershipAsync(session.Id, "owner-b")).Outcome);
        clock.Advance(InMemoryWeaverSessionRecordStore.LeaseDuration + TimeSpan.FromSeconds(1));
        var takeover = await store.AcquireOwnershipAsync(session.Id, "owner-b");
        Assert.Equal(new WeaverSessionFence("owner-b", 2), takeover.Fence);

        var stale = await store.WriteAsync(new(session.Id, fence) { Messages = [new(Message(session.Id, "message-1"))] });
        Assert.Equal(WeaverSessionRecordOutcome.OwnershipLost, stale.Outcome);
        var conflict = await store.WriteAsync(new(session.Id, takeover.Fence!)
        {
            Messages = [new(Message(session.Id, "message-2"))],
            Keys = [new(WeaverSessionKeyKinds.CallInput, "call-1", "input-1"), new(WeaverSessionKeyKinds.Call, "call-1", "proposal-2")]
        });
        Assert.Equal(WeaverSessionRecordOutcome.KeyConflict, conflict.Outcome);
        Assert.Equal("proposal-1", Assert.Single(conflict.ConflictingKeys).Value);

        var recorded = (await store.LoadAsync(session.Id))!;
        Assert.Empty(recorded.Messages);
        Assert.Equal(new WeaverSessionUniqueKey(WeaverSessionKeyKinds.Call, "call-1", "proposal-1"), Assert.Single(recorded.Keys));
        // The same value is idempotent, and the write extends the current owner's lease.
        var repeated = await store.WriteAsync(new(session.Id, takeover.Fence!) { Keys = [new(WeaverSessionKeyKinds.Call, "call-1", "proposal-1")] });
        Assert.True(repeated.Succeeded);
        Assert.Equal(clock.GetUtcNow() + InMemoryWeaverSessionRecordStore.LeaseDuration, repeated.LeaseExpiresAt);
    }

    [Fact]
    public async Task The_owner_re_acquires_its_own_lapsed_lease_without_moving_the_fence()
    {
        var clock = new ProposalTestClock();
        var store = new InMemoryWeaverSessionRecordStore(clock);
        var fence = (await store.CreateAsync(new(Session("session-1")), Owner)).Fence!;
        clock.Advance(TimeSpan.FromHours(1));

        Assert.True((await store.WriteAsync(new("session-1", fence) { Messages = [new(Message("session-1", "message-1"))] })).Succeeded);
        Assert.Equal(fence, (await store.AcquireOwnershipAsync("session-1", Owner)).Fence);
        await store.ReleaseOwnershipAsync("session-1", fence);
        Assert.Equal(new WeaverSessionFence("owner-b", 2), (await store.AcquireOwnershipAsync("session-1", "owner-b")).Fence);
    }

    [Fact]
    public async Task Proposals_resolve_to_their_session_and_a_paused_turn_is_recorded_and_removed()
    {
        var store = new InMemoryWeaverSessionRecordStore();
        var session = Session("session-1");
        var fence = (await store.CreateAsync(new(session), Owner)).Fence!;
        var proposal = Proposal(session.Id, "proposal-1", WeaverActionProposalStatus.AwaitingApproval);
        var paused = new Core.Contracts.WeaverPausedTurn(session.Id, "turn-1", proposal.Id, "call-1", "tool", [], 1);

        Assert.True((await store.WriteAsync(new(session.Id, fence) { Proposals = [new(proposal)], PausedTurn = new(paused) })).Succeeded);
        Assert.Equal(session.Id, await store.FindProposalSessionAsync(proposal.Id));
        Assert.Null(await store.FindProposalSessionAsync("proposal-unknown"));
        Assert.Equal(WeaverInvocationIntegrity.Digest(paused), WeaverInvocationIntegrity.Digest((await store.LoadAsync(session.Id))!.PausedTurn!.Turn));

        Assert.True((await store.WriteAsync(new(session.Id, fence) { RemovePausedTurn = true })).Succeeded);
        Assert.Null((await store.LoadAsync(session.Id))!.PausedTurn);
    }

    [Fact]
    public async Task The_owned_list_scopes_by_owner_and_workspace_before_its_order_cursor_and_limit()
    {
        var store = new InMemoryWeaverSessionRecordStore();
        var start = DateTimeOffset.UtcNow;
        for (var index = 0; index < 3; index++)
            await store.CreateAsync(new(Session($"mine-{index}") with { UpdatedAt = start.AddMinutes(index), Workspace = "workspace-1" }), Owner);
        await store.CreateAsync(new(Session("other-workspace") with { UpdatedAt = start.AddMinutes(10), Workspace = "workspace-2" }), Owner);
        await store.CreateAsync(new(Session("foreign") with { ActorId = "actor-2", UpdatedAt = start.AddMinutes(20), Workspace = "workspace-1" }), Owner);
        var fence = new WeaverSessionFence(Owner, 1);
        await store.WriteAsync(new("mine-1", fence) { Proposals = [new(Proposal("mine-1", "proposal-1", WeaverActionProposalStatus.AwaitingApproval))] });

        var first = await store.ListOwnedAsync(new("actor-1", "tenant-1", "workspace-1", Take: 2));
        Assert.Equal(new[] { "mine-2", "mine-1" }, first.Items.Select(x => x.Id));
        Assert.Equal(1, first.Items[1].PendingReviewCount);
        var second = await store.ListOwnedAsync(new("actor-1", "tenant-1", "workspace-1", Take: 2, Before: first.Next));
        Assert.Equal(new[] { "mine-0" }, second.Items.Select(x => x.Id));
        Assert.Null(second.Next);
        Assert.Equal(4, (await store.ListOwnedAsync(new("actor-1", "tenant-1"))).Items.Count);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ListOwnedAsync(new("actor-1", "tenant-1", Take: WeaverSessionOwnerQuery.MaxTake + 1)));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ListOwnedAsync(new(" ", "tenant-1")));
    }

    internal static WeaverSession Session(string id) => new(id, "Title", "tenant-1", "actor-1", "conversation-1", "provider", "explain",
        WeaverPolicy.Default, WeaverSessionStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(8),
        new Dictionary<string, string>()) { PolicyRevision = 1 };

    private static WeaverMessage Message(string sessionId, string id) => new(id, sessionId, WeaverRole.User, "hello", WeaverMessageStatus.Pending,
        null, DateTimeOffset.UtcNow, null, null, [], [], []) { RequestId = "request-1", HasDisclosureProvenance = true };

    private static WeaverActionProposal Proposal(string sessionId, string id, WeaverActionProposalStatus status) => new(id, sessionId, null, "tool",
        "tool-invocation", "Title", "Summary", WeaverRisk.ReviewRequired, "revision-1", [], [], [], null, [], "test.resource", "resource-1", true,
        status, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
