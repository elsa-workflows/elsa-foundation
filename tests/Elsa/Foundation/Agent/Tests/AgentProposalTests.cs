using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Extensions;
using Elsa.Foundation.Agent.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Agent.Tests;

public sealed class AgentProposalTests
{
    [Fact]
    public async Task Proposal_requires_approval_before_execution()
    {
        using var provider = BuildAgentProvider();
        var proposals = provider.GetRequiredService<IAgentProposalService>();
        var proposal = await proposals.AddAsync(CreateProposal(requiresApproval: true));

        var result = await proposals.ExecuteAsync(proposal.Id, "reviewer", "rev-1");

        Assert.False(result.Succeeded);
        Assert.Equal("agent.proposal.approval_required", result.Error?.Code);
    }

    [Fact]
    public async Task Proposal_lifecycle_emits_audit_events()
    {
        using var provider = BuildAgentProvider();
        var proposals = provider.GetRequiredService<IAgentProposalService>();
        var audit = provider.GetRequiredService<IAgentAuditReader>();
        var proposal = await proposals.AddAsync(CreateProposal(requiresApproval: true));

        var approval = await proposals.ApproveAsync(proposal.Id, "reviewer", "rev-1", "Looks safe.");

        Assert.True(approval.Succeeded);
        var events = await audit.ListAsync("session-1");
        Assert.Contains(events, x => x.Kind == AgentAuditEventKind.ProposalCreated);
        Assert.Contains(events, x => x.Kind == AgentAuditEventKind.ProposalApproved && x.ActorId == "reviewer");
    }

    [Fact]
    public async Task Proposal_approval_rejects_revision_mismatch()
    {
        using var provider = BuildAgentProvider();
        var proposals = provider.GetRequiredService<IAgentProposalService>();
        var proposal = await proposals.AddAsync(CreateProposal(requiresApproval: true));

        var approval = await proposals.ApproveAsync(proposal.Id, "reviewer", "rev-2");

        Assert.False(approval.Succeeded);
        Assert.Equal("agent.proposal.revision_conflict", approval.Error?.Code);
    }

    [Fact]
    public async Task Feedback_emits_audit_event_with_message_linkage()
    {
        using var provider = BuildAgentProvider();
        var feedback = provider.GetRequiredService<IAgentFeedbackService>();
        var audit = provider.GetRequiredService<IAgentAuditReader>();

        await feedback.AddAsync(new(
            "feedback-1",
            "session-1",
            "msg-1",
            "positive",
            "Helpful.",
            "user-1",
            DateTimeOffset.UtcNow));

        var events = await audit.ListAsync("session-1");
        var feedbackEvent = Assert.Single(events, x => x.Kind == AgentAuditEventKind.FeedbackReceived);
        Assert.Equal("msg-1", feedbackEvent.Metadata["messageId"]);
        Assert.Equal("positive", feedbackEvent.Metadata["rating"]);
    }

    private static ServiceProvider BuildAgentProvider()
    {
        var services = new ServiceCollection();
        services.AddFoundationAgentAbstractions();
        return services.BuildServiceProvider();
    }

    private static AgentActionProposal CreateProposal(bool requiresApproval)
    {
        var now = DateTimeOffset.UtcNow;
        return new(
            "proposal-1",
            "session-1",
            "msg-1",
            "workflow.propose-change",
            "workflow-change",
            "Update workflow",
            "Reviewable change.",
            AgentRisk.ReviewRequired,
            "rev-1",
            [new("workflow.json", "modify", "Update an activity input.")],
            [new Dictionary<string, object?> { ["op"] = "replace-input" }],
            ["Changes draft workflow behavior."],
            "Restore the previous draft revision.",
            ["workflow.proposals"],
            "workflow-definition",
            "workflow-1",
            requiresApproval,
            AgentActionProposalStatus.AwaitingApproval,
            null,
            null,
            now,
            now);
    }
}
