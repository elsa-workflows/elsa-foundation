using System.Text;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Xunit;

namespace Elsa.Architecture.Tests;

[Collection(Wave4AgentFastEndpointsCollection.Name)]
public sealed class Wave4AgentRouteFallbackTests
{
    [Fact]
    public async Task Create_session_collects_workflow_context_from_the_active_surface_route_when_resource_id_is_missing()
    {
        var collector = new TrackingContextCollector();
        await using var host = await Wave4AgentMinimalApiHost.StartAsync(contextCollector: collector);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_elsa/agent/sessions")
        {
            Content = new StringContent("{\"activeSurface\":{\"route\":\"/workflows/workflow-42/editor\"}}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add(Wave4AgentHost.IdentityHeader, "use");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("workflow-42", collector.WorkflowDefinitionId);
    }

    [Fact]
    public async Task Cancel_turn_uses_the_route_turn_id_when_the_json_body_omits_it()
    {
        var registry = new TrackingTurnRegistry();
        await using var host = await Wave4AgentMinimalApiHost.StartAsync(turnRegistry: registry);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_elsa/agent/sessions/session-1/turns/turn-42/cancel")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add(Wave4AgentHost.IdentityHeader, "use");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("turn-42", registry.CancelledTurnId);
        Assert.Contains("\"turnId\":\"turn-42\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private sealed class TrackingContextCollector : IAgentContextCollector
    {
        public string? WorkflowDefinitionId { get; private set; }

        public Task<AgentResult<IReadOnlyCollection<AgentContextAttachment>>> CollectAsync(
            AgentPolicy policy,
            AgentContextRequest request,
            CancellationToken cancellationToken = default)
        {
            WorkflowDefinitionId = request.Inputs.GetValueOrDefault("workflowDefinitionId");
            return Task.FromResult(AgentResult<IReadOnlyCollection<AgentContextAttachment>>.Success([]));
        }
    }

    private sealed class TrackingTurnRegistry : IAgentTurnRegistry
    {
        public string? CancelledTurnId { get; private set; }

        public CancellationToken Register(string turnId) => CancellationToken.None;

        public bool Cancel(string turnId)
        {
            CancelledTurnId = turnId;
            return true;
        }

        public void Unregister(string turnId) { }
    }
}
