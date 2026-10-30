using System.Security.Claims;
using System.Text.Json;
using Elsa.Agent.Api;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Elsa.Api.AspNetCore;
using Elsa.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

public sealed class AgentApiExecutionOutcomeTests
{
    [Theory]
    [InlineData("expired", "agent.proposal.expired", "expired", 409, false)]
    [InlineData("stale", "test.stale", "failed", 409, true)]
    [InlineData("revoked", "agent.proposal.forbidden", "failed", 403, true)]
    [InlineData("denied", "agent.proposal.closed", "denied", 409, true)]
    [InlineData("cancelled", "agent.proposal.closed", "cancelled", 409, false)]
    public async Task Terminal_execute_failure_preserves_error_and_exposes_authoritative_non_success_outcome(
        string scenario, string errorCode, string status, int httpStatus, bool retainedToolResult)
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), configure: services => services.AddRouting().AddElsaEndpoints());
        var proposal = await fixture.ProposeAsync();
        await fixture.ApproveAsync(proposal);
        switch (scenario)
        {
            case "expired": fixture.Clock.Advance(TimeSpan.FromMinutes(6)); break;
            case "stale": fixture.Mutation!.ChangeByUser("user edit"); break;
            case "revoked": fixture.Authority.Allowed = false; break;
            case "denied": await fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "Do not apply."); break;
            case "cancelled":
                var store = fixture.Services.GetRequiredService<InMemoryAgentProposalStore>();
                store.Proposals[proposal.Id] = (await fixture.Proposals.FindAsync(proposal.Id))! with { Status = AgentActionProposalStatus.Cancelled };
                break;
        }

        var response = await ExecuteAsync(fixture, proposal, proposal.BaseRevision);

        Assert.Equal(httpStatus, response.Status);
        Assert.Equal(errorCode, response.Body.GetProperty("error").GetProperty("code").GetString());
        var data = response.Body.GetProperty("data");
        Assert.Equal(JsonValueKind.Object, data.ValueKind);
        Assert.Equal(proposal.Id, data.GetProperty("proposalId").GetString());
        Assert.Equal(status, data.GetProperty("status").GetString());
        Assert.False(data.GetProperty("executed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("clientInvocation").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("summary").GetString()));
        var tool = data.GetProperty("toolResult");
        Assert.Equal(retainedToolResult ? JsonValueKind.Object : JsonValueKind.Null, tool.ValueKind);
        if (retainedToolResult)
        {
            Assert.False(tool.GetProperty("succeeded").GetBoolean());
            Assert.Equal(JsonValueKind.Null, tool.GetProperty("receipt").ValueKind);
            Assert.Equal("agent.operation-outcome", tool.GetProperty("payload").GetProperty("typeId").GetString());
        }
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
        Assert.Equal(scenario == "stale" ? "user edit" : "initial", fixture.Mutation.Value);
        Assert.Equal(status, (await fixture.Proposals.FindAsync(proposal.Id))!.Status.ToString().ToLowerInvariant());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retryable_review_or_revision_conflict_has_no_terminal_outcome(bool wrongRevision)
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), configure: services => services.AddRouting().AddElsaEndpoints());
        var proposal = await fixture.ProposeAsync();
        if (wrongRevision) await fixture.ApproveAsync(proposal);

        var response = await ExecuteAsync(fixture, proposal, wrongRevision ? "wrong-revision" : proposal.BaseRevision);

        Assert.Equal(StatusCodes.Status409Conflict, response.Status);
        Assert.Equal(wrongRevision ? "agent.proposal.revision_conflict" : "agent.proposal.approval_required",
            response.Body.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("data").ValueKind);
        Assert.Equal(wrongRevision ? AgentActionProposalStatus.Approved : AgentActionProposalStatus.AwaitingApproval,
            (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
    }

    private static async Task<(int Status, JsonElement Body)> ExecuteAsync(ProposalFixture fixture, AgentActionProposal proposal, string? revision)
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
