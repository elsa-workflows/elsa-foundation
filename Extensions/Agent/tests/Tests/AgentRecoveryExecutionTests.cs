using System.Security.Claims;
using System.Text.Json;
using Elsa.Agent.Api;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 T012 (story 2.3): after a restart, an Approved, undispatched operation executes once, only on the owner's
/// explicit execute, and only while every approval condition still holds; otherwise it is Expired or a zero-effect refusal.
/// </summary>
public sealed class AgentRecoveryExecutionTests
{
    [Theory]
    [InlineData("deadline", "agent.proposal.expired", AgentActionProposalStatus.Expired)]
    [InlineData("policy-revision", "agent.proposal.policy_stale", AgentActionProposalStatus.Failed)]
    [InlineData("approval-binding", "agent.proposal.binding_invalid", AgentActionProposalStatus.Failed)]
    [InlineData("target-revision", "test.revision_changed", AgentActionProposalStatus.Failed)]
    [InlineData("authority", "agent.proposal.forbidden", AgentActionProposalStatus.Failed)]
    [InlineData("risk", "agent.proposal.contract_changed", AgentActionProposalStatus.Failed)]
    [InlineData("host-policy", "agent.disabled", AgentActionProposalStatus.Failed)]
    public async Task An_approved_operation_whose_condition_no_longer_holds_after_a_restart_is_refused_with_zero_effect(
        string condition, string code, AgentActionProposalStatus status)
    {
        var authority = new ProposalTestAuthorizer();
        var evaluator = new SwitchablePolicyEvaluator();
        using var harness = new AgentRestartHarness(configure: services =>
        {
            services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer>(_ => authority));
            services.Replace(ServiceDescriptor.Scoped<IAgentPolicyEvaluator>(_ => evaluator));
        });
        var (session, proposal) = await ApprovedAsync(harness);
        var host = harness.Restart();
        Assert.Equal(AgentActionProposalStatus.Approved, (await host.Proposals.FindAsync(proposal.Id))!.Status);

        switch (condition)
        {
            case "deadline": harness.Clock.Advance(TimeSpan.FromMinutes(6)); break;
            case "policy-revision":
                // Normally unreachable while the restored claim holds; prove the recheck independently of the claim.
                host.Owner.ReleaseProposal(session.Id, proposal.Id);
                Assert.Equal(AgentSessionPolicyOutcome.Succeeded,
                    (await host.Policy.ChangeAutonomyAsync(new(session.Id, session.ActorId, session.TenantId, AgentAutonomyMode.FullAuto, 1))).Outcome);
                break;
            case "approval-binding":
                host.ProposalStore.Proposals[proposal.Id] = host.ProposalStore.Proposals[proposal.Id] with { ApprovedBy = "someone-else" };
                break;
            case "target-revision": harness.Module.ChangeByUser(); break;
            case "authority": authority.Allowed = false; break;
            case "risk": harness.Module.Risk = AgentRisk.Destructive; break;
            case "host-policy": evaluator.Available = false; break;
        }

        var refused = await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);

        Assert.Equal(code, refused.Error?.Code);
        Assert.Equal(status, (await host.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.Equal(status, (await harness.Store.LoadAsync(session.Id))!.Proposals.Single().Proposal.Status);
        Assert.Equal(0, harness.Module.Effects);
        // The refusal is retained: a repeated execute reports it again and still dispatches nothing.
        Assert.Equal(code, (await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Error?.Code);
        Assert.Equal(0, harness.Module.Effects);
    }

    [Fact]
    public async Task An_approved_operation_executes_once_after_a_restart_and_never_again_after_another()
    {
        using var harness = new AgentRestartHarness();
        var (session, proposal) = await ApprovedAsync(harness);

        var host = harness.Restart();
        var executed = await host.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        Assert.True(executed.Value!.Executed);

        var again = harness.Restart();
        var retained = await again.Proposals.ExecuteAsync(proposal.Id, session.ActorId, proposal.BaseRevision);
        Assert.True(retained.Value!.Executed);
        Assert.Equal(executed.Value.ToolResult!.Receipt!.InvocationId, retained.Value.ToolResult!.Receipt!.InvocationId);
        Assert.Equal((1, 1), (harness.Module.Dispatches, harness.Module.Effects));
    }

    [Fact]
    public async Task The_execute_route_maps_a_stale_policy_basis_to_409_with_the_recorded_refusal()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), configure: services => services.AddRouting().AddElsaEndpoints());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        var owner = (Services.InMemoryAgentSessionService)fixture.Services.GetRequiredService<IAgentSessionService>();
        owner.ReleaseProposal(fixture.Session.Id, proposal.Id);
        Assert.True((await fixture.Services.GetRequiredService<IAgentSessionPolicyService>().ChangeAutonomyAsync(
            new(fixture.Session.Id, fixture.Session.ActorId, fixture.Session.TenantId, AgentAutonomyMode.FullAuto, 1))).Succeeded);

        var (status, body) = await AgentApiTestCalls.ExecuteAsync(fixture, proposal, proposal.BaseRevision);

        Assert.Equal(409, status);
        Assert.Equal("agent.proposal.policy_stale", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("failed", body.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
    }

    private static async Task<(AgentSession Session, AgentActionProposal Proposal)> ApprovedAsync(AgentRestartHarness harness)
    {
        var session = await harness.Host.CreateAsync();
        var proposal = (await harness.Host.Invoker.DispatchAsync(AgentRecoveryRestartTests.Call(session), session.Policy)).Proposal!;
        Assert.True((await harness.Host.Proposals.ApproveAsync(proposal.Id, session.ActorId, proposal.BaseRevision)).Succeeded);
        return (session, proposal);
    }

    /// <summary>The default policy evaluator, except that host availability can be withdrawn.</summary>
    private sealed class SwitchablePolicyEvaluator : IAgentPolicyEvaluator
    {
        private readonly Services.DefaultAgentPolicyEvaluator _inner = new();
        public bool Available { get; set; } = true;
        public ValueTask<AgentPolicyDecision> EvaluateAvailabilityAsync(AgentPolicy policy, CancellationToken cancellationToken = default)
            => Available ? _inner.EvaluateAvailabilityAsync(policy, cancellationToken)
                : ValueTask.FromResult(new AgentPolicyDecision(false, [new("agent.disabled", "Disabled by the host for this test.")]));
        public ValueTask<AgentPolicyDecision> EvaluateContextAsync(AgentPolicy policy, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default)
            => _inner.EvaluateContextAsync(policy, attachments, cancellationToken);
        public ValueTask<AgentPolicyDecision> EvaluateCapabilityAsync(AgentPolicy policy, string capabilityId, CancellationToken cancellationToken = default)
            => _inner.EvaluateCapabilityAsync(policy, capabilityId, cancellationToken);
    }
}

/// <summary>Calls one proposal decision route in-process as the fixture's actor.</summary>
internal static class AgentApiTestCalls
{
    public static async Task<(int Status, JsonElement Body)> ExecuteAsync(ProposalFixture fixture, AgentActionProposal proposal, string? revision)
    {
        var app = new TestEndpointRouteBuilder(fixture.Services);
        AgentApi.MapAgentApi(app);
        var endpoint = Assert.Single(app.DataSources.SelectMany(source => source.Endpoints), item =>
            item.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaAgentApiEndpointsExecuteProposal");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { revision });
        var context = new DefaultHttpContext
        {
            RequestServices = fixture.Services,
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, ProposalFixture.Actor), new Claim("elsa.identity.tenant_id", fixture.Session.TenantId)], "test"))
        };
        context.Request.RouteValues["proposalId"] = proposal.Id;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, response.RootElement.Clone());
    }
}
