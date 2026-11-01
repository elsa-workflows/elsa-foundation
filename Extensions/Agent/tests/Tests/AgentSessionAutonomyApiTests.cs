using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Elsa.Agent.Api;
using Elsa.Agent.Api.Models;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Elsa.Api.AspNetCore;
using Elsa.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

/// <summary>
/// HTTP behavior of the experimental (#3440, T009, not frozen) autonomy update and policy readback. Requests run
/// through the mapped endpoint delegates and the real default owner, each in its own request scope.
/// </summary>
public sealed class AgentSessionAutonomyApiTests
{
    private const string Actor = "actor-1";
    private const string Tenant = "tenant-1";

    [Fact]
    public async Task Create_session_returns_the_owner_policy_with_its_ceiling_allowed_modes_and_revision()
    {
        using var fixture = new Fixture();

        var (status, body) = await fixture.SendAsync("CreateSession", new { autonomyMode = "manual", conversationId = "c-1" });

        Assert.Equal(200, status);
        var data = body.RootElement.GetProperty("data");
        AssertPolicy(data.GetProperty("policy"), "manual", 1);
        var sessionId = data.GetProperty("sessionId").GetString()!;
        Assert.Equal(AgentAutonomyMode.Manual, (await fixture.FindAsync(sessionId)).Policy.AutonomyMode);
    }

    [Fact]
    public async Task An_update_changes_autonomy_in_both_directions_and_session_detail_reads_it_back()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();

        var (raised, raisedBody) = await fixture.ChangeAsync(session.Id, new { autonomyMode = "full-auto", expectedRevision = 1 });
        Assert.Equal(200, raised);
        Assert.Equal(JsonValueKind.Null, raisedBody.RootElement.GetProperty("error").ValueKind);
        AssertPolicy(raisedBody.RootElement.GetProperty("data"), "full-auto", 2);
        AssertPolicy((await fixture.DetailAsync(session.Id)).GetProperty("policy"), "full-auto", 2);

        var (lowered, loweredBody) = await fixture.ChangeAsync(session.Id, new { autonomyMode = "manual", expectedRevision = 2 });
        Assert.Equal(200, lowered);
        AssertPolicy(loweredBody.RootElement.GetProperty("data"), "manual", 3);
        AssertPolicy((await fixture.DetailAsync(session.Id)).GetProperty("policy"), "manual", 3);

        var stored = await fixture.FindAsync(session.Id);
        Assert.Equal(AgentAutonomyMode.Manual, stored.Policy.AutonomyMode);
        Assert.Equal(3, stored.PolicyRevision);
    }

    [Fact]
    public async Task A_requested_mode_is_clamped_to_the_session_ceiling()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync(AgentPolicy.Default with
        { AutonomyMode = AgentAutonomyMode.Manual, MaxAutonomyMode = AgentAutonomyMode.AutoReadOnly });

        var (status, body) = await fixture.ChangeAsync(session.Id, new { autonomyMode = "full-auto", expectedRevision = 1 });

        Assert.Equal(200, status);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("auto-read-only", data.GetProperty("autonomyMode").GetString());
        Assert.Equal("auto-read-only", data.GetProperty("maxAutonomyMode").GetString());
        Assert.Equal(["manual", "auto-read-only"], data.GetProperty("allowedAutonomyModes").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal(2, data.GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task An_unchanged_idle_request_acknowledges_the_current_revision()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();

        var (status, body) = await fixture.ChangeAsync(session.Id, new { autonomyMode = "auto-read-only", expectedRevision = 1 });

        Assert.Equal(200, status);
        AssertPolicy(body.RootElement.GetProperty("data"), "auto-read-only", 1);
        Assert.Equal(1, (await fixture.FindAsync(session.Id)).PolicyRevision);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"full-auto\"")]
    [InlineData("{}")]
    [InlineData("{\"expectedRevision\":1}")]
    [InlineData("{\"autonomyMode\":\"full-auto\"}")]
    [InlineData("{\"autonomyMode\":null,\"expectedRevision\":1}")]
    [InlineData("{\"autonomyMode\":\"turbo\",\"expectedRevision\":1}")]
    [InlineData("{\"autonomyMode\":\"\",\"expectedRevision\":1}")]
    [InlineData("{\"autonomyMode\":\"FullAuto\",\"expectedRevision\":1}")]
    [InlineData("{\"autonomyMode\":2,\"expectedRevision\":1}")]
    [InlineData("{\"autonomyMode\":\"full-auto\",\"expectedRevision\":\"one\"}")]
    [InlineData("{\"autonomyMode\":\"full-auto\",\"expectedRevision\":1.5}")]
    public async Task A_malformed_body_or_undefined_mode_is_invalid_without_state_change(string json)
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();

        var (status, body) = await fixture.SendRawAsync("ChangeAutonomy", json, session.Id);

        AssertFailure(status, body, 400, "agent.session.autonomy_invalid");
        await AssertUnchangedAsync(fixture, session.Id);
    }

    [Theory]
    [InlineData("missing", Actor, Tenant)]
    [InlineData(null, "actor-2", Tenant)]
    [InlineData(null, Actor, "tenant-2")]
    [InlineData(null, null, null)]
    public async Task A_missing_or_foreign_session_is_not_found_without_state_change(string? sessionId, string? actor, string? tenant)
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();

        var (status, body) = await fixture.SendAsync("ChangeAutonomy", new { autonomyMode = "full-auto", expectedRevision = 1 },
            sessionId ?? session.Id, Principal(actor, tenant));

        AssertFailure(status, body, 404, "agent.session.not_found");
        await AssertUnchangedAsync(fixture, session.Id);
    }

    [Fact]
    public async Task Request_metadata_cannot_select_another_owner()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();

        var (status, body) = await fixture.SendAsync("ChangeAutonomy",
            new { autonomyMode = "full-auto", expectedRevision = 1, actorId = Actor, tenantId = Tenant, sessionId = session.Id },
            session.Id, Principal("actor-2", "tenant-2"));

        AssertFailure(status, body, 404, "agent.session.not_found");
        await AssertUnchangedAsync(fixture, session.Id);
    }

    [Fact]
    public async Task An_old_request_after_a_newer_update_is_stale_and_cannot_overwrite_it()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        Assert.Equal(200, (await fixture.ChangeAsync(session.Id, new { autonomyMode = "manual", expectedRevision = 1 })).Status);

        foreach (var mode in new[] { "full-auto", "manual" })
        {
            var (status, body) = await fixture.ChangeAsync(session.Id, new { autonomyMode = mode, expectedRevision = 1 });
            AssertFailure(status, body, 409, "agent.session.policy_stale");
        }

        AssertPolicy((await fixture.DetailAsync(session.Id)).GetProperty("policy"), "manual", 2);
    }

    [Fact]
    public async Task A_pending_prompt_makes_the_session_busy_until_it_is_terminal()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var (posted, postedBody) = await fixture.SendAsync("PostMessage", new { content = "hello", requestId = "r-1" }, session.Id);
        Assert.Equal(200, posted);
        var messageId = postedBody.RootElement.GetProperty("data").GetProperty("message").GetProperty("id").GetString()!;

        var (status, body) = await fixture.ChangeAsync(session.Id, new { autonomyMode = "full-auto", expectedRevision = 1 });
        AssertFailure(status, body, 409, "agent.session.busy");
        await AssertUnchangedAsync(fixture, session.Id);

        await fixture.Sessions.SetMessageStatusAsync(session.Id, messageId, AgentMessageStatus.Cancelled);
        Assert.Equal(200, (await fixture.ChangeAsync(session.Id, new { autonomyMode = "full-auto", expectedRevision = 1 })).Status);
    }

    [Fact]
    public async Task An_active_turn_makes_the_session_busy_until_its_foreground_claim_is_released()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var owner = (IAgentSessionPolicyOwner)fixture.Sessions;
        // The orchestrator holds exactly this claim from before it reads policy until Unregister completes.
        var foreground = owner.Acquire(session.Id, AgentSessionClaimKind.Foreground)!;

        var (status, body) = await fixture.ChangeAsync(session.Id, new { autonomyMode = "manual", expectedRevision = 1 });
        AssertFailure(status, body, 409, "agent.session.busy");
        await AssertUnchangedAsync(fixture, session.Id);

        owner.Release(foreground);
        Assert.Equal(200, (await fixture.ChangeAsync(session.Id, new { autonomyMode = "manual", expectedRevision = 1 })).Status);
    }

    [Fact]
    public async Task A_closed_or_expired_session_refuses_the_update()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        fixture.Clock.Advance(TimeSpan.FromHours(9));

        var (status, body) = await fixture.ChangeAsync(session.Id, new { autonomyMode = "full-auto", expectedRevision = 1 });

        AssertFailure(status, body, 409, "agent.session.closed");
        await AssertUnchangedAsync(fixture, session.Id);
    }

    [Fact]
    public async Task A_forwarding_participant_replacement_is_unsupported_and_policy_readback_is_absent()
    {
        using var fixture = new Fixture(services => services.Replace(ServiceDescriptor.Scoped<IAgentProposalService>(sp =>
            new ForwardingProposals(ActivatorUtilities.CreateInstance<InMemoryAgentProposalService>(sp)))));

        var (created, createdBody) = await fixture.SendAsync("CreateSession", new { conversationId = "c-1" });
        Assert.Equal(200, created);
        var createdData = createdBody.RootElement.GetProperty("data");
        Assert.False(createdData.TryGetProperty("policy", out _));
        var sessionId = createdData.GetProperty("sessionId").GetString()!;

        var (status, body) = await fixture.ChangeAsync(sessionId, new { autonomyMode = "full-auto", expectedRevision = 1 });

        AssertFailure(status, body, 409, "agent.session.autonomy_unsupported");
        await AssertUnchangedAsync(fixture, sessionId);
        Assert.False((await fixture.DetailAsync(sessionId)).TryGetProperty("policy", out _));
    }

    [Fact]
    public async Task A_prompt_that_loses_to_a_policy_change_is_refused_and_a_retry_is_attributed_to_the_new_revision()
    {
        var source = new PolicyChangingSource();
        using var fixture = new Fixture(services => services.AddScoped<IAgentContextProvider>(sp =>
        {
            source.Policies = sp.GetRequiredService<IAgentSessionPolicyService>();
            return source;
        }));
        var session = await fixture.CreateAsync();
        source.ChangeOnce = session;

        var (refused, refusedBody) = await fixture.SendAsync("PostMessage", new { content = "hello", requestId = "r-1" }, session.Id);

        AssertFailure(refused, refusedBody, 409, "agent.message.policy_changed");
        Assert.Empty(await fixture.Sessions.ListMessagesAsync(session.Id));
        AssertPolicy((await fixture.DetailAsync(session.Id)).GetProperty("policy"), "full-auto", 2);

        var (accepted, acceptedBody) = await fixture.SendAsync("PostMessage", new { content = "hello", requestId = "r-1" }, session.Id);
        Assert.Equal(200, accepted);
        var acceptedPolicy = acceptedBody.RootElement.GetProperty("data").GetProperty("message").GetProperty("acceptedPolicy");
        Assert.Equal(2, acceptedPolicy.GetProperty("revision").GetInt64());
        var message = Assert.Single((await fixture.DetailAsync(session.Id)).GetProperty("messages").EnumerateArray());
        Assert.Equal(2, message.GetProperty("acceptedPolicyRevision").GetInt64());
    }

    [Fact]
    public async Task Session_detail_attributes_each_message_to_its_accepted_revision_after_later_changes()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var first = await fixture.Sessions.AddMessageAsync(session.Id, new(AgentRole.User, "first", AgentMessageStatus.Pending, null, [], []));
        await fixture.Sessions.SetMessageStatusAsync(session.Id, first.Id, AgentMessageStatus.Completed);
        Assert.Equal(200, (await fixture.ChangeAsync(session.Id, new { autonomyMode = "manual", expectedRevision = 1 })).Status);
        await fixture.Sessions.AddMessageAsync(session.Id, new(AgentRole.User, "second", AgentMessageStatus.Completed, null, [], []));

        var messages = (await fixture.DetailAsync(session.Id)).GetProperty("messages").EnumerateArray().ToArray();

        Assert.Equal([1L, 2L], messages.Select(x => x.GetProperty("acceptedPolicyRevision").GetInt64()).ToArray());
        Assert.Equal(["first", "second"], messages.Select(x => x.GetProperty("content").GetString()!).ToArray());
    }

    [Fact]
    public void A_message_without_owner_attribution_reads_back_a_null_accepted_revision()
    {
        var view = new AgentMessage("m", "s", AgentRole.User, "c", AgentMessageStatus.Completed, null, DateTimeOffset.UtcNow, null, null, [], [], [])
            .ToViewModel();
        var json = JsonSerializer.Serialize(view, AgentJsonContext.Default.AgentMessageViewModelResponse);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("acceptedPolicyRevision").ValueKind);
    }

    // ---- Helpers ----

    private static void AssertPolicy(JsonElement policy, string mode, long revision)
    {
        Assert.Equal(mode, policy.GetProperty("autonomyMode").GetString());
        Assert.Equal("full-auto", policy.GetProperty("maxAutonomyMode").GetString());
        Assert.Equal(["manual", "auto-read-only", "full-auto"], policy.GetProperty("allowedAutonomyModes").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal(revision, policy.GetProperty("revision").GetInt64());
        Assert.Equal(4, policy.EnumerateObject().Count());
    }

    private static void AssertFailure(int status, JsonDocument body, int expectedStatus, string code)
    {
        Assert.Equal(expectedStatus, status);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
        var error = body.RootElement.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal(expectedStatus, error.GetProperty("statusCode").GetInt32());
    }

    private static async Task AssertUnchangedAsync(Fixture fixture, string sessionId)
    {
        var stored = await fixture.FindAsync(sessionId);
        Assert.Equal(AgentAutonomyMode.AutoReadOnly, stored.Policy.AutonomyMode);
        Assert.Equal(1, stored.PolicyRevision);
    }

    private static ClaimsPrincipal Principal(string? actor, string? tenant)
    {
        var claims = new List<Claim>();
        if (actor is not null) claims.Add(new(ClaimTypes.NameIdentifier, actor));
        if (tenant is not null) claims.Add(new("elsa.identity.tenant_id", tenant));
        return new(new ClaimsIdentity(claims, "test"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IReadOnlyCollection<Endpoint> _endpoints;

        public Fixture(Action<IServiceCollection>? configure = null)
        {
            Root = AgentTestServices.Create(services =>
            {
                services.AddRouting().AddElsaEndpoints();
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
                services.AddSingleton<IAgentProvider, DeterministicAgentProvider>();
                configure?.Invoke(services);
            });
            Scope = Root.CreateScope();
            var app = new TestEndpointRouteBuilder(Scope.ServiceProvider);
            AgentApi.MapAgentApi(app);
            _endpoints = app.DataSources.SelectMany(x => x.Endpoints).ToList();
        }

        public ProposalTestClock Clock { get; } = new();
        public ServiceProvider Root { get; }
        public IServiceScope Scope { get; }
        public IAgentSessionService Sessions => Scope.ServiceProvider.GetRequiredService<IAgentSessionService>();

        public Task<AgentSession> CreateAsync(AgentPolicy? policy = null) => Sessions.CreateAsync(new(
            Tenant, Actor, "conversation-1", DeterministicAgentProvider.Id, "explain", "Autonomy session", policy ?? AgentPolicy.Default,
            new Dictionary<string, string>()));

        public async Task<AgentSession> FindAsync(string sessionId) => (await Sessions.FindAsync(sessionId))!;

        public Task<(int Status, JsonDocument Body)> ChangeAsync(string sessionId, object body) => SendAsync("ChangeAutonomy", body, sessionId);

        public async Task<JsonElement> DetailAsync(string sessionId)
        {
            var (status, body) = await SendRawAsync("GetSession", null, sessionId);
            Assert.Equal(200, status);
            return body.RootElement.GetProperty("data");
        }

        public Task<(int Status, JsonDocument Body)> SendAsync(string operation, object body, string? sessionId = null, ClaimsPrincipal? user = null)
            => SendRawAsync(operation, JsonSerializer.Serialize(body), sessionId, user);

        public async Task<(int Status, JsonDocument Body)> SendRawAsync(string operation, string? json, string? sessionId = null, ClaimsPrincipal? user = null)
        {
            var endpoint = Assert.Single(_endpoints, x => x.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaAgentApiEndpoints" + operation);
            using var request = Root.CreateScope();
            var context = new DefaultHttpContext { RequestServices = request.ServiceProvider, User = user ?? Principal(Actor, Tenant) };
            if (sessionId is not null)
                context.Request.RouteValues["sessionId"] = sessionId;
            if (json is not null)
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                context.Request.ContentType = "application/json";
                context.Request.Body = new MemoryStream(bytes);
                context.Request.ContentLength = bytes.Length;
            }
            var response = new MemoryStream();
            context.Response.Body = response;

            await endpoint.RequestDelegate!(context);

            return (context.Response.StatusCode, JsonDocument.Parse(response.ToArray()));
        }

        public void Dispose()
        {
            Scope.Dispose();
            Root.Dispose();
        }
    }

    /// <summary>An installed context source that commits one policy change while a prompt is being authorized.</summary>
    private sealed class PolicyChangingSource : IAgentContextProvider
    {
        public AgentContextProviderDescriptor Descriptor { get; } = new("autonomy.source", "autonomy.module", "*", ["autonomy.context"], ["1"], []);
        public IAgentSessionPolicyService Policies { get; set; } = null!;
        public AgentSession? ChangeOnce { get; set; }

        public async ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
        {
            if (ChangeOnce is { } session)
            {
                ChangeOnce = null;
                var changed = await Policies.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, AgentAutonomyMode.FullAuto, 1), cancellationToken);
                Assert.Equal(AgentSessionPolicyOutcome.Succeeded, changed.Outcome);
            }
            return new([], []);
        }
    }

    private sealed class ForwardingProposals(IAgentProposalService inner) : IAgentProposalService
    {
        public Task<AgentActionProposal> AddAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default) => inner.AddAsync(proposal, cancellationToken);
        public Task<AgentActionProposal?> FindAsync(string proposalId, CancellationToken cancellationToken = default) => inner.FindAsync(proposalId, cancellationToken);
        public Task<AgentResult<AgentActionProposal>> ApproveAsync(string proposalId, string actorId, string? expectedRevision = null, string? comment = null, CancellationToken cancellationToken = default)
            => inner.ApproveAsync(proposalId, actorId, expectedRevision, comment, cancellationToken);
        public Task<AgentResult<AgentActionProposal>> DenyAsync(string proposalId, string actorId, string? reason, CancellationToken cancellationToken = default)
            => inner.DenyAsync(proposalId, actorId, reason, cancellationToken);
        public Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(string proposalId, string actorId, string? expectedRevision = null, CancellationToken cancellationToken = default)
            => inner.ExecuteAsync(proposalId, actorId, expectedRevision, cancellationToken);
        public Task<AgentResult<AgentProposalExecutionResult>> AcknowledgeAsync(string sessionId, string invocationId, string actorId, AgentClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
            => inner.AcknowledgeAsync(sessionId, invocationId, actorId, acknowledgement, cancellationToken);
        public Task<AgentToolResult?> FindResultAsync(string proposalId, CancellationToken cancellationToken = default) => inner.FindResultAsync(proposalId, cancellationToken);
    }
}
