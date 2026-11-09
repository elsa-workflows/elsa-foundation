using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Elsa.Api.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 T018 (story 1, FR-010, FR-013, contract §2): the routes a returning owner uses to find a session again and give
/// each recovered item its explicit outcome: the owner-scoped session list, the session's proposals, withdrawing a
/// prompt that never started, the owner's resolution of an uncertain client operation, and a user stop.
/// </summary>
public sealed class AgentRecoveryApiTests
{
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(30);

    // ---- GET /sessions ----

    [Fact]
    public async Task The_session_list_is_scoped_to_the_owner_before_its_limit_and_narrowed_by_workspace()
    {
        using var harness = Harness();
        var host = harness.Host;
        var older = await CreateAsync(harness, "actor-1", "tenant-1", "ws-a");
        var otherWorkspace = await CreateAsync(harness, "actor-1", "tenant-1", "ws-b");
        var newer = await CreateAsync(harness, "actor-1", "tenant-1", "ws-a", continuedFrom: older);
        // Newer sessions of another actor and another tenant must never take a slot of the owner's page.
        await CreateAsync(harness, "actor-2", "tenant-1", "ws-a");
        await CreateAsync(harness, "actor-1", "tenant-2", "ws-a");
        var owner = AgentRecoveryApi.Principal("actor-1", "tenant-1");

        var first = await ListAsync(host, owner, "?workspace=ws-a&take=1");
        Assert.Equal([newer], Ids(first));
        var second = await ListAsync(host, owner, "?workspace=ws-a&take=1&before=" + Uri.EscapeDataString(first.GetProperty("next").GetString()!));
        Assert.Equal([older], Ids(second));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("next").ValueKind);

        var all = new List<string>();
        string? cursor = null;
        do
        {
            var page = await ListAsync(host, owner, "?take=1" + (cursor is null ? string.Empty : "&before=" + Uri.EscapeDataString(cursor)));
            all.AddRange(Ids(page));
            cursor = page.GetProperty("next").GetString();
        } while (cursor is not null);
        Assert.Equal([newer, otherWorkspace, older], all);

        var item = (await ListAsync(host, owner, "?workspace=ws-a&take=1")).GetProperty("items")[0];
        Assert.Equal("active", item.GetProperty("status").GetString());
        Assert.Equal(0, item.GetProperty("pendingReviewCount").GetInt32());
        // The create request stamps the workspace and the continuation link, and nothing else is carried over.
        var continued = (await host.Sessions.FindAsync(newer))!;
        Assert.Equal(("ws-a", older), (continued.Workspace, continued.ContinuedFrom));
        Assert.Empty(await host.Sessions.ListMessagesAsync(newer));
    }

    [Fact]
    public async Task The_session_list_refuses_an_unresolved_caller_a_foreign_cursor_and_an_unsupported_service()
    {
        using var harness = Harness();
        var (unresolved, unresolvedBody) = await AgentRecoveryApi.SendAsync(harness.Host, "ListSessions", AgentRecoveryApi.Principal(null, "tenant-1"));
        AgentRecoveryApi.AssertFailure(unresolved, unresolvedBody, 403, "agent.actor.unresolved");

        var (cursor, cursorBody) = await AgentRecoveryApi.SendAsync(harness.Host, "ListSessions", AgentRecoveryApi.Principal("actor-1", "tenant-1"), query: "?before=not-a-cursor");
        Assert.Equal(400, cursor);
        Assert.Contains("before", cursorBody, StringComparison.Ordinal);

        using var unsupported = new AgentRestartHarness(configure: services =>
        {
            services.AddRouting().AddElsaEndpoints();
            services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp =>
                new AgentSessionPolicyTests.ForwardingSessions(ActivatorUtilities.CreateInstance<InMemoryAgentSessionService>(sp))));
        });
        var (refused, refusedBody) = await AgentRecoveryApi.SendAsync(unsupported.Host, "ListSessions", AgentRecoveryApi.Principal("actor-1", "tenant-1"));
        AgentRecoveryApi.AssertFailure(refused, refusedBody, 409, "agent.session.query_unsupported");
    }

    [Fact]
    public async Task A_create_request_whose_workspace_or_continuation_link_exceeds_its_limit_is_refused_before_anything_is_recorded()
    {
        using var harness = Harness();
        var owner = AgentRecoveryApi.Principal("actor-1", "tenant-1");
        var audits = harness.Host.Services.GetRequiredService<IAgentAuditReader>();

        var (workspace, workspaceBody) = await AgentRecoveryApi.SendAsync(harness.Host, "CreateSession", owner,
            body: new { conversationId = "conversation", workspace = new string('w', AgentSessionCreateRequest.MaxWorkspaceLength + 1) });
        AgentRecoveryApi.AssertFailure(workspace, workspaceBody, 400, "agent.session.invalid_request");
        var (link, linkBody) = await AgentRecoveryApi.SendAsync(harness.Host, "CreateSession", owner,
            body: new { conversationId = "conversation", continuedFrom = new string('s', AgentSessionCreateRequest.MaxContinuedFromLength + 1) });
        AgentRecoveryApi.AssertFailure(link, linkBody, 400, "agent.session.invalid_request");

        Assert.Empty(Ids(await ListAsync(harness.Host, owner, "")));
        Assert.Empty(await audits.ListAsync());
        // The owner refuses the same values whatever its record store, before it associates an audit owner.
        await Assert.ThrowsAsync<ArgumentException>(() => harness.Host.Sessions.CreateAsync(new("tenant-1", "actor-1", "conversation", DeterministicAgentProvider.Id,
            "explain", null, AgentPolicy.Default, new Dictionary<string, string>()) { Workspace = new string('w', AgentSessionCreateRequest.MaxWorkspaceLength + 1) }));
        Assert.Empty(Ids(await ListAsync(harness.Host, owner, "")));
        // Values at the limit are accepted.
        Assert.NotNull(await CreateAsync(harness, "actor-1", "tenant-1", new string('w', AgentSessionCreateRequest.MaxWorkspaceLength)));
    }

    [Fact]
    public async Task A_session_that_reached_its_limit_reports_it_on_its_detail_and_in_the_list()
    {
        using var harness = Harness();
        var host = harness.Host;
        var session = await host.CreateAsync();
        var (fresh, freshBody) = await AgentRecoveryApi.SendAsync(host, "GetSession", AgentRecoveryApi.Owner(session), Session(session));
        Assert.Equal(200, fresh);
        Assert.False(JsonDocument.Parse(freshBody).RootElement.GetProperty("data").TryGetProperty("limitReached", out _));

        await host.Owner.RecordLimitAsync(session.Id, "snapshots");

        var (status, body) = await AgentRecoveryApi.SendAsync(host, "GetSession", AgentRecoveryApi.Owner(session), Session(session));
        Assert.Equal(200, status);
        Assert.Equal("snapshots", JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("limitReached").GetString());
        var item = (await ListAsync(host, AgentRecoveryApi.Owner(session), "")).GetProperty("items")[0];
        Assert.Equal("limit-reached:snapshots", item.GetProperty("recoveryState").GetString());
    }

    // ---- GET /sessions/{id}/proposals ----

    [Fact]
    public async Task The_proposals_route_returns_each_proposal_with_its_status_preview_and_deadline_after_a_restart()
    {
        using var harness = Harness();
        var session = await harness.Host.CreateAsync();
        var proposal = await harness.Host.PauseOnReviewAsync(session);
        var host = harness.Restart();

        var (status, body) = await AgentRecoveryApi.SendAsync(host, "ListSessionProposals", AgentRecoveryApi.Owner(session), Session(session));

        Assert.Equal(200, status);
        var listed = Assert.Single(JsonDocument.Parse(body).RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(proposal.Id, listed.GetProperty("id").GetString());
        Assert.Equal("awaitingApproval", listed.GetProperty("status").GetString());
        var prepared = listed.GetProperty("preparedInvocation");
        Assert.Equal(proposal.PreparedInvocation!.ExpiresAt, prepared.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Object, prepared.GetProperty("preview").ValueKind);
        Assert.False(listed.TryGetProperty("ownerResolution", out _));
    }

    // ---- POST /sessions/{id}/messages/{messageId}/withdraw ----

    [Fact]
    public async Task Withdrawing_a_prompt_that_never_started_cancels_it_once_and_releases_its_claim_across_a_restart()
    {
        using var harness = Harness();
        var session = await harness.Host.CreateAsync();
        var prompt = await harness.Host.PostAsync(session, "hello");
        var host = harness.Restart();
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await host.ProbeAsync(session)).Outcome);

        var (status, body) = await WithdrawAsync(host, session, prompt.Id);

        Assert.Equal(200, status);
        var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
        Assert.Equal(("cancelled", "withdrawn"), (data.GetProperty("status").GetString(), data.GetProperty("statusReason").GetString()));
        var stored = (await harness.Store.LoadAsync(session.Id))!.Messages.Single().Message;
        Assert.Equal((AgentMessageStatus.Cancelled, AgentMessageStatusReasons.Withdrawn), (stored.Status, stored.StatusReason));
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);
        // The session details carry the reason, so a returning view can show it.
        var (_, details) = await AgentRecoveryApi.SendAsync(host, "GetSession", AgentRecoveryApi.Owner(session), Session(session));
        Assert.Equal("withdrawn", JsonDocument.Parse(details).RootElement.GetProperty("data").GetProperty("messages")[0].GetProperty("statusReason").GetString());

        var (again, againBody) = await WithdrawAsync(host, session, prompt.Id);
        AgentRecoveryApi.AssertFailure(again, againBody, 409, "agent.message.not_pending");
        var (missing, missingBody) = await WithdrawAsync(host, session, "missing");
        AgentRecoveryApi.AssertFailure(missing, missingBody, 404, "agent.message.not_found");
        Assert.Equal("agent.message.no_pending", Assert.Single(await host.StreamAsync(session), x => x.Kind == AgentStreamEventKind.Error).Error?.Code);
        Assert.Equal(0, harness.Provider.Turns);
    }

    [Fact]
    public async Task A_prompt_is_not_withdrawn_while_a_turn_is_registered_for_its_session()
    {
        using var harness = Harness();
        var session = await harness.Host.CreateAsync();
        var prompt = await harness.Host.PostAsync(session, "hello");
        var registry = harness.Host.Services.GetRequiredService<IAgentTurnRegistry>();
        registry.Register(session.Id, "turn-1");

        var (status, body) = await WithdrawAsync(harness.Host, session, prompt.Id);

        AgentRecoveryApi.AssertFailure(status, body, 409, "agent.message.not_pending");
        Assert.Equal(AgentMessageStatus.Pending, (await harness.Host.Sessions.FindMessageAsync(session.Id, prompt.Id))!.Status);
        registry.Unregister(session.Id, "turn-1");
    }

    [Fact]
    public async Task A_withdraw_that_wins_the_race_with_a_start_leaves_the_provider_uncalled()
    {
        using var harness = new AgentRestartHarness(clock => new HoldingRecordStore(new InMemoryAgentSessionRecordStore(clock)),
            services => services.AddRouting().AddElsaEndpoints());
        var store = (HoldingRecordStore)harness.Store;
        var host = harness.Host;
        var session = await host.CreateAsync();
        var prompt = await host.PostAsync(session, "hello");
        // The withdraw holds the session write gate across its awaited write; the start arrives meanwhile.
        var held = store.HoldNextWrite(write => write.Messages.Any(x => x.Message.Status == AgentMessageStatus.Cancelled));
        var withdraw = Task.Run(() => WithdrawAsync(host, session, prompt.Id));
        await held.Entered.Task.WaitAsync(HandoffTimeout);
        var start = Task.Run(() => host.StreamAsync(session));
        held.Release.SetResult();

        Assert.Equal(200, (await withdraw.WaitAsync(HandoffTimeout)).Status);
        var events = await start.WaitAsync(HandoffTimeout);
        Assert.Contains(events, x => x.Error?.Code is "agent.message.no_pending" or "agent.message.not_pending");
        Assert.DoesNotContain(events, x => x.Kind == AgentStreamEventKind.Started);
        Assert.Equal(0, harness.Provider.Turns);
        Assert.Equal(AgentMessageStatusReasons.Withdrawn, (await host.Sessions.FindMessageAsync(session.Id, prompt.Id))!.StatusReason);
    }

    [Fact]
    public async Task A_start_that_wins_the_race_with_a_withdraw_runs_once_and_the_withdraw_is_refused()
    {
        using var harness = new AgentRestartHarness(clock => new HoldingRecordStore(new InMemoryAgentSessionRecordStore(clock)),
            services => services.AddRouting().AddElsaEndpoints());
        var store = (HoldingRecordStore)harness.Store;
        var host = harness.Host;
        var session = await host.CreateAsync();
        var prompt = await host.PostAsync(session, "hello");
        // The start holds the session write gate across its Pending-to-Streaming write; the withdraw arrives meanwhile.
        var held = store.HoldNextWrite(write => write.Messages.Any(x => x.Message.Status == AgentMessageStatus.Streaming));
        var start = Task.Run(() => host.StreamAsync(session));
        await held.Entered.Task.WaitAsync(HandoffTimeout);
        var withdraw = Task.Run(() => WithdrawAsync(host, session, prompt.Id));
        held.Release.SetResult();

        var (status, body) = await withdraw.WaitAsync(HandoffTimeout);
        AgentRecoveryApi.AssertFailure(status, body, 409, "agent.message.not_pending");
        Assert.Equal(AgentStreamEventKind.Completed, (await start.WaitAsync(HandoffTimeout))[^1].Kind);
        Assert.Equal(1, harness.Provider.Turns);
        Assert.Equal(AgentMessageStatus.Completed, (await host.Sessions.FindMessageAsync(session.Id, prompt.Id))!.Status);
    }

    // ---- POST /sessions/{id}/client-invocations/{invocationId}/resolution ----

    [Fact]
    public async Task The_owner_resolves_an_uncertain_client_operation_once_and_a_later_receipt_supersedes_it()
    {
        var client = new ProposalClientTool();
        var authority = new ProposalTestAuthorizer();
        using var harness = Harness(services =>
        {
            services.AddSingleton<IAgentTool>(client);
            services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer>(_ => authority));
        });
        var (session, proposal, release) = await UncertainClientOperationAsync(harness, client, authority);
        var host = harness.Host;
        var invocationId = proposal.PreparedInvocation!.InvocationId;
        // A paused turn that awaits the operation keeps its own claim; the resolution releases only the operation's.
        var pause = (await host.Owner.EnsurePauseAsync(session.Id))!;
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await host.ProbeAsync(session)).Outcome);

        var (invalid, invalidBody) = await ResolveAsync(host, session, invocationId, "maybe");
        AgentRecoveryApi.AssertFailure(invalid, invalidBody, 400, "agent.invocation.resolution_invalid");
        var (resolved, resolvedBody) = await ResolveAsync(host, session, invocationId, "notApplied");

        Assert.Equal(200, resolved);
        var data = JsonDocument.Parse(resolvedBody).RootElement.GetProperty("data");
        Assert.Equal("uncertain", data.GetProperty("status").GetString());
        Assert.Equal(("notApplied", session.ActorId), (data.GetProperty("ownerResolution").GetProperty("claim").GetString(),
            data.GetProperty("ownerResolution").GetProperty("actorId").GetString()));
        Assert.Equal(AgentSessionPolicyOutcome.Busy, (await host.ProbeAsync(session)).Outcome);
        host.Owner.Release(pause);
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);

        // A retry is idempotent and writes nothing; a different statement is refused.
        var version = (await harness.Store.LoadAsync(session.Id))!.Version;
        Assert.Equal(200, (await ResolveAsync(host, session, invocationId, "notApplied")).Status);
        Assert.Equal(version, (await harness.Store.LoadAsync(session.Id))!.Version);
        var (conflict, conflictBody) = await ResolveAsync(host, session, invocationId, "applied");
        AgentRecoveryApi.AssertFailure(conflict, conflictBody, 409, "agent.invocation.resolution_conflict");

        // After another restart the resolved operation no longer keeps the session busy.
        host = harness.Restart();
        Assert.Equal(AgentSessionPolicyOutcome.Succeeded, (await host.ProbeAsync(session)).Outcome);

        // A later truthful receipt still reconciles the operation; the owner's statement stays on the record.
        var reconciled = await host.Proposals.AcknowledgeAsync(session.Id, invocationId, session.ActorId, AgentRecoveryRestartTests.Acknowledgement(proposal));
        Assert.True(reconciled.Value!.Executed);
        var recorded = (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal;
        Assert.Equal((AgentActionProposalStatus.Executed, AgentOwnerResolutionClaim.NotApplied), (recorded.Status, recorded.OwnerResolution?.Claim));
        Assert.Equal(200, (await ResolveAsync(host, session, invocationId, "notApplied")).Status);
        Assert.Equal((1, 0), (client.CompleteCalls, client.ExecuteCalls));
        await release();
    }

    [Fact]
    public async Task Only_an_uncertain_client_operation_can_be_resolved()
    {
        var client = new ProposalClientTool();
        using var harness = Harness(services => services.AddSingleton<IAgentTool>(client));
        var host = harness.Host;
        var session = await host.CreateAsync();
        var proposal = (await host.Invoker.DispatchAsync(AgentRecoveryRestartTests.ClientCall(session, client), session.Policy)).Proposal!;
        Assert.True((await host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        Assert.Equal("awaiting-client", (await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Value?.Status);

        var (pending, pendingBody) = await ResolveAsync(host, session, proposal.PreparedInvocation!.InvocationId, "applied");
        AgentRecoveryApi.AssertFailure(pending, pendingBody, 409, "agent.invocation.not_uncertain");
        var (missing, missingBody) = await ResolveAsync(host, session, "missing-invocation", "applied");
        AgentRecoveryApi.AssertFailure(missing, missingBody, 404, "agent.invocation.not_found");
        Assert.Null((await host.Proposals.FindAsync(proposal.Id))!.OwnerResolution);
    }

    // ---- POST /sessions/{id}/turns/{turnId}/cancel (existing) ----

    [Fact]
    public async Task A_users_stop_records_user_stop_and_a_dropped_stream_does_not()
    {
        var turns = new RecordingTurnRegistry();
        using var harness = Harness(services => services.Replace(ServiceDescriptor.Singleton<IAgentTurnRegistry>(turns)));
        var host = harness.Host;
        var session = await host.CreateAsync();
        var stopped = await host.PostAsync(session, "stop me");
        var held = harness.Provider.HoldNext();
        var running = Task.Run(() => host.StreamAsync(session));
        await held.Entered.Task.WaitAsync(HandoffTimeout);

        var (status, _) = await AgentRecoveryApi.SendAsync(host, "CancelTurn", AgentRecoveryApi.Owner(session),
            new Dictionary<string, string> { ["sessionId"] = session.Id, ["turnId"] = turns.Last! }, new { turnId = turns.Last });

        Assert.Equal(200, status);
        Assert.Equal(AgentStreamEventKind.TurnCancelled, (await running.WaitAsync(HandoffTimeout))[^1].Kind);
        var stop = (await host.Sessions.FindMessageAsync(session.Id, stopped.Id))!;
        Assert.Equal((AgentMessageStatus.Cancelled, AgentMessageStatusReasons.UserStop), (stop.Status, stop.StatusReason));

        var dropped = await host.PostAsync(session, "drop me");
        var heldAgain = harness.Provider.HoldNext();
        using var connection = new CancellationTokenSource();
        var droppedRun = Task.Run(() => host.StreamAsync(session, connection.Token));
        await heldAgain.Entered.Task.WaitAsync(HandoffTimeout);
        connection.Cancel();
        await droppedRun.WaitAsync(HandoffTimeout);
        var drop = (await host.Sessions.FindMessageAsync(session.Id, dropped.Id))!;
        Assert.Equal((AgentMessageStatus.Cancelled, (string?)null), (drop.Status, drop.StatusReason));
    }

    // ---- Helpers ----

    private static AgentRestartHarness Harness(Action<IServiceCollection>? configure = null) => new(configure: services =>
    {
        services.AddRouting().AddElsaEndpoints();
        configure?.Invoke(services);
    });

    private static Dictionary<string, string> Session(AgentSession session) => new() { ["sessionId"] = session.Id };

    private static async Task<string> CreateAsync(AgentRestartHarness harness, string actor, string tenant, string workspace, string? continuedFrom = null)
    {
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        var (status, body) = await AgentRecoveryApi.SendAsync(harness.Host, "CreateSession", AgentRecoveryApi.Principal(actor, tenant),
            body: new { conversationId = "conversation", workspace, continuedFrom });
        Assert.Equal(200, status);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("sessionId").GetString()!;
    }

    private static async Task<JsonElement> ListAsync(RecoveryHost host, System.Security.Claims.ClaimsPrincipal user, string query)
    {
        var (status, body) = await AgentRecoveryApi.SendAsync(host, "ListSessions", user, query: query);
        Assert.Equal(200, status);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }

    private static string[] Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToArray();

    private static Task<(int Status, string Body)> WithdrawAsync(RecoveryHost host, AgentSession session, string messageId)
        => AgentRecoveryApi.SendAsync(host, "WithdrawMessage", AgentRecoveryApi.Owner(session),
            new Dictionary<string, string> { ["sessionId"] = session.Id, ["messageId"] = messageId });

    private static Task<(int Status, string Body)> ResolveAsync(RecoveryHost host, AgentSession session, string invocationId, string claim)
        => AgentRecoveryApi.SendAsync(host, "ResolveClientInvocation", AgentRecoveryApi.Owner(session),
            new Dictionary<string, string> { ["sessionId"] = session.Id, ["invocationId"] = invocationId }, new { claim });

    /// <summary>
    /// A client operation that was dispatched when the process stopped: Executing is recorded, then the host restarts and
    /// recovery records it Uncertain. Returns a release for the crashed host's held authority check.
    /// </summary>
    internal static async Task<(AgentSession Session, AgentActionProposal Proposal, Func<Task> Release)> UncertainClientOperationAsync(
        AgentRestartHarness harness, ProposalClientTool client, ProposalTestAuthorizer authority)
    {
        var old = harness.Host;
        var session = await old.CreateAsync();
        var proposal = (await old.Invoker.DispatchAsync(AgentRecoveryRestartTests.ClientCall(session, client), session.Policy)).Proposal!;
        Assert.True((await old.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        authority.HoldNextDecision(async _ => { entered.TrySetResult(); await release.Task.WaitAsync(HandoffTimeout); });
        var interrupted = Task.Run(() => old.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision));
        await entered.Task.WaitAsync(HandoffTimeout);
        var host = harness.Restart();
        Assert.Equal(AgentActionProposalStatus.Uncertain, (await host.Proposals.FindAsync(proposal.Id))!.Status);
        return (session, proposal, async () =>
        {
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<Exception>(() => interrupted.WaitAsync(HandoffTimeout));
        });
    }

    /// <summary>The default registry, remembering the last registered turn identity so a test can stop it by its id.</summary>
    private sealed class RecordingTurnRegistry : IAgentTurnRegistry
    {
        private readonly AgentTurnRegistry _inner = new();
        public string? Last { get; private set; }

        public CancellationToken Register(string sessionId, string turnId)
        {
            var token = _inner.Register(sessionId, turnId);
            Last = turnId;
            return token;
        }

        public bool Cancel(string sessionId, string turnId) => _inner.Cancel(sessionId, turnId);
        public void Unregister(string sessionId, string turnId) => _inner.Unregister(sessionId, turnId);
        public bool IsActive(string sessionId) => _inner.IsActive(sessionId);
    }

    /// <summary>A forwarding record store whose next write matching a condition is held at a barrier.</summary>
    private sealed class HoldingRecordStore(IAgentSessionRecordStore inner) : IAgentSessionRecordStore
    {
        private Hold? _hold;

        public bool SupportsFencing => inner.SupportsFencing;

        public Hold HoldNextWrite(Func<AgentSessionRecordWrite, bool> condition)
        {
            var hold = new Hold(condition);
            Volatile.Write(ref _hold, hold);
            return hold;
        }

        public async Task<AgentSessionRecordResult> WriteAsync(AgentSessionRecordWrite write, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _hold) is { } hold && hold.Condition(write) && Interlocked.CompareExchange(ref _hold, null, hold) == hold)
            {
                hold.Entered.TrySetResult();
                await hold.Release.Task.WaitAsync(HandoffTimeout, cancellationToken);
            }
            return await inner.WriteAsync(write, cancellationToken);
        }

        public Task<AgentSessionRecordResult> CreateAsync(AgentSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => inner.CreateAsync(record, ownerToken, cancellationToken);
        public Task<AgentSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default) => inner.LoadAsync(sessionId, cancellationToken);
        public Task<AgentSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
            => inner.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken);
        public Task ReleaseOwnershipAsync(string sessionId, AgentSessionFence fence, CancellationToken cancellationToken = default)
            => inner.ReleaseOwnershipAsync(sessionId, fence, cancellationToken);
        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => inner.FindProposalSessionAsync(proposalId, cancellationToken);
        public Task<AgentSessionSummaryPage> ListOwnedAsync(AgentSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => inner.ListOwnedAsync(query, cancellationToken);
        public Task<AgentSessionOwner?> FindOwnerAsync(string sessionId, CancellationToken cancellationToken = default) => inner.FindOwnerAsync(sessionId, cancellationToken);
    }

    private sealed class Hold(Func<AgentSessionRecordWrite, bool> condition)
    {
        public Func<AgentSessionRecordWrite, bool> Condition { get; } = condition;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
