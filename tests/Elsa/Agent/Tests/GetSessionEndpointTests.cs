using System.Security.Claims;
using Elsa.Agent.Api.Models;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Elsa.Agent.Tests;

/// <summary>
/// Endpoint-level coverage for the shared session-access guard (issue #414 item 1): the session-scoped
/// endpoints authorize through <c>AgentSessionAuthorization</c>, and the wire behavior must stay exactly
/// what the inline guards produced — 404 with <c>agent.session.not_found</c> for a missing session,
/// 403 with <c>agent.session.forbidden</c> for a foreign principal, 200 for the owner. GetSession is the
/// representative endpoint; it is internal, so it is built through FastEndpoints' <see cref="Factory"/>
/// via reflection.
/// </summary>
public sealed class GetSessionEndpointTests
{
    [Fact]
    public async Task Missing_session_returns_404_not_found_error()
    {
        var endpoint = CreateEndpoint(new StubSessionService(session: null), OwnerPrincipal());

        await HandleAsync(endpoint, new AgentSessionRouteRequest { SessionId = "missing" });

        Assert.Equal(StatusCodes.Status404NotFound, endpoint.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Foreign_principal_returns_403_forbidden_error()
    {
        var endpoint = CreateEndpoint(new StubSessionService(Session()), Principal("intruder", "tenant-1"));

        await HandleAsync(endpoint, new AgentSessionRouteRequest { SessionId = "session-1" });

        Assert.Equal(StatusCodes.Status403Forbidden, endpoint.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Owner_returns_200_with_session_details()
    {
        var endpoint = CreateEndpoint(new StubSessionService(Session()), OwnerPrincipal());

        await HandleAsync(endpoint, new AgentSessionRouteRequest { SessionId = "session-1" });

        Assert.Equal(StatusCodes.Status200OK, endpoint.HttpContext.Response.StatusCode);
    }

    private static ClaimsPrincipal OwnerPrincipal() => Principal("user-1", "tenant-1");

    private static ClaimsPrincipal Principal(string actorId, string tenantId) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, actorId), new Claim("tenant_id", tenantId)],
            authenticationType: "test"));

    private static AgentSession Session()
    {
        var now = DateTimeOffset.UtcNow;
        return new(
            "session-1",
            "Session",
            "tenant-1",
            "user-1",
            "conversation-1",
            "provider-1",
            "chat",
            AgentPolicy.Default,
            AgentSessionStatus.Active,
            now,
            now,
            null,
            new Dictionary<string, string>());
    }

    private static BaseEndpoint CreateEndpoint(IAgentSessionService sessions, ClaimsPrincipal user)
    {
        var endpointType = typeof(AgentSessionRouteRequest).Assembly
            .GetType("Elsa.Agent.Api.Endpoints.GetSession", throwOnError: true)!;

        var create = typeof(Factory).GetMethods()
            .Single(m => m.Name == nameof(Factory.Create)
                         && m.IsGenericMethodDefinition
                         && m.GetParameters() is [var first, var rest]
                         && first.ParameterType == typeof(Action<DefaultHttpContext>)
                         && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);

        Action<DefaultHttpContext> configureContext = context =>
        {
            context.User = user;
            context.Response.Body = new MemoryStream();
        };

        return (BaseEndpoint)create.Invoke(null, [configureContext, new object[] { sessions }])!;
    }

    private static Task HandleAsync(BaseEndpoint endpoint, AgentSessionRouteRequest request) =>
        (Task)endpoint.GetType()
            .GetMethod("HandleAsync", [typeof(AgentSessionRouteRequest), typeof(CancellationToken)])!
            .Invoke(endpoint, [request, CancellationToken.None])!;

    private sealed class StubSessionService(AgentSession? session) : IAgentSessionService
    {
        public Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not used by GetSession.");

        public Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(session);

        public Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not used by GetSession.");

        public Task<IReadOnlyCollection<AgentMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<AgentMessage>>([]);

        public Task<AgentMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentMessage?>(null);

        public Task<AgentMessage?> FindLatestMessageAsync(string sessionId, AgentRole? role = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentMessage?>(null);

        public Task AddContextAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyCollection<AgentContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<AgentContextAttachment>>([]);
    }
}
