using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Extensions;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Agent.Tests;

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
    public async Task Proposal_approval_requires_expected_revision_when_base_revision_exists()
    {
        using var provider = BuildAgentProvider();
        var proposals = provider.GetRequiredService<IAgentProposalService>();
        var proposal = await proposals.AddAsync(CreateProposal(requiresApproval: true));

        var approval = await proposals.ApproveAsync(proposal.Id, "reviewer");

        Assert.False(approval.Succeeded);
        Assert.Equal("agent.proposal.revision_conflict", approval.Error?.Code);
    }

    [Fact]
    public async Task Proposal_execution_rejects_denied_proposal_even_without_approval_requirement()
    {
        using var provider = BuildAgentProvider();
        var proposals = provider.GetRequiredService<IAgentProposalService>();
        var proposal = await proposals.AddAsync(CreateProposal(requiresApproval: false) with
        {
            Status = AgentActionProposalStatus.Denied
        });

        var result = await proposals.ExecuteAsync(proposal.Id, "reviewer", "rev-1");

        Assert.False(result.Succeeded);
        Assert.Equal("agent.proposal.closed", result.Error?.Code);
    }

    [Fact]
    public async Task Proposal_denial_rejects_executed_proposal()
    {
        using var provider = BuildAgentProvider();
        var proposals = provider.GetRequiredService<IAgentProposalService>();
        var proposal = await proposals.AddAsync(CreateProposal(requiresApproval: false));

        var execution = await proposals.ExecuteAsync(proposal.Id, "reviewer", "rev-1");
        var denial = await proposals.DenyAsync(proposal.Id, "reviewer", "Too late.");

        Assert.True(execution.Succeeded);
        Assert.False(denial.Succeeded);
        Assert.Equal("agent.proposal.closed", denial.Error?.Code);
    }

    [Fact]
    public async Task Proposal_approval_rejects_invalid_non_reviewable_state()
    {
        using var provider = BuildAgentProvider();
        var proposals = provider.GetRequiredService<IAgentProposalService>();
        var proposal = await proposals.AddAsync(CreateProposal(requiresApproval: true) with
        {
            Status = AgentActionProposalStatus.Draft
        });

        var approval = await proposals.ApproveAsync(proposal.Id, "reviewer", "rev-1");

        Assert.False(approval.Succeeded);
        Assert.Equal("agent.proposal.invalid_state", approval.Error?.Code);
    }

    [Fact]
    public async Task Proposal_execution_is_reserved_atomically()
    {
        var executor = new CountingExecutor();
        var audit = new InMemoryAgentAuditStore();
        var proposals = new InMemoryAgentProposalService(executor, audit);
        var proposal = await proposals.AddAsync(CreateProposal(requiresApproval: false));

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => proposals.ExecuteAsync(proposal.Id, "reviewer", "rev-1")));

        Assert.Single(results, x => x.Succeeded);
        Assert.Equal(1, executor.Count);
        Assert.All(results.Where(x => !x.Succeeded), x => Assert.Equal("agent.proposal.closed", x.Error?.Code));
    }

    [Fact]
    public async Task Proposal_execution_exception_marks_failed_and_emits_audit()
    {
        var audit = new InMemoryAgentAuditStore();
        var proposals = new InMemoryAgentProposalService(new ThrowingExecutor(), audit);
        var proposal = await proposals.AddAsync(CreateProposal(requiresApproval: false));

        var result = await proposals.ExecuteAsync(proposal.Id, "reviewer", "rev-1");
        var stored = await proposals.FindAsync(proposal.Id);
        var events = await audit.ListAsync("session-1");

        Assert.False(result.Succeeded);
        Assert.Equal("agent.proposal.execution_exception", result.Error?.Code);
        Assert.Equal(AgentActionProposalStatus.Failed, stored?.Status);
        Assert.Contains(events, x => x.Kind == AgentAuditEventKind.ProposalFailed && x.ActorId == "reviewer");
    }

    [Fact]
    public async Task Proposal_execution_exception_surfaces_message_and_is_logged()
    {
        var audit = new InMemoryAgentAuditStore();
        var logger = new RecordingLogger();
        var proposals = new InMemoryAgentProposalService(new ThrowingExecutor(), audit, logger);
        var proposal = await proposals.AddAsync(CreateProposal(requiresApproval: false));

        var result = await proposals.ExecuteAsync(proposal.Id, "reviewer", "rev-1");

        Assert.False(result.Succeeded);
        Assert.Equal("agent.proposal.execution_exception", result.Error?.Code);
        Assert.Contains("Simulated executor failure.", result.Error?.Message);
        var logged = Assert.Single(logger.Entries, e => e.Exception is not null);
        Assert.IsType<InvalidOperationException>(logged.Exception);
        Assert.Equal(LogLevel.Error, logged.Level);
    }

    [Fact]
    public async Task Proposal_execution_propagates_cancellation_and_still_marks_failed()
    {
        var audit = new InMemoryAgentAuditStore();
        var proposals = new InMemoryAgentProposalService(new CancellingExecutor(), audit);
        var proposal = await proposals.AddAsync(CreateProposal(requiresApproval: false));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => proposals.ExecuteAsync(proposal.Id, "reviewer", "rev-1"));

        var stored = await proposals.FindAsync(proposal.Id);
        var events = await audit.ListAsync("session-1");
        Assert.Equal(AgentActionProposalStatus.Failed, stored?.Status);
        Assert.Contains(events, x => x.Kind == AgentAuditEventKind.ProposalFailed && x.ActorId == "reviewer");
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

    private sealed class CountingExecutor : IAgentActionProposalExecutor
    {
        private int _count;

        public int Count => _count;

        public async Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            await Task.Delay(25, cancellationToken);
            return AgentResult<AgentProposalExecutionResult>.Success(new(proposal.Id, true, proposal.ResourceType!, proposal.ResourceId!, "Executed once."));
        }
    }

    private sealed class ThrowingExecutor : IAgentActionProposalExecutor
    {
        public Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated executor failure.");
    }

    private sealed class CancellingExecutor : IAgentActionProposalExecutor
    {
        public Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException("Simulated cancellation.");
    }

    private sealed class RecordingLogger : ILogger<InMemoryAgentProposalService>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception, formatter(state, exception)));
    }
}
