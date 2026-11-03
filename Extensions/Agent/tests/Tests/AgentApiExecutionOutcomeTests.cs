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

    [Theory]
    [InlineData("Approve", "expired", "agent.proposal.expired", false)]
    [InlineData("Approve", "denied", "agent.proposal.invalid_state", true)]
    [InlineData("Approve", "failed", "agent.proposal.invalid_state", true)]
    [InlineData("Approve", "cancelled", "agent.proposal.invalid_state", false)]
    [InlineData("Deny", "expired", "agent.proposal.closed", false)]
    [InlineData("Deny", "failed", "agent.proposal.closed", true)]
    [InlineData("Deny", "cancelled", "agent.proposal.closed", false)]
    public async Task Terminal_review_decision_failure_exposes_current_outcome_without_execution(
        string decision, string status, string errorCode, bool retainedToolResult)
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), configure: services => services.AddRouting().AddElsaEndpoints());
        var proposal = await fixture.ProposeAsync();
        switch (status)
        {
            case "expired":
                fixture.Clock.Advance(TimeSpan.FromMinutes(6));
                if (decision == "Deny")
                    Assert.Equal("agent.proposal.expired", (await fixture.ApproveAsync(proposal)).Error!.Code);
                break;
            case "denied":
                Assert.True((await fixture.Proposals.DenyAsync(proposal.Id, ProposalFixture.Actor, "Do not apply.")).Succeeded);
                break;
            case "failed":
                await fixture.ApproveAsync(proposal);
                fixture.Mutation!.ChangeByUser("user edit");
                Assert.Equal("test.stale", (await fixture.ExecuteAsync(proposal)).Error!.Code);
                break;
            case "cancelled":
                fixture.Services.GetRequiredService<InMemoryAgentProposalStore>().Proposals[proposal.Id] =
                    (await fixture.Proposals.FindAsync(proposal.Id))! with { Status = AgentActionProposalStatus.Cancelled };
                break;
        }

        var response = await DecisionAsync(fixture, proposal, decision, proposal.BaseRevision);

        Assert.Equal(StatusCodes.Status409Conflict, response.Status);
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
        Assert.Equal(status, (await fixture.Proposals.FindAsync(proposal.Id))!.Status.ToString().ToLowerInvariant());
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
        Assert.Equal(status == "failed" ? "user edit" : "initial", fixture.Mutation.Value);
    }

    [Fact]
    public async Task Approval_revision_conflict_has_no_terminal_outcome_or_effect()
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), configure: services => services.AddRouting().AddElsaEndpoints());
        var proposal = await fixture.ProposeAsync();

        var response = await DecisionAsync(fixture, proposal, "Approve", "wrong-revision");

        Assert.Equal(StatusCodes.Status409Conflict, response.Status);
        Assert.Equal("agent.proposal.revision_conflict", response.Body.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("data").ValueKind);
        Assert.Equal(AgentActionProposalStatus.AwaitingApproval, (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
        Assert.Equal("initial", fixture.Mutation.Value);
    }

    [Theory]
    [InlineData("Approve", true)]
    [InlineData("Approve", false)]
    [InlineData("Deny", true)]
    [InlineData("Deny", false)]
    public async Task Unauthorized_review_decision_does_not_expose_terminal_outcome(string decision, bool differentActor)
    {
        using var fixture = await ProposalFixture.CreateAsync(new ProposalMutationTool(), configure: services => services.AddRouting().AddElsaEndpoints());
        var proposal = await fixture.ProposeAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal("agent.proposal.expired", (await fixture.ApproveAsync(proposal)).Error!.Code);

        var response = await DecisionAsync(fixture, proposal, decision, proposal.BaseRevision,
            differentActor ? "other-actor" : ProposalFixture.Actor, differentActor ? fixture.Session.TenantId : "other-tenant");

        Assert.Equal(StatusCodes.Status403Forbidden, response.Status);
        Assert.Equal("agent.proposal.forbidden", response.Body.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("data").ValueKind);
        Assert.DoesNotContain(proposal.ResourceId!, response.Body.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(AgentActionProposalStatus.Expired, (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
        Assert.Equal(0, fixture.Mutation!.ExecuteCalls);
        Assert.Equal("initial", fixture.Mutation.Value);
    }

    private static Task<(int Status, JsonElement Body)> ExecuteAsync(ProposalFixture fixture, AgentActionProposal proposal, string? revision)
        => DecisionAsync(fixture, proposal, "Execute", revision);

    private static async Task<(int Status, JsonElement Body)> DecisionAsync(ProposalFixture fixture, AgentActionProposal proposal,
        string decision, string? revision, string? actor = null, string? tenant = null)
    {
        var app = new TestEndpointRouteBuilder(fixture.Services);
        AgentApi.MapAgentApi(app);
        var endpoint = Assert.Single(app.DataSources.SelectMany(source => source.Endpoints), item =>
            item.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == $"ElsaAgentApiEndpoints{decision}Proposal");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { revision });
        var context = new DefaultHttpContext
        {
            RequestServices = fixture.Services,
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, actor ?? ProposalFixture.Actor), new Claim("elsa.identity.tenant_id", tenant ?? fixture.Session.TenantId)], "test"))
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
