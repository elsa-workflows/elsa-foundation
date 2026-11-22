using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Elsa.Weaver.Api;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Elsa.Api.AspNetCore;
using Elsa.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Tests;

/// <summary>
/// #3441 T016 (story 4.3, FR-013): every route authorizes on the session's plain owner columns before anything decodes a
/// payload, hydrates the session or takes its ownership. A foreign or missing caller receives only the shared 404 or 403
/// and causes no record write, takeover, settlement or audit record; ownership outcomes are reported only to the owner.
/// </summary>
public sealed class WeaverRecoveryAuthorizationTests
{
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PastPreparedDeadline = TimeSpan.FromMinutes(6);

    /// <summary>Plain owner reads that authorization may perform; anything else would hydrate, take over or write.</summary>
    private static readonly string[] AuthorizationReads =
        [nameof(IWeaverSessionRecordStore.FindProposalSessionAsync), RecordingRecordStore.FindOwner, nameof(IWeaverSessionRecordStore.ListOwnedAsync)];

    public static TheoryData<string, string?, string?> ForeignWork()
    {
        var data = new TheoryData<string, string?, string?>();
        foreach (var work in new[] { "pending", "approved", "dispatched", "running" })
        {
            data.Add(work, "actor-2", "tenant-1");
            data.Add(work, "actor-1", "tenant-2");
            data.Add(work, null, null);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ForeignWork))]
    public async Task A_foreign_or_missing_caller_gets_the_shared_refusal_and_causes_no_hydration_write_or_settlement(string work, string? actor, string? tenant)
    {
        using var harness = new WeaverRestartHarness(clock => new RecordingRecordStore(new InMemoryWeaverSessionRecordStore(clock)),
            services => services.AddRouting().AddElsaEndpoints());
        var store = (RecordingRecordStore)harness.Store;
        var (session, proposal, release) = await RestartWithAsync(harness, work);
        var host = harness.Host;
        var before = (await store.Inner.LoadAsync(session.Id))!;
        var dispatches = harness.Module.Dispatches;
        store.Clear();
        var foreign = WeaverRecoveryApi.Principal(actor, tenant);

        foreach (var (operation, route, body, query) in SessionRoutes(session, proposal))
        {
            var (status, text) = await WeaverRecoveryApi.SendAsync(host, operation, foreign, route, body, query);
            if (operation == "StreamSession")
                Assert.Contains("weaver.session.forbidden", text, StringComparison.Ordinal);
            else
                WeaverRecoveryApi.AssertFailure(status, text, 403, "weaver.session.forbidden");
        }
        foreach (var operation in new[] { "ApproveProposal", "DenyProposal", "ExecuteProposal" })
        {
            var (status, text) = await WeaverRecoveryApi.SendAsync(host, operation, foreign,
                new Dictionary<string, string> { ["proposalId"] = proposal.Id }, new { revision = proposal.BaseRevision });
            WeaverRecoveryApi.AssertFailure(status, text, 403, "weaver.proposal.forbidden");
        }

        // The caller's own session list never shows the session.
        var (listed, listedBody) = await WeaverRecoveryApi.SendAsync(host, "ListSessions", foreign);
        if (actor is null)
            WeaverRecoveryApi.AssertFailure(listed, listedBody, 403, "weaver.actor.unresolved");
        else
            Assert.Empty(System.Text.Json.JsonDocument.Parse(listedBody).RootElement.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.All(store.Calls, call => Assert.Contains(call, AuthorizationReads));
        var after = (await store.Inner.LoadAsync(session.Id))!;
        Assert.Equal((before.Owner, before.Version, before.LeaseExpiresAt), (after.Owner, after.Version, after.LeaseExpiresAt));
        Assert.Empty(await host.Audit.ListAsync(session.Id));
        Assert.Equal(dispatches, harness.Module.Dispatches);

        // Positive control: the owner's own read is what hydrates, takes ownership and settles the recorded work.
        var (owned, _) = await WeaverRecoveryApi.SendAsync(host, "GetSession", WeaverRecoveryApi.Principal(session.ActorId, session.TenantId),
            new Dictionary<string, string> { ["sessionId"] = session.Id });
        Assert.Equal(200, owned);
        Assert.Contains(nameof(IWeaverSessionRecordStore.AcquireOwnershipAsync), store.Calls);
        Assert.Contains(nameof(IWeaverSessionRecordStore.WriteAsync), store.Calls);
        await release();
    }

    [Fact]
    public async Task A_missing_session_is_the_shared_404_without_any_hydration()
    {
        using var harness = new WeaverRestartHarness(clock => new RecordingRecordStore(new InMemoryWeaverSessionRecordStore(clock)),
            services => services.AddRouting().AddElsaEndpoints());
        var store = (RecordingRecordStore)harness.Store;

        var (status, text) = await WeaverRecoveryApi.SendAsync(harness.Host, "GetSession", WeaverRecoveryApi.Principal("actor-1", "tenant-1"),
            new Dictionary<string, string> { ["sessionId"] = "missing" });

        WeaverRecoveryApi.AssertFailure(status, text, 404, "weaver.session.not_found");
        Assert.All(store.Calls, call => Assert.Contains(call, AuthorizationReads));
    }

    [Fact]
    public async Task Ownership_held_elsewhere_is_reported_only_to_the_owner()
    {
        using var harness = new WeaverRestartHarness(clock => new RecordingRecordStore(new InMemoryWeaverSessionRecordStore(clock)),
            services => services.AddRouting().AddElsaEndpoints());
        var store = (RecordingRecordStore)harness.Store;
        var session = await harness.Host.CreateAsync();
        using var other = harness.StartAnotherHost();
        store.Clear();
        var route = new Dictionary<string, string> { ["sessionId"] = session.Id };

        var (foreign, foreignBody) = await WeaverRecoveryApi.SendAsync(other, "GetSession", WeaverRecoveryApi.Principal("actor-2", session.TenantId), route);
        WeaverRecoveryApi.AssertFailure(foreign, foreignBody, 403, "weaver.session.forbidden");
        Assert.All(store.Calls, call => Assert.Contains(call, AuthorizationReads));

        var (owner, ownerBody) = await WeaverRecoveryApi.SendAsync(other, "GetSession", WeaverRecoveryApi.Principal(session.ActorId, session.TenantId), route);
        WeaverRecoveryApi.AssertFailure(owner, ownerBody, 409, WeaverSessionOwnershipException.OwnedElsewhere);
    }

    /// <summary>Every session-scoped route a returning caller can reach, with a body that passes its own request validation.</summary>
    internal static IEnumerable<(string Operation, IReadOnlyDictionary<string, string> Route, object? Body, string? Query)> SessionRoutes(
        WeaverSession session, WeaverActionProposal proposal)
    {
        var route = new Dictionary<string, string> { ["sessionId"] = session.Id };
        yield return ("GetSession", route, null, null);
        yield return ("ChangeAutonomy", route, new { autonomyMode = "manual", expectedRevision = 1 }, null);
        yield return ("PostMessage", route, new { content = "foreign prompt" }, null);
        yield return ("CancelTurn", new Dictionary<string, string>(route) { ["turnId"] = "turn-1" }, new { turnId = "turn-1" }, null);
        yield return ("StreamSession", route, null, null);
        yield return ("Feedback", route, new { sessionId = session.Id, rating = 1 }, null);
        yield return ("AcknowledgeClientInvocation", new Dictionary<string, string>(route) { ["invocationId"] = proposal.PreparedInvocation!.InvocationId },
            new WeaverClientAcknowledgement(new("client-1", "generation-1"), proposal.PreparedInvocation.ApprovalDigest, WeaverClientOutcome.Applied,
                "Foreign report.", null, null), null);
        yield return ("Audit", new Dictionary<string, string>(), null, "?sessionId=" + session.Id);
        yield return ("ListSessionProposals", route, null, null);
        yield return ("WithdrawMessage", new Dictionary<string, string>(route) { ["messageId"] = "message-1" }, null, null);
        yield return ("ResolveClientInvocation", new Dictionary<string, string>(route) { ["invocationId"] = proposal.PreparedInvocation.InvocationId },
            new { claim = "applied" }, null);
    }

    /// <summary>
    /// Leaves recorded work of one kind behind, restarts past its deadline, and returns a release for anything the crashed
    /// host still holds. Each kind would be settled by the first hydration.
    /// </summary>
    internal static async Task<(WeaverSession Session, WeaverActionProposal Proposal, Func<Task> Release)> RestartWithAsync(WeaverRestartHarness harness, string work)
    {
        var old = harness.Host;
        var session = await old.CreateAsync();
        Func<Task> release = () => Task.CompletedTask;
        WeaverActionProposal proposal;
        switch (work)
        {
            case "pending":
                proposal = await old.PauseOnReviewAsync(session);
                break;
            case "approved":
                proposal = (await old.Invoker.DispatchAsync(WeaverRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
                Assert.True((await old.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
                break;
            case "dispatched":
            {
                proposal = (await old.Invoker.DispatchAsync(WeaverRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
                Assert.True((await old.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
                var held = harness.Module.HoldNextDispatch();
                var interrupted = Task.Run(() => old.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision));
                await held.Started.Task.WaitAsync(HandoffTimeout);
                release = async () =>
                {
                    held.Release.TrySetResult();
                    await Assert.ThrowsAnyAsync<Exception>(() => interrupted.WaitAsync(HandoffTimeout));
                };
                break;
            }
            case "running":
            {
                proposal = (await old.Invoker.DispatchAsync(WeaverRecoveryRestartTests.Call(session, "call-0", "turn-0"), session.Policy)).Proposal!;
                Assert.True((await old.Proposals.DenyAsync(proposal.Id, session.ActorId, "not this one")).Succeeded);
                await old.PostAsync(session, "hello");
                var held = harness.Provider.HoldNext();
                var running = Task.Run(() => old.StreamAsync(session));
                await held.Entered.Task.WaitAsync(HandoffTimeout);
                release = async () =>
                {
                    held.Release.TrySetResult();
                    await Assert.ThrowsAnyAsync<Exception>(() => running.WaitAsync(HandoffTimeout));
                };
                break;
            }
            default: throw new ArgumentOutOfRangeException(nameof(work), work, null);
        }
        harness.Clock.Advance(PastPreparedDeadline);
        harness.Restart();
        return (session, proposal, release);
    }

    /// <summary>A forwarding record store that records the name of every member called on it.</summary>
    internal sealed class RecordingRecordStore(IWeaverSessionRecordStore inner) : IWeaverSessionRecordStore
    {
        public const string FindOwner = "FindOwnerAsync";
        private readonly ConcurrentQueue<string> _calls = new();

        public IWeaverSessionRecordStore Inner => inner;
        public IReadOnlyCollection<string> Calls => _calls.ToArray();
        public void Clear() => _calls.Clear();
        public bool SupportsFencing => inner.SupportsFencing;

        public Task<WeaverSessionRecordResult> CreateAsync(WeaverSessionRecord record, string ownerToken, CancellationToken cancellationToken = default)
            => Record(nameof(CreateAsync), () => inner.CreateAsync(record, ownerToken, cancellationToken));
        public Task<WeaverSessionRecord?> LoadAsync(string sessionId, CancellationToken cancellationToken = default)
            => Record(nameof(LoadAsync), () => inner.LoadAsync(sessionId, cancellationToken));
        public Task<WeaverSessionRecordResult> AcquireOwnershipAsync(string sessionId, string ownerToken, CancellationToken cancellationToken = default)
            => Record(nameof(AcquireOwnershipAsync), () => inner.AcquireOwnershipAsync(sessionId, ownerToken, cancellationToken));
        public Task ReleaseOwnershipAsync(string sessionId, WeaverSessionFence fence, CancellationToken cancellationToken = default)
            => Record(nameof(ReleaseOwnershipAsync), () => inner.ReleaseOwnershipAsync(sessionId, fence, cancellationToken));
        public Task<WeaverSessionRecordResult> WriteAsync(WeaverSessionRecordWrite write, CancellationToken cancellationToken = default)
            => Record(nameof(WriteAsync), () => inner.WriteAsync(write, cancellationToken));
        public Task<string?> FindProposalSessionAsync(string proposalId, CancellationToken cancellationToken = default)
            => Record(nameof(FindProposalSessionAsync), () => inner.FindProposalSessionAsync(proposalId, cancellationToken));
        public Task<WeaverSessionSummaryPage> ListOwnedAsync(WeaverSessionOwnerQuery query, CancellationToken cancellationToken = default)
            => Record(nameof(ListOwnedAsync), () => inner.ListOwnedAsync(query, cancellationToken));
        public Task<WeaverSessionOwner?> FindOwnerAsync(string sessionId, CancellationToken cancellationToken = default)
            => Record(FindOwner, () => inner.FindOwnerAsync(sessionId, cancellationToken));

        private T Record<T>(string member, Func<T> call)
        {
            _calls.Enqueue(member);
            return call();
        }
    }
}

/// <summary>Calls one mapped Weaver route in-process on a recovery host, as the given principal.</summary>
internal static class WeaverRecoveryApi
{
    public static ClaimsPrincipal Principal(string? actor, string? tenant)
    {
        var claims = new List<Claim>();
        if (actor is not null) claims.Add(new(ClaimTypes.NameIdentifier, actor));
        if (tenant is not null) claims.Add(new("elsa.identity.tenant_id", tenant));
        return new(new ClaimsIdentity(claims, "test"));
    }

    public static ClaimsPrincipal Owner(WeaverSession session) => Principal(session.ActorId, session.TenantId);

    public static async Task<(int Status, string Body)> SendAsync(RecoveryHost host, string operation, ClaimsPrincipal user,
        IReadOnlyDictionary<string, string>? route = null, object? body = null, string? query = null, CancellationToken aborted = default)
    {
        var app = new TestEndpointRouteBuilder(host.Services);
        WeaverApi.MapWeaverApi(app);
        var endpoint = Assert.Single(app.DataSources.SelectMany(source => source.Endpoints), item =>
            item.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaWeaverApiEndpoints" + operation);
        var context = new DefaultHttpContext { RequestServices = host.Services, User = user, RequestAborted = aborted };
        // A composition with the HTTP tool authorizer reads the current request through the accessor, as in a host.
        if (host.Services.GetService<IHttpContextAccessor>() is { } accessor)
            accessor.HttpContext = context;
        foreach (var (name, value) in route ?? new Dictionary<string, string>())
            context.Request.RouteValues[name] = value;
        if (query is not null)
            context.Request.QueryString = new QueryString(query);
        if (body is not null)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
        }
        var response = new MemoryStream();
        context.Response.Body = response;
        await endpoint.RequestDelegate!(context);
        return (context.Response.StatusCode, Encoding.UTF8.GetString(response.ToArray()));
    }

    public static void AssertFailure(int status, string body, int expectedStatus, string code)
    {
        Assert.Equal(expectedStatus, status);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("data").ValueKind);
        Assert.Equal(code, document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
