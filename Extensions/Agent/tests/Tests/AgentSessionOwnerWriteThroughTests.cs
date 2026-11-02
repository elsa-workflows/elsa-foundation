using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;

namespace Elsa.Agent.Tests;

/// <summary>#3441 T005: the default owner writes through its record store, hydrates before acting and applies only recorded changes.</summary>
public sealed class AgentSessionOwnerWriteThroughTests
{
    // ---- Causal control: an unloaded row authorized dispatch ----

    [Fact]
    public async Task Dispatch_is_refused_for_a_session_the_owner_cannot_load()
    {
        var owner = new InMemoryAgentSessionService(new InMemoryAgentAuditStore());

        // Before hydration-first, an unloaded row returned true and let the executor run without any claim.
        Assert.False(await owner.MarkProposalDispatchedAsync("missing-session", "proposal-1"));
    }

    [Fact]
    public async Task Dispatch_hydrates_a_recorded_session_the_owner_has_not_loaded()
    {
        var clock = new ProposalTestClock();
        var store = new InMemoryAgentSessionRecordStore(clock);
        var first = new InMemoryAgentSessionService(new InMemoryAgentAuditStore(), clock, recordStore: store);
        var session = await CreateAsync(first);
        var original = (await store.LoadAsync(session.Id))!.Owner!;
        clock.Advance(InMemoryAgentSessionRecordStore.LeaseDuration + TimeSpan.FromSeconds(1));
        var restarted = new InMemoryAgentSessionService(new InMemoryAgentAuditStore(), clock, recordStore: store);

        Assert.True(await restarted.MarkProposalDispatchedAsync(session.Id, "proposal-1"));
        // Hydration took the lapsed ownership over: the earlier owner can no longer record anything.
        var current = (await store.LoadAsync(session.Id))!.Owner!;
        Assert.NotEqual(original.OwnerToken, current.OwnerToken);
        Assert.Equal(original.Fence + 1, current.Fence);
        await Assert.ThrowsAsync<AgentContributionException>(() => first.AddMessageAsync(session.Id, Prompt("stale")));
        Assert.Empty(await restarted.ListMessagesAsync(session.Id));
    }

    // ---- Write-through and hydration ----

    [Fact]
    public async Task A_new_owner_over_the_same_store_hydrates_the_recorded_session_messages_context_and_revision()
    {
        var clock = new ProposalTestClock();
        var store = new InMemoryAgentSessionRecordStore(clock);
        var first = new InMemoryAgentSessionService(new InMemoryAgentAuditStore(), clock, recordStore: store);
        var session = await CreateAsync(first);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded,
            (await first.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, AgentAutonomyMode.FullAuto, 1))).Outcome);
        var prompt = await first.AddMessageAsync(session.Id, Prompt("hello"));
        await first.SetMessageStatusAsync(session.Id, prompt.Id, AgentMessageStatus.Completed);
        AgentContextAttachment attachment = new("attachment-1", "test", null, "Label", "test.kind", AgentContextSensitivity.Internal, "scope", "Summary", null,
            new Dictionary<string, string>());
        await first.AddContextAsync(session.Id, [attachment]);
        clock.Advance(InMemoryAgentSessionRecordStore.LeaseDuration + TimeSpan.FromSeconds(1));

        var restarted = new InMemoryAgentSessionService(new InMemoryAgentAuditStore(), clock, recordStore: store);

        var hydrated = (await restarted.FindAsync(session.Id))!;
        Assert.NotSame(session, hydrated);
        Assert.Equal(AgentAutonomyMode.FullAuto, hydrated.Policy.AutonomyMode);
        Assert.Equal(2, hydrated.PolicyRevision);
        var message = Assert.Single(await restarted.ListMessagesAsync(session.Id));
        Assert.Equal(AgentMessageStatus.Completed, message.Status);
        Assert.Equal(2, message.AcceptedPolicy!.Revision);
        Assert.Equal("attachment-1", Assert.Single(await restarted.ListContextAsync(session.Id)).Id);
        var policy = await restarted.GetPolicyAsync(session.Id, session.ActorId, session.TenantId);
        Assert.Equal(2, policy.State!.Revision);
        Assert.Null(await restarted.FindAsync("missing-session"));
    }

    [Fact]
    public async Task A_refused_record_write_applies_nothing_in_memory()
    {
        var store = new RefusingRecordStore(new InMemoryAgentSessionRecordStore());
        var owner = new InMemoryAgentSessionService(new InMemoryAgentAuditStore(), recordStore: store);
        var session = await CreateAsync(owner);
        store.Refuse = true;

        await Assert.ThrowsAsync<AgentContributionException>(() => owner.AddMessageAsync(session.Id, Prompt("hello")));
        store.Refuse = false;

        // The refused write evicted the row; the next operation hydrates the recorded state, which has no message.
        Assert.Empty(await owner.ListMessagesAsync(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await owner.GetPolicyAsync(session.Id, session.ActorId, session.TenantId)).Outcome);
        Assert.Equal(1, store.Refused);
    }

    [Fact]
    public async Task A_message_status_compare_and_set_changes_only_the_expected_status()
    {
        var owner = new InMemoryAgentSessionService(new InMemoryAgentAuditStore());
        var session = await CreateAsync(owner);
        var prompt = await owner.AddMessageAsync(session.Id, Prompt("hello"));

        Assert.False(await owner.TrySetMessageStatusAsync(session.Id, prompt.Id, AgentMessageStatus.Streaming, AgentMessageStatus.Completed));
        Assert.Equal(AgentMessageStatus.Pending, (await owner.FindMessageAsync(session.Id, prompt.Id))!.Status);
        Assert.True(await owner.TrySetMessageStatusAsync(session.Id, prompt.Id, AgentMessageStatus.Pending, AgentMessageStatus.Cancelled, "withdrawn"));
        Assert.False(await owner.TrySetMessageStatusAsync(session.Id, prompt.Id, AgentMessageStatus.Pending, AgentMessageStatus.Streaming));

        var recorded = Assert.Single((await owner.RecordStore.LoadAsync(session.Id))!.Messages);
        Assert.Equal(AgentMessageStatus.Cancelled, recorded.Status);
        Assert.Equal("withdrawn", recorded.StatusReason);
        // The withdrawn prompt released its own claim; the session is idle again.
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded,
            (await owner.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, AgentAutonomyMode.Manual, 1))).Outcome);
    }

    [Fact]
    public async Task Workspace_and_continuation_are_stamped_from_the_create_request()
    {
        var owner = new InMemoryAgentSessionService(new InMemoryAgentAuditStore());
        var session = await owner.CreateAsync(new("tenant-1", "actor-1", "conversation-1", "provider", "explain", null, AgentPolicy.Default,
            new Dictionary<string, string>()) { Workspace = "workspace-1", ContinuedFrom = "session-0" });

        var recorded = (await owner.RecordStore.LoadAsync(session.Id))!.Session;
        Assert.Equal(("workspace-1", "session-0"), (recorded.Workspace, recorded.ContinuedFrom));
    }

    internal static Task<AgentSession> CreateAsync(InMemoryAgentSessionService owner) => owner.CreateAsync(new("tenant-1", "actor-1", "conversation-1",
        "provider", "explain", null, AgentPolicy.Default, new Dictionary<string, string>()));

    internal static AgentMessageCreateRequest Prompt(string content) => new(AgentRole.User, content, AgentMessageStatus.Pending, null, [], []);

    /// <summary>A store whose writes can be refused as if another process had taken the session over.</summary>
    internal sealed class RefusingRecordStore(IAgentSessionRecordStore inner) : IAgentSessionRecordStore
    {
        public bool Refuse { get; set; }
        public int Refused { get; private set; }
        public bool SupportsFencing => inner.SupportsFencing;
        public Task<AgentSessionRecordResult> CreateAsync(AgentSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => inner.CreateAsync(record, ownerToken, cancellationToken);
        public Task<AgentSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default) => inner.LoadAsync(sessionId, cancellationToken);
        public Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
            => inner.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
        public Task ReleaseOwnershipAsync(string sessionId, AgentSessionFence fence, CancellationToken cancellationToken = default)
            => inner.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);
        public Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default)
        {
            if (!Refuse) return inner.WriteAsync(write, cancellationToken);
            Refused++;
            return Task.FromResult(new AgentSessionRecordResult(AgentSessionRecordOutcome.OwnershipLost));
        }
        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => inner.FindProposalSessionAsync(proposalId, cancellationToken);
        public Task<AgentSessionSummaryPage> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => inner.ListOwnedAsync(query, cancellationToken);
    }
}
