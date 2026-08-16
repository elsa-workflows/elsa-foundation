using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using System.Net;
using System.Text;
using Xunit;

namespace Elsa.Architecture.Tests;

[Collection(Wave4AgentFastEndpointsCollection.Name)]
public sealed class Wave4AgentPolicyAuditTests
{
    [Fact]
    public async Task Policy_denial_audit_preserves_index_and_code_metadata()
    {
        var evaluator = new DenyingPolicyEvaluator();
        var auditSink = new TrackingAuditSink();
        await using var host = await Wave4AgentMinimalApiHost.StartAsync(policyEvaluator: evaluator, auditSink: auditSink);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/_elsa/agent/sessions/session-1/messages")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add(Wave4AgentHost.IdentityHeader, "use");
        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("\"code\":\"agent.policyDenied\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.NotNull(auditSink.Event);
        Assert.Equal(AgentAuditEventKind.ContextDenied, auditSink.Event!.Kind);
        Assert.Equal("session-1", auditSink.Event.SessionId);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["0:agent.disabled"] = "Agent policy is disabled.",
                ["1:agent.capability.denied"] = "The requested capability is denied."
            },
            auditSink.Event.Metadata);
    }

    private sealed class DenyingPolicyEvaluator : IAgentPolicyEvaluator
    {
        public ValueTask<AgentPolicyDecision> EvaluateAvailabilityAsync(AgentPolicy policy, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AgentPolicyDecision(false,
            [
                new AgentViolation("agent.disabled", "Agent policy is disabled."),
                new AgentViolation("agent.capability.denied", "The requested capability is denied.")
            ]));

        public ValueTask<AgentPolicyDecision> EvaluateContextAsync(AgentPolicy policy, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AgentPolicyDecision(true, []));

        public ValueTask<AgentPolicyDecision> EvaluateCapabilityAsync(AgentPolicy policy, string capabilityId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AgentPolicyDecision(true, []));
    }

    private sealed class TrackingAuditSink : IAgentAuditSink
    {
        public AgentAuditEvent? Event { get; private set; }

        public Task EmitAsync(AgentAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Event = auditEvent;
            return Task.CompletedTask;
        }
    }
}
