using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Extensions;
using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Workflows.Agent.Contracts;
using Elsa.Foundation.Workflows.Agent.Extensions;
using Elsa.Foundation.Workflows.Agent.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Agent.Tests;

public sealed class WorkflowAgentTests
{
    [Fact]
    public async Task Workflow_context_provider_returns_minimized_workflow_attachment_shape()
    {
        using var provider = BuildWorkflowProvider("rev-1", allowChanges: true);
        var contextProvider = provider.GetServices<IAgentContextProvider>().Single(x => x.ScopeKind == "workflow");

        var attachments = await contextProvider.CollectAsync(new(
            "session-1",
            "workflow",
            new Dictionary<string, string>
            {
                ["workflowDefinitionId"] = "wf-1",
                ["workflowVersionId"] = "v1"
            }));

        var attachment = Assert.Single(attachments);
        Assert.Equal("workflow.definition", attachment.Source);
        Assert.Equal("workflow.definition", attachment.ContentType);
        Assert.Equal(AgentContextSensitivity.Internal, attachment.Sensitivity);
        Assert.Equal("wf-1", attachment.References["workflowDefinitionId"]);
        Assert.Equal("rev-1", attachment.References["revision"]);
        Assert.Contains("Redactions:", attachment.Summary);
        Assert.NotNull(attachment.Content);
    }

    [Fact]
    public async Task Workflow_change_proposal_rejects_stale_base_revision()
    {
        using var provider = BuildWorkflowProvider("rev-2", allowChanges: true);
        var service = provider.GetRequiredService<IWorkflowChangeProposalService>();

        var result = await service.ProposeAsync(CreateRequest(baseRevision: "rev-1"));

        Assert.False(result.Succeeded);
        Assert.Equal("agent.workflow.revision_conflict", result.Error?.Code);
    }

    [Fact]
    public async Task Workflow_change_proposal_requires_permission()
    {
        using var provider = BuildWorkflowProvider("rev-1", allowChanges: false);
        var service = provider.GetRequiredService<IWorkflowChangeProposalService>();

        var result = await service.ProposeAsync(CreateRequest(baseRevision: "rev-1"));

        Assert.False(result.Succeeded);
        Assert.Equal("agent.workflow.permission_denied", result.Error?.Code);
    }

    [Fact]
    public async Task Workflow_change_proposal_is_reviewable_and_requires_approval()
    {
        using var provider = BuildWorkflowProvider("rev-1", allowChanges: true);
        var service = provider.GetRequiredService<IWorkflowChangeProposalService>();

        var result = await service.ProposeAsync(CreateRequest(baseRevision: "rev-1"));

        Assert.True(result.Succeeded);
        Assert.True(result.Value?.RequiresApproval);
        Assert.Equal(AgentActionProposalStatus.AwaitingApproval, result.Value?.Status);
        Assert.Equal("workflow-change", result.Value?.Kind);
        Assert.Equal("workflow.propose-change", result.Value?.CapabilityId);
        Assert.Equal("workflow-definition", result.Value?.ResourceType);
        Assert.Equal("wf-1", result.Value?.ResourceId);
    }

    private static ServiceProvider BuildWorkflowProvider(string revision, bool allowChanges)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowRevisionProvider>(new FixedWorkflowRevisionProvider(revision));
        services.AddSingleton<IWorkflowChangePermissionEvaluator>(new FixedWorkflowChangePermissionEvaluator(allowChanges));
        services.AddFoundationAgentAbstractions();
        services.AddFoundationWorkflowsAgent();
        return services.BuildServiceProvider();
    }

    private static WorkflowChangeProposalRequest CreateRequest(string baseRevision)
        => new(
            "session-1",
            "actor-1",
            "wf-1",
            baseRevision,
            "Update workflow",
            "Reviewable workflow update.",
            [new Dictionary<string, object?> { ["op"] = "replace-input", ["path"] = "workflow:wf-1" }],
            ["Changes draft workflow behavior."],
            "Restore the previous draft revision.");

    private sealed class FixedWorkflowRevisionProvider(string revision) : IWorkflowRevisionProvider
    {
        public Task<string> GetCurrentRevisionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => Task.FromResult(revision);
    }

    private sealed class FixedWorkflowChangePermissionEvaluator(bool allowed) : IWorkflowChangePermissionEvaluator
    {
        public Task<bool> CanProposeChangeAsync(string actorId, string workflowDefinitionId, CancellationToken cancellationToken = default) => Task.FromResult(allowed);
    }
}
