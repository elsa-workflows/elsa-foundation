using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

/// <summary>#3441 T005: the default owner writes through its record store, hydrates before acting and applies only recorded changes.</summary>
public sealed class WeaverSessionOwnerWriteThroughTests
{
    // ---- Causal control: an unloaded row authorized dispatch ----

    [Fact]
    public async Task Dispatch_is_refused_for_a_session_the_owner_cannot_load()
    {
        var owner = new InMemoryWeaverSessionService(new InMemoryWeaverAuditStore());

        // Before hydration-first, an unloaded row returned true and let the executor run without any claim.
        Assert.False(await owner.MarkProposalDispatchedAsync("missing-session", "proposal-1"));
    }

    [Fact]
    public async Task Dispatch_hydrates_a_recorded_session_the_owner_has_not_loaded()
    {
        var clock = new ProposalTestClock();
        var store = new InMemoryWeaverSessionRecordStore(clock);
        var first = new InMemoryWeaverSessionService(new InMemoryWeaverAuditStore(), clock, recordStore: store);
        var session = await CreateAsync(first);
        var original = (await store.LoadAsync(session.Id))!.Owner!;
        clock.Advance(InMemoryWeaverSessionRecordStore.LeaseDuration + TimeSpan.FromSeconds(1));
        var restarted = new InMemoryWeaverSessionService(new InMemoryWeaverAuditStore(), clock, recordStore: store);

        // An unclaimed proposal never authorizes dispatch (#3441 Phase 3 review); the call still hydrates the session.
        Assert.False(await restarted.MarkProposalDispatchedAsync(session.Id, "proposal-1"));
        // Hydration took the lapsed ownership over: the earlier owner can no longer record anything.
        var current = (await store.LoadAsync(session.Id))!.Owner!;
        Assert.NotEqual(original.OwnerToken, current.OwnerToken);
        Assert.Equal(original.Fence + 1, current.Fence);
        Assert.Equal(WeaverSessionOwnershipException.OwnershipLost, (await Assert.ThrowsAsync<WeaverSessionOwnershipException>(() => first.AddMessageAsync(session.Id, Prompt("stale")))).Code);
        Assert.Empty(await restarted.ListMessagesAsync(session.Id));
    }

    // ---- Write-through and hydration ----

    [Fact]
    public async Task A_new_owner_over_the_same_store_hydrates_the_recorded_session_messages_context_and_revision()
    {
        var clock = new ProposalTestClock();
        var store = new InMemoryWeaverSessionRecordStore(clock);
        var first = new InMemoryWeaverSessionService(new InMemoryWeaverAuditStore(), clock, recordStore: store);
        var session = await CreateAsync(first);
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded,
            (await first.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, WeaverAutonomyMode.FullAuto, 1))).Outcome);
        var prompt = await first.AddMessageAsync(session.Id, Prompt("hello"));
        await first.SetMessageStatusAsync(session.Id, prompt.Id, WeaverMessageStatus.Completed);
        WeaverContextAttachment attachment = new("attachment-1", "test", null, "Label", "test.kind", WeaverContextSensitivity.Internal, "scope", "Summary", null,
            new Dictionary<string, string>());
        await first.AddContextAsync(session.Id, [attachment]);
        clock.Advance(InMemoryWeaverSessionRecordStore.LeaseDuration + TimeSpan.FromSeconds(1));

        var restarted = new InMemoryWeaverSessionService(new InMemoryWeaverAuditStore(), clock, recordStore: store);

        var hydrated = (await restarted.FindAsync(session.Id))!;
        Assert.NotSame(session, hydrated);
        Assert.Equal(WeaverAutonomyMode.FullAuto, hydrated.Policy.AutonomyMode);
        Assert.Equal(2, hydrated.PolicyRevision);
        var message = Assert.Single(await restarted.ListMessagesAsync(session.Id));
        Assert.Equal(WeaverMessageStatus.Completed, message.Status);
        Assert.Equal(2, message.AcceptedPolicy!.Revision);
        Assert.Equal("attachment-1", Assert.Single(await restarted.ListContextAsync(session.Id)).Id);
        var policy = await restarted.GetPolicyAsync(session.Id, session.ActorId, session.TenantId);
        Assert.Equal(2, policy.State!.Revision);
        Assert.Null(await restarted.FindAsync("missing-session"));
    }

    [Fact]
    public async Task A_refused_record_write_applies_nothing_in_memory()
    {
        var store = new RefusingRecordStore(new InMemoryWeaverSessionRecordStore());
        var owner = new InMemoryWeaverSessionService(new InMemoryWeaverAuditStore(), recordStore: store);
        var session = await CreateAsync(owner);
        store.Refuse = true;

        await Assert.ThrowsAsync<WeaverSessionOwnershipException>(() => owner.AddMessageAsync(session.Id, Prompt("hello")));
        store.Refuse = false;

        // The refused write evicted the row; the next operation hydrates the recorded state, which has no message.
        Assert.Empty(await owner.ListMessagesAsync(session.Id));
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded, (await owner.GetPolicyAsync(session.Id, session.ActorId, session.TenantId)).Outcome);
        Assert.Equal(1, store.Refused);
    }

    [Fact]
    public async Task A_message_status_compare_and_set_changes_only_the_expected_status()
    {
        var owner = new InMemoryWeaverSessionService(new InMemoryWeaverAuditStore());
        var session = await CreateAsync(owner);
        var prompt = await owner.AddMessageAsync(session.Id, Prompt("hello"));

        Assert.False(await owner.TrySetMessageStatusAsync(session.Id, prompt.Id, WeaverMessageStatus.Streaming, WeaverMessageStatus.Completed));
        Assert.Equal(WeaverMessageStatus.Pending, (await owner.FindMessageAsync(session.Id, prompt.Id))!.Status);
        Assert.True(await owner.TrySetMessageStatusAsync(session.Id, prompt.Id, WeaverMessageStatus.Pending, WeaverMessageStatus.Cancelled, "withdrawn"));
        Assert.False(await owner.TrySetMessageStatusAsync(session.Id, prompt.Id, WeaverMessageStatus.Pending, WeaverMessageStatus.Streaming));

        var recorded = Assert.Single((await owner.RecordStore.LoadAsync(session.Id))!.Messages).Message;
        Assert.Equal(WeaverMessageStatus.Cancelled, recorded.Status);
        Assert.Equal("withdrawn", recorded.StatusReason);
        // The withdrawn prompt released its own claim; the session is idle again.
        Assert.Equal(WeaverSessionPolicyOutcome.Succeeded,
            (await owner.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, WeaverAutonomyMode.Manual, 1))).Outcome);
    }

    [Fact]
    public async Task Workspace_and_continuation_are_stamped_from_the_create_request()
    {
        var owner = new InMemoryWeaverSessionService(new InMemoryWeaverAuditStore());
        var session = await owner.CreateAsync(new("tenant-1", "actor-1", "conversation-1", "provider", "explain", null, WeaverPolicy.Default,
            new Dictionary<string, string>()) { Workspace = "workspace-1", ContinuedFrom = "session-0" });

        var recorded = (await owner.RecordStore.LoadAsync(session.Id))!.Session;
        Assert.Equal(("workspace-1", "session-0"), (recorded.Workspace, recorded.ContinuedFrom));
    }

    // ---- T006: the default paused-turn store lives on the owner's record ----

    [Fact]
    public async Task Saving_the_default_pause_records_it_on_the_session_and_claims_the_pause_in_one_step()
    {
        using var root = WeaverTestServices.Create();
        using var scope = root.CreateScope();
        var owner = Assert.IsType<InMemoryWeaverSessionService>(scope.ServiceProvider.GetRequiredService<IWeaverSessionService>());
        var pauses = Assert.IsType<InMemoryWeaverTurnStateStore>(scope.ServiceProvider.GetRequiredService<IWeaverTurnStateStore>());
        Assert.Same(owner, pauses.Owner);
        var session = await CreateAsync(owner);
        var paused = new WeaverPausedTurn(session.Id, "turn-1", "proposal-1", "call-1", "tool", [new(WeaverRole.User, "hello")], 1);

        await pauses.SaveAsync(paused);

        Assert.NotNull(owner.FindPause(session.Id));
        Assert.Equal(WeaverSessionPolicyOutcome.Busy,
            (await owner.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, WeaverAutonomyMode.Manual, 1))).Outcome);
        var recorded = (await owner.RecordStore.LoadAsync(session.Id))!.PausedTurn!.Turn;
        Assert.Equal(WeaverInvocationIntegrity.Digest(paused), WeaverInvocationIntegrity.Digest(recorded));

        // After the first owner releases its lease, a host over the same store finds the recorded pause and records its removal.
        await owner.RecordStore.ReleaseOwnershipAsync(session.Id, (await owner.RecordStore.LoadAsync(session.Id))!.Owner!);
        var restarted = new InMemoryWeaverTurnStateStore(new InMemoryWeaverSessionService(new InMemoryWeaverAuditStore(), recordStore: owner.RecordStore));
        Assert.Equal(WeaverInvocationIntegrity.Digest(paused), WeaverInvocationIntegrity.Digest(await restarted.FindAsync(session.Id)));
        await restarted.RemoveAsync(session.Id);
        Assert.Null((await owner.RecordStore.LoadAsync(session.Id))!.PausedTurn);
    }

    // ---- Review fix: a pause claim that cannot be ensured never strands the turn ----

    [Fact]
    public async Task A_failed_pause_claim_still_unregisters_the_turn_and_releases_its_foreground_claim()
    {
        var store = new TakeoverOnPauseStore(new InMemoryWeaverSessionRecordStore());
        using var root = WeaverTestServices.Create(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IWeaverSessionRecordStore>(store));
            services.AddSingleton<IWeaverProvider, DeterministicWeaverProvider>();
            services.AddSingleton<IWeaverTool>(new ApplyChangeTool());
        });
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        var streaming = scope.ServiceProvider.GetRequiredService<IWeaverStreamingService>();
        var session = await CreateAsync((InMemoryWeaverSessionService)sessions);
        await sessions.AddMessageAsync(session.Id, Prompt("mutate the resource"));

        // Another process takes the session over exactly when the pause is recorded.
        await Assert.ThrowsAnyAsync<Exception>(async () => { await foreach (var _ in streaming.StreamAsync(session.Id)) { } });

        Assert.False(scope.ServiceProvider.GetRequiredService<IWeaverTurnRegistry>().IsActive(session.Id));
        store.TakenOver = false;
        var next = new List<WeaverStreamEvent>();
        await foreach (var item in streaming.StreamAsync(session.Id)) next.Add(item);
        Assert.DoesNotContain(next, x => x.Error?.Code == "weaver.turn.busy");
    }

    // ---- T004/T006: durable recovery support ----

    [Fact]
    public void Durable_recovery_is_supported_by_the_default_composition()
    {
        using var root = WeaverTestServices.Create();
        using var scope = root.CreateScope();

        Assert.Equal(WeaverDurableRecoverySupport.Supported, scope.ServiceProvider.GetRequiredService<WeaverDurableRecovery>().Evaluate());
    }

    [Theory]
    [InlineData("turn-state")]
    [InlineData("unfenced-store")]
    [InlineData("session-service")]
    public async Task Durable_recovery_is_unsupported_for_a_replaced_pause_store_an_unfenced_record_store_or_a_replaced_owner(string replacement)
    {
        using var root = WeaverTestServices.Create(services =>
        {
            if (replacement == "turn-state")
                services.Replace(ServiceDescriptor.Singleton<IWeaverTurnStateStore>(_ => new InMemoryWeaverTurnStateStore()));
            else if (replacement == "unfenced-store")
                services.Replace(ServiceDescriptor.Singleton<IWeaverSessionRecordStore>(_ => new UnfencedRecordStore(new InMemoryWeaverSessionRecordStore())));
            else
                services.Replace(ServiceDescriptor.Singleton<IWeaverSessionService>(sp => new WeaverSessionPolicyTests.ForwardingSessions(
                    new InMemoryWeaverSessionService(sp.GetRequiredService<IWeaverAuditSink>(), sp.GetRequiredService<TimeProvider>()))));
        });
        using var scope = root.CreateScope();
        var services = scope.ServiceProvider;

        Assert.Equal(WeaverDurableRecoverySupport.Unsupported, services.GetRequiredService<WeaverDurableRecovery>().Evaluate());
        // The in-process behaviour is unchanged: a session is created and read back.
        var sessions = services.GetRequiredService<IWeaverSessionService>();
        var session = await sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", "provider", "explain", null, WeaverPolicy.Default,
            new Dictionary<string, string>()));
        Assert.Equal(session.Id, (await sessions.FindAsync(session.Id))!.Id);
    }

    internal static Task<WeaverSession> CreateAsync(InMemoryWeaverSessionService owner) => owner.CreateAsync(new("tenant-1", "actor-1", "conversation-1",
        "provider", "explain", null, WeaverPolicy.Default, new Dictionary<string, string>()));

    internal static WeaverMessageCreateRequest Prompt(string content) => new(WeaverRole.User, content, WeaverMessageStatus.Pending, null, [], []);

    /// <summary>Refuses the write that records a paused turn and then reports a live lease held by another owner.</summary>
    private sealed class TakeoverOnPauseStore(IWeaverSessionRecordStore inner) : IWeaverSessionRecordStore
    {
        private readonly RefusingRecordStore _forward = new(inner);
        public bool TakenOver { get; set; }
        public bool SupportsFencing => true;
        public Task<WeaverSessionRecordResult> CreateAsync(WeaverSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => _forward.CreateAsync(record, ownerToken, cancellationToken);
        public Task<WeaverSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default) => _forward.LoadAsync(sessionId, cancellationToken);
        public Task<WeaverSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
            => TakenOver ? Task.FromResult(new WeaverSessionRecordResult(WeaverSessionRecordOutcome.OwnedElsewhere))
                : _forward.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
        public Task ReleaseOwnershipAsync(string sessionId, WeaverSessionFence fence, CancellationToken cancellationToken = default)
            => _forward.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);
        public Task<WeaverSessionRecordResult> WriteAsync(WeaverSessionRecordWrite write, CancellationToken cancellationToken = default)
        {
            if (write.PausedTurn is null && !TakenOver) return _forward.WriteAsync(write, cancellationToken);
            TakenOver = true;
            return Task.FromResult(new WeaverSessionRecordResult(WeaverSessionRecordOutcome.OwnershipLost));
        }
        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => _forward.FindProposalSessionAsync(proposalId, cancellationToken);
        public Task<WeaverSessionSummaryPage> ListOwnedAsync(WeaverSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => _forward.ListOwnedAsync(query, cancellationToken);
    }

    /// <summary>A store that keeps every record but cannot guard its writes on owner and fence.</summary>
    private sealed class UnfencedRecordStore(IWeaverSessionRecordStore inner) : IWeaverSessionRecordStore
    {
        private readonly RefusingRecordStore _forward = new(inner);
        public bool SupportsFencing => false;
        public Task<WeaverSessionRecordResult> CreateAsync(WeaverSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => _forward.CreateAsync(record, ownerToken, cancellationToken);
        public Task<WeaverSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default) => _forward.LoadAsync(sessionId, cancellationToken);
        public Task<WeaverSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
            => _forward.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
        public Task ReleaseOwnershipAsync(string sessionId, WeaverSessionFence fence, CancellationToken cancellationToken = default)
            => _forward.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);
        public Task<WeaverSessionRecordResult> WriteAsync(WeaverSessionRecordWrite write, CancellationToken cancellationToken = default)
            => _forward.WriteAsync(write, cancellationToken);
        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => _forward.FindProposalSessionAsync(proposalId, cancellationToken);
        public Task<WeaverSessionSummaryPage> ListOwnedAsync(WeaverSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => _forward.ListOwnedAsync(query, cancellationToken);
    }

    /// <summary>A store whose writes can be refused as if another process had taken the session over.</summary>
    internal sealed class RefusingRecordStore(IWeaverSessionRecordStore inner) : IWeaverSessionRecordStore
    {
        public bool Refuse { get; set; }
        public int Refused { get; private set; }
        public bool SupportsFencing => inner.SupportsFencing;
        public Task<WeaverSessionRecordResult> CreateAsync(WeaverSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => inner.CreateAsync(record, ownerToken, cancellationToken);
        public Task<WeaverSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default) => inner.LoadAsync(sessionId, cancellationToken);
        public Task<WeaverSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
            => inner.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
        public Task ReleaseOwnershipAsync(string sessionId, WeaverSessionFence fence, CancellationToken cancellationToken = default)
            => inner.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);
        public Task<WeaverSessionRecordResult> WriteAsync(WeaverSessionRecordWrite write, CancellationToken cancellationToken = default)
        {
            if (!Refuse) return inner.WriteAsync(write, cancellationToken);
            Refused++;
            return Task.FromResult(new WeaverSessionRecordResult(WeaverSessionRecordOutcome.OwnershipLost));
        }
        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => inner.FindProposalSessionAsync(proposalId, cancellationToken);
        public Task<WeaverSessionSummaryPage> ListOwnedAsync(WeaverSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => inner.ListOwnedAsync(query, cancellationToken);
    }
}
