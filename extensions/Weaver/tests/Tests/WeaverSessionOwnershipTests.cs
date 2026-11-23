using System.Security.Claims;
using System.Text.Json;
using Elsa.Weaver.Api;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Elsa.Api.AspNetCore;
using Elsa.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Tests;

/// <summary>
/// #3441 T014 (FR-017, research R5): two host processes over one record store. At most one owns a session's work; a
/// live lease elsewhere is a retryable refusal, a superseded owner's writes are refused, and its row and turn end.
/// </summary>
public sealed class WeaverSessionOwnershipTests
{
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PastLease = InMemoryWeaverSessionRecordStore.LeaseDuration + TimeSpan.FromSeconds(1);

    [Fact]
    public async Task A_live_lease_elsewhere_is_refused_with_a_retry_hint_and_every_guarded_write_extends_it()
    {
        using var harness = new WeaverRestartHarness();
        var a = harness.Host;
        var session = await a.CreateAsync();
        var created = (await harness.Store.LoadAsync(session.Id))!;
        using var b = harness.StartAnotherHost();

        var refused = await Assert.ThrowsAsync<WeaverSessionOwnershipException>(() => b.Sessions.FindAsync(session.Id));
        Assert.Equal(WeaverSessionOwnershipException.OwnedElsewhere, refused.Code);
        Assert.Equal(created.LeaseExpiresAt, refused.RetryAt);
        Assert.Equal((created.Owner, created.Version), ((await harness.Store.LoadAsync(session.Id))!.Owner, (await harness.Store.LoadAsync(session.Id))!.Version));

        // Each write by the owner extends its lease, so another process stays refused past the first lease.
        harness.Clock.Advance(TimeSpan.FromSeconds(20));
        await a.PostAsync(session, "hello");
        harness.Clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(WeaverSessionOwnershipException.OwnedElsewhere,
            (await Assert.ThrowsAsync<WeaverSessionOwnershipException>(() => b.Sessions.FindAsync(session.Id))).Code);

        // A lapsed lease is taken over with a new fence, and the earlier owner's next write is refused.
        harness.Clock.Advance(PastLease);
        Assert.Equal(session.Id, (await b.Sessions.FindAsync(session.Id))!.Id);
        Assert.Equal(created.Owner!.Fence + 1, (await harness.Store.LoadAsync(session.Id))!.Owner!.Fence);
        Assert.Equal(WeaverSessionOwnershipException.OwnershipLost,
            (await Assert.ThrowsAsync<WeaverSessionOwnershipException>(() => a.Sessions.AddContextAsync(session.Id,
                [new("attachment-1", "test", null, "Label", "test.kind", WeaverContextSensitivity.Internal, "scope", "Summary", null, new Dictionary<string, string>())]))).Code);
        Assert.Empty(await b.Sessions.ListContextAsync(session.Id));
        Assert.Single(await b.Sessions.ListMessagesAsync(session.Id));
    }

    [Fact]
    public async Task Losing_ownership_mid_call_evicts_the_row_and_cancels_the_running_turn()
    {
        using var harness = new WeaverRestartHarness();
        var a = harness.Host;
        var session = await a.CreateAsync();
        await a.PostAsync(session, "hello");
        var held = harness.Provider.HoldNext();
        var running = Task.Run(() => a.StreamAsync(session));
        await held.Entered.Task.WaitAsync(HandoffTimeout);
        harness.Clock.Advance(PastLease);
        using var b = harness.StartAnotherHost();
        Assert.NotNull(await b.Sessions.FindAsync(session.Id));

        var lost = await Assert.ThrowsAsync<WeaverSessionOwnershipException>(() => a.Sessions.AddContextAsync(session.Id,
            [new("attachment-1", "test", null, "Label", "test.kind", WeaverContextSensitivity.Internal, "scope", "Summary", null, new Dictionary<string, string>())]));

        Assert.Equal(WeaverSessionOwnershipException.OwnershipLost, lost.Code);
        // The running turn was cancelled through the registry; it ends without the provider being released.
        var ended = await Assert.ThrowsAnyAsync<Exception>(() => running.WaitAsync(HandoffTimeout));
        Assert.NotNull(WeaverSessionOwnershipException.Find(ended));
        Assert.False(a.Services.GetRequiredService<Core.Contracts.IWeaverTurnRegistry>().IsActive(session.Id));
        // The row was evicted: the earlier owner now sees the live lease elsewhere instead of its stale copy.
        Assert.Equal(WeaverSessionOwnershipException.OwnedElsewhere,
            (await Assert.ThrowsAsync<WeaverSessionOwnershipException>(() => a.Sessions.FindAsync(session.Id))).Code);
        var interrupted = Assert.Single(await b.Sessions.ListMessagesAsync(session.Id));
        Assert.Equal(WeaverMessageStatusReasons.Interrupted, interrupted.StatusReason);
        held.Release.TrySetResult();
    }

    [Fact]
    public async Task The_lease_is_renewed_in_the_background_only_while_a_turn_runs_or_an_execution_is_dispatched()
    {
        using var harness = new WeaverRestartHarness();
        var a = harness.Host;
        var session = await a.CreateAsync();
        using var b = harness.StartAnotherHost();
        await a.PostAsync(session, "hello");
        Assert.False(a.Owner.RenewalActive);
        var held = harness.Provider.HoldNext();
        var running = Task.Run(() => a.StreamAsync(session));
        await held.Entered.Task.WaitAsync(HandoffTimeout);
        Assert.True(a.Owner.RenewalActive);

        harness.Clock.Advance(TimeSpan.FromSeconds(20));
        await a.Owner.RenewLeasesAsync();
        harness.Clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(WeaverSessionOwnershipException.OwnedElsewhere,
            (await Assert.ThrowsAsync<WeaverSessionOwnershipException>(() => b.Sessions.FindAsync(session.Id))).Code);
        held.Release.SetResult();
        Assert.Equal(WeaverStreamEventKind.Completed, (await running.WaitAsync(HandoffTimeout))[^1].Kind);

        // An idle session is not renewed: once its last write's lease lapses, another process may take it over.
        await a.Owner.RenewLeasesAsync();
        harness.Clock.Advance(PastLease);
        await a.Owner.RenewLeasesAsync();
        Assert.NotNull(await b.Sessions.FindAsync(session.Id));
    }

    [Fact]
    public async Task A_dispatched_execution_keeps_its_lease_alive()
    {
        using var harness = new WeaverRestartHarness();
        var a = harness.Host;
        var session = await a.CreateAsync();
        var proposal = (await a.Invoker.DispatchAsync(WeaverRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await a.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        var held = harness.Module.HoldNextDispatch();
        var execution = Task.Run(() => a.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision));
        await held.Started.Task.WaitAsync(HandoffTimeout);
        using var b = harness.StartAnotherHost();

        harness.Clock.Advance(TimeSpan.FromSeconds(20));
        await a.Owner.RenewLeasesAsync();
        harness.Clock.Advance(TimeSpan.FromSeconds(20));

        Assert.Equal(WeaverSessionOwnershipException.OwnedElsewhere,
            (await Assert.ThrowsAsync<WeaverSessionOwnershipException>(() => b.Proposals.FindAsync(proposal.Id))).Code);
        held.Release.SetResult();
        Assert.True((await execution.WaitAsync(HandoffTimeout)).Value!.Executed);
        Assert.Equal(1, harness.Module.Effects);
    }

    [Fact]
    public async Task The_api_answers_a_session_owned_elsewhere_with_a_retryable_409()
    {
        using var harness = new WeaverRestartHarness(configure: services => services.AddRouting().AddElsaEndpoints());
        var session = await harness.Host.CreateAsync();
        harness.Clock.Advance(TimeSpan.FromSeconds(10));
        using var b = harness.StartAnotherHost();

        var (status, retryAfter, body) = await GetSessionAsync(b, session);

        Assert.Equal(409, status);
        Assert.Equal("20", retryAfter);
        Assert.Equal(WeaverSessionOwnershipException.OwnedElsewhere, body.GetProperty("error").GetProperty("code").GetString());
    }

    private static async Task<(int Status, string? RetryAfter, JsonElement Body)> GetSessionAsync(RecoveryHost host, WeaverSession session)
    {
        var app = new TestEndpointRouteBuilder(host.Services);
        WeaverApi.MapWeaverApi(app);
        var endpoint = Assert.Single(app.DataSources.SelectMany(source => source.Endpoints), item =>
            item.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaWeaverApiEndpointsGetSession");
        var context = new DefaultHttpContext
        {
            RequestServices = host.Services,
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, session.ActorId), new Claim("elsa.identity.tenant_id", session.TenantId)], "test"))
        };
        context.Request.RouteValues["sessionId"] = session.Id;
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, context.Response.Headers.RetryAfter.ToString(), response.RootElement.Clone());
    }
}
