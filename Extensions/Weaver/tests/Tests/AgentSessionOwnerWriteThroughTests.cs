using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

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

        // An unclaimed proposal never authorizes dispatch (#3441 Phase 3 review); the call still hydrates the session.
        Assert.False(await restarted.MarkProposalDispatchedAsync(session.Id, "proposal-1"));
        // Hydration took the lapsed ownership over: the earlier owner can no longer record anything.
        var current = (await store.LoadAsync(session.Id))!.Owner!;
        Assert.NotEqual(original.OwnerToken, current.OwnerToken);
        Assert.Equal(original.Fence + 1, current.Fence);
        Assert.Equal(AgentSessionOwnershipException.OwnershipLost, (await Assert.ThrowsAsync<AgentSessionOwnershipException>(() => first.AddMessageAsync(session.Id, Prompt("stale")))).Code);
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

        await Assert.ThrowsAsync<AgentSessionOwnershipException>(() => owner.AddMessageAsync(session.Id, Prompt("hello")));
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

        var recorded = Assert.Single((await owner.RecordStore.LoadAsync(session.Id))!.Messages).Message;
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

    // ---- T006: the default paused-turn store lives on the owner's record ----

    [Fact]
    public async Task Saving_the_default_pause_records_it_on_the_session_and_claims_the_pause_in_one_step()
    {
        using var root = AgentTestServices.Create();
        using var scope = root.CreateScope();
        var owner = Assert.IsType<InMemoryAgentSessionService>(scope.ServiceProvider.GetRequiredService<IAgentSessionService>());
        var pauses = Assert.IsType<InMemoryAgentTurnStateStore>(scope.ServiceProvider.GetRequiredService<IAgentTurnStateStore>());
        Assert.Same(owner, pauses.Owner);
        var session = await CreateAsync(owner);
        var paused = new AgentPausedTurn(session.Id, "turn-1", "proposal-1", "call-1", "tool", [new(AgentRole.User, "hello")], 1);

        await pauses.SaveAsync(paused);

        Assert.NotNull(owner.FindPause(session.Id));
        Assert.Equal(AgentSessionPolicyOutcome.Busy,
            (await owner.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, AgentAutonomyMode.Manual, 1))).Outcome);
        var recorded = (await owner.RecordStore.LoadAsync(session.Id))!.PausedTurn!.Turn;
        Assert.Equal(AgentInvocationIntegrity.Digest(paused), AgentInvocationIntegrity.Digest(recorded));

        // After the first owner releases its lease, a host over the same store finds the recorded pause and records its removal.
        await owner.RecordStore.ReleaseOwnershipAsync(session.Id, (await owner.RecordStore.LoadAsync(session.Id))!.Owner!);
        var restarted = new InMemoryAgentTurnStateStore(new InMemoryAgentSessionService(new InMemoryAgentAuditStore(), recordStore: owner.RecordStore));
        Assert.Equal(AgentInvocationIntegrity.Digest(paused), AgentInvocationIntegrity.Digest(await restarted.FindAsync(session.Id)));
        await restarted.RemoveAsync(session.Id);
        Assert.Null((await owner.RecordStore.LoadAsync(session.Id))!.PausedTurn);
    }

    // ---- Review fix: a pause claim that cannot be ensured never strands the turn ----

    [Fact]
    public async Task A_failed_pause_claim_still_unregisters_the_turn_and_releases_its_foreground_claim()
    {
        var store = new TakeoverOnPauseStore(new InMemoryAgentSessionRecordStore());
        using var root = AgentTestServices.Create(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionRecordStore>(store));
            services.AddSingleton<IAgentProvider, DeterministicAgentProvider>();
            services.AddSingleton<IAgentTool>(new ApplyChangeTool());
        });
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAgentSessionService>();
        var streaming = scope.ServiceProvider.GetRequiredService<IAgentStreamingService>();
        var session = await CreateAsync((InMemoryAgentSessionService)sessions);
        await sessions.AddMessageAsync(session.Id, Prompt("mutate the resource"));

        // Another process takes the session over exactly when the pause is recorded.
        await Assert.ThrowsAnyAsync<Exception>(async () => { await foreach (var _ in streaming.StreamAsync(session.Id)) { } });

        Assert.False(scope.ServiceProvider.GetRequiredService<IAgentTurnRegistry>().IsActive(session.Id));
        store.TakenOver = false;
        var next = new List<AgentStreamEvent>();
        await foreach (var item in streaming.StreamAsync(session.Id)) next.Add(item);
        Assert.DoesNotContain(next, x => x.Error?.Code == "weaver.turn.busy");
    }

    // ---- T004/T006: durable recovery support ----

    [Fact]
    public void Durable_recovery_is_supported_by_the_default_composition()
    {
        using var root = AgentTestServices.Create();
        using var scope = root.CreateScope();

        Assert.Equal(AgentDurableRecoverySupport.Supported, scope.ServiceProvider.GetRequiredService<AgentDurableRecovery>().Evaluate());
    }

    [Theory]
    [InlineData("turn-state")]
    [InlineData("unfenced-store")]
    [InlineData("session-service")]
    public async Task Durable_recovery_is_unsupported_for_a_replaced_pause_store_an_unfenced_record_store_or_a_replaced_owner(string replacement)
    {
        using var root = AgentTestServices.Create(services =>
        {
            if (replacement == "turn-state")
                services.Replace(ServiceDescriptor.Singleton<IAgentTurnStateStore>(_ => new InMemoryAgentTurnStateStore()));
            else if (replacement == "unfenced-store")
                services.Replace(ServiceDescriptor.Singleton<IAgentSessionRecordStore>(_ => new UnfencedRecordStore(new InMemoryAgentSessionRecordStore())));
            else
                services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => new AgentSessionPolicyTests.ForwardingSessions(
                    new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>(), sp.GetRequiredService<TimeProvider>()))));
        });
        using var scope = root.CreateScope();
        var services = scope.ServiceProvider;

        Assert.Equal(AgentDurableRecoverySupport.Unsupported, services.GetRequiredService<AgentDurableRecovery>().Evaluate());
        // The in-process behaviour is unchanged: a session is created and read back.
        var sessions = services.GetRequiredService<IAgentSessionService>();
        var session = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", "provider", "explain", null, AgentPolicy.Default,
            new Dictionary<string, string>()));
        Assert.Equal(session.Id, (await sessions.FindAsync(session.Id))!.Id);
    }

    internal static Task<AgentSession> CreateAsync(InMemoryAgentSessionService owner) => owner.CreateAsync(new("tenant-1", "actor-1", "conversation-1",
        "provider", "explain", null, AgentPolicy.Default, new Dictionary<string, string>()));

    internal static AgentMessageCreateRequest Prompt(string content) => new(AgentRole.User, content, AgentMessageStatus.Pending, null, [], []);

    /// <summary>Refuses the write that records a paused turn and then reports a live lease held by another owner.</summary>
    private sealed class TakeoverOnPauseStore(IAgentSessionRecordStore inner) : IAgentSessionRecordStore
    {
        private readonly RefusingRecordStore _forward = new(inner);
        public bool TakenOver { get; set; }
        public bool SupportsFencing => true;
        public Task<AgentSessionRecordResult> CreateAsync(AgentSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => _forward.CreateAsync(record, ownerToken, cancellationToken);
        public Task<AgentSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default) => _forward.LoadAsync(sessionId, cancellationToken);
        public Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
            => TakenOver ? Task.FromResult(new AgentSessionRecordResult(AgentSessionRecordOutcome.OwnedElsewhere))
                : _forward.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
        public Task ReleaseOwnershipAsync(string sessionId, AgentSessionFence fence, CancellationToken cancellationToken = default)
            => _forward.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);
        public Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default)
        {
            if (write.PausedTurn is null && !TakenOver) return _forward.WriteAsync(write, cancellationToken);
            TakenOver = true;
            return Task.FromResult(new AgentSessionRecordResult(AgentSessionRecordOutcome.OwnershipLost));
        }
        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => _forward.FindProposalSessionAsync(proposalId, cancellationToken);
        public Task<AgentSessionSummaryPage> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => _forward.ListOwnedAsync(query, cancellationToken);
    }

    /// <summary>A store that keeps every record but cannot guard its writes on owner and fence.</summary>
    private sealed class UnfencedRecordStore(IAgentSessionRecordStore inner) : IAgentSessionRecordStore
    {
        private readonly RefusingRecordStore _forward = new(inner);
        public bool SupportsFencing => false;
        public Task<AgentSessionRecordResult> CreateAsync(AgentSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => _forward.CreateAsync(record, ownerToken, cancellationToken);
        public Task<AgentSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default) => _forward.LoadAsync(sessionId, cancellationToken);
        public Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
            => _forward.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
        public Task ReleaseOwnershipAsync(string sessionId, AgentSessionFence fence, CancellationToken cancellationToken = default)
            => _forward.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);
        public Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default)
            => _forward.WriteAsync(write, cancellationToken);
        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => _forward.FindProposalSessionAsync(proposalId, cancellationToken);
        public Task<AgentSessionSummaryPage> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => _forward.ListOwnedAsync(query, cancellationToken);
    }

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
