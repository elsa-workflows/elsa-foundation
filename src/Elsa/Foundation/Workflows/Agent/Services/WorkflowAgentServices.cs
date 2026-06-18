using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Workflows.Agent.Contracts;
using Elsa.Foundation.Workflows.Agent.Models;

namespace Elsa.Foundation.Workflows.Agent.Services;

public sealed class WorkflowAgentCapabilityProvider : IAgentCapabilityProvider
{
    public ValueTask<IReadOnlyCollection<AgentCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyCollection<AgentCapability>>(
        [
            new(
                "workflow.explain",
                "Elsa.Studio.Workflows",
                "Explain workflow",
                "Explains the selected workflow definition using minimized workflow context.",
                AgentCapabilityKind.Answer,
                AgentRisk.ReadOnly,
                ["/workflows"],
                [],
                ["workflow.definition"]
            ),
            new(
                "workflow.troubleshoot",
                "Elsa.Studio.Workflows",
                "Troubleshoot active workflow",
                "Troubleshoots an active workflow execution using selected workflow/runtime context.",
                AgentCapabilityKind.Answer,
                AgentRisk.ReadOnly,
                ["/workflows"],
                [],
                ["workflow.definition", "workflow.instance", "workflow.execution"]
            ),
            new(
                "workflow.propose-change",
                "Elsa.Studio.Workflows",
                "Propose workflow change",
                "Creates a reviewable workflow-change proposal; execution remains gated by explicit approval.",
                AgentCapabilityKind.Proposal,
                AgentRisk.ReviewRequired,
                ["/workflows"],
                ["workflow.proposals"],
                ["workflow.definition"])
        ]);
}

public sealed class DefaultWorkflowAgentContextProvider(IWorkflowRevisionProvider revisionProvider) : IWorkflowAgentContextProvider, IAgentContextProvider
{
    public string ScopeKind => "workflow";

    public async ValueTask<IReadOnlyCollection<AgentContextAttachment>> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.Inputs.TryGetValue("workflowDefinitionId", out var workflowDefinitionId) || string.IsNullOrWhiteSpace(workflowDefinitionId))
            throw new ArgumentException("Workflow context collection requires a 'workflowDefinitionId' input.", nameof(request));

        request.Inputs.TryGetValue("workflowVersionId", out var workflowVersionId);
        var context = await GetContextAsync(new(request.SessionId, workflowDefinitionId, workflowVersionId), cancellationToken);

        return
        [
            new(
                $"workflow:{context.WorkflowDefinitionId}",
                "workflow.definition",
                context.WorkflowDefinitionId,
                context.Summary,
                "workflow.definition",
                AgentContextSensitivity.Internal,
                "selection",
                $"Workflow definition '{context.WorkflowDefinitionId}' at revision '{context.Revision}' with {context.Activities.Count} activity summaries. Redactions: {string.Join("; ", context.Redactions)}",
                new
                {
                    workflowId = context.WorkflowDefinitionId,
                    version = context.WorkflowVersionId ?? "draft",
                    summary = context.Summary,
                    activities = context.Activities,
                    connections = Array.Empty<object>(),
                    diagnostics = context.Diagnostics
                },
                new Dictionary<string, string>
                {
                    ["workflowDefinitionId"] = context.WorkflowDefinitionId,
                    ["workflowVersionId"] = context.WorkflowVersionId ?? string.Empty,
                    ["revision"] = context.Revision
                })
        ];
    }

    public async Task<WorkflowAgentContext> GetContextAsync(WorkflowAgentContextRequest request, CancellationToken cancellationToken = default)
    {
        var revision = await revisionProvider.GetCurrentRevisionAsync(request.WorkflowDefinitionId, cancellationToken);
        return new(
            request.WorkflowDefinitionId,
            request.WorkflowVersionId,
            revision,
            $"Workflow {request.WorkflowDefinitionId}",
            [],
            [],
            ["Secrets, credentials, provider tokens, and full execution payloads are excluded from the MVP workflow context."]);
    }
}

public sealed class DefaultWorkflowRevisionProvider : IWorkflowRevisionProvider
{
    public Task<string> GetCurrentRevisionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
        => Task.FromResult("unknown");
}

public sealed class DenyAllWorkflowChangePermissionEvaluator : IWorkflowChangePermissionEvaluator
{
    public Task<bool> CanProposeChangeAsync(string actorId, string workflowDefinitionId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}

public sealed class DefaultWorkflowChangeProposalService(
    IWorkflowRevisionProvider revisionProvider,
    IWorkflowChangePermissionEvaluator permissions,
    IAgentProposalService proposals) : IWorkflowChangeProposalService
{
    public async Task<AgentResult<AgentActionProposal>> ProposeAsync(WorkflowChangeProposalRequest request, CancellationToken cancellationToken = default)
    {
        var currentRevision = await revisionProvider.GetCurrentRevisionAsync(request.WorkflowDefinitionId, cancellationToken);
        if (!string.Equals(currentRevision, "unknown", StringComparison.OrdinalIgnoreCase) && !string.Equals(currentRevision, request.BaseRevision, StringComparison.Ordinal))
            return AgentResult<AgentActionProposal>.Failure("agent.workflow.revision_conflict", $"Workflow '{request.WorkflowDefinitionId}' is at revision '{currentRevision}', not requested base revision '{request.BaseRevision}'.", 409);

        if (!await permissions.CanProposeChangeAsync(request.ActorId, request.WorkflowDefinitionId, cancellationToken))
            return AgentResult<AgentActionProposal>.Failure("agent.workflow.permission_denied", $"Actor '{request.ActorId}' is not allowed to propose workflow changes for '{request.WorkflowDefinitionId}'.", 403);

        var now = DateTimeOffset.UtcNow;
        var proposal = new AgentActionProposal(
            Guid.NewGuid().ToString("N"),
            request.SessionId,
            null,
            "workflow.propose-change",
            "workflow-change",
            request.Title,
            request.Summary,
            AgentRisk.ReviewRequired,
            request.BaseRevision,
            request.Operations.Select(x => new AgentActionChange("workflow", x.TryGetValue("op", out var op) ? Convert.ToString(op) ?? "operation" : "operation", request.Summary)).ToList(),
            request.Operations,
            request.Risks,
            request.Rollback,
            ["workflow.proposals"],
            "workflow-definition",
            request.WorkflowDefinitionId,
            RequiresApproval: true,
            AgentActionProposalStatus.AwaitingApproval,
            null,
            null,
            now,
            now);

        return AgentResult<AgentActionProposal>.Success(await proposals.AddAsync(proposal, cancellationToken));
    }
}
