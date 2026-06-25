using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Contracts;
using Elsa.Agent.Workflows.Models;

namespace Elsa.Agent.Workflows.Services;

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

public sealed class DeterministicWorkflowAgentProvider : IAgentProvider
{
    public const string Id = "deterministic-workflow-authoring";

    public string ProviderId => Id;

    public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>
        {
            ["adapter"] = "deterministic",
            ["surface"] = "workflow-authoring",
            ["status"] = "available"
        }));

    public async IAsyncEnumerable<AgentStreamEvent> SendMessageAsync(AgentProviderMessage message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var messageId = Guid.NewGuid().ToString("N");
        yield return new AgentStreamEvent(messageId, AgentStreamEventKind.Started, null, null, null, DateTimeOffset.UtcNow);

        if (ShouldReturnError(message.Content))
        {
            yield return new AgentStreamEvent(
                messageId,
                AgentStreamEventKind.Error,
                null,
                null,
                new("agent.workflow.deterministic_error", "Deterministic workflow authoring provider returned a requested error.", 400),
                DateTimeOffset.UtcNow,
                AgentResultKind.Error);
            yield break;
        }

        if (ShouldReturnWorkflowBatch(message.Content))
        {
            yield return new AgentStreamEvent(
                messageId,
                AgentStreamEventKind.WorkflowGraphOperationBatchCreated,
                "Prepared one workflow graph operation batch.",
                null,
                null,
                DateTimeOffset.UtcNow,
                AgentResultKind.WorkflowGraphOperationBatch,
                CreateBatch(message));
            yield return new AgentStreamEvent(messageId, AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
            yield break;
        }

        yield return new AgentStreamEvent(
            messageId,
            AgentStreamEventKind.MessageDelta,
            "Deterministic workflow authoring provider response.",
            null,
            null,
            DateTimeOffset.UtcNow,
            AgentResultKind.Message);
        yield return new AgentStreamEvent(messageId, AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
    }

    public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentToolApprovalResult(request.Approved, request.Approved ? "Tool approval accepted by deterministic workflow provider." : "Tool approval denied by deterministic workflow provider."));

    public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentProviderDiagnostics(
            ProviderId,
            IsAvailable: true,
            Status: "Deterministic workflow authoring provider is available for Weaver contract validation.",
            AgentProviderKind.ProviderSdkBinding,
            [AgentProviderOperation.Chat, AgentProviderOperation.Streaming, AgentProviderOperation.ToolApproval],
            AgentProviderRiskProfile.ReviewRequired,
            new Dictionary<string, string>
            {
                ["adapter"] = "deterministic",
                ["surface"] = "workflow-authoring"
            }));

    private static WorkflowGraphOperationBatch CreateBatch(AgentProviderMessage message)
    {
        var workflowContext = message.Context.FirstOrDefault(x => string.Equals(x.ContentType, "workflow.definition", StringComparison.OrdinalIgnoreCase));
        var workflowDefinitionId = GetReference(workflowContext, "workflowDefinitionId") ?? "workflow-draft";
        var baseRevision = GetReference(workflowContext, "revision");
        const string temporaryActivityId = "temp:activity:send-email-1";

        return new(
            WorkflowGraphOperationBatchSchema.CurrentVersion,
            workflowDefinitionId,
            baseRevision,
            [
                new(
                    "op-add-send-email",
                    WorkflowGraphOperationKind.AddActivity,
                    new Dictionary<string, object?>
                    {
                        ["activityId"] = temporaryActivityId,
                        ["activityType"] = "Elsa.Email.SendEmail",
                        ["displayName"] = "Send email"
                    },
                    [temporaryActivityId],
                    "Add a send email activity."),
                new(
                    "op-set-root",
                    WorkflowGraphOperationKind.SetRoot,
                    new Dictionary<string, object?>
                    {
                        ["activityId"] = temporaryActivityId
                    },
                    [temporaryActivityId],
                    "Use the send email activity as the workflow root."),
                new(
                    "op-set-position",
                    WorkflowGraphOperationKind.SetDesignerPosition,
                    new Dictionary<string, object?>
                    {
                        ["activityId"] = temporaryActivityId,
                        ["x"] = 320,
                        ["y"] = 180
                    },
                    [temporaryActivityId],
                    "Place the activity on the designer canvas."),
                new(
                    "op-set-subject",
                    WorkflowGraphOperationKind.SetActivityProperty,
                    new Dictionary<string, object?>
                    {
                        ["activityId"] = temporaryActivityId,
                        ["propertyName"] = "Subject",
                        ["value"] = "Hello from Weaver"
                    },
                    [temporaryActivityId],
                    "Set the email subject.")
            ],
            new Dictionary<string, string>
            {
                ["surface"] = "designer",
                ["source"] = "deterministic-workflow-authoring"
            });
    }

    private static string? GetReference(AgentContextAttachment? attachment, string key)
        => attachment?.References.TryGetValue(key, out var value) == true && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static bool ShouldReturnWorkflowBatch(string content)
        => Contains(content, "workflow graph operation")
            || Contains(content, "direct apply")
            || Contains(content, "add activity")
            || Contains(content, "create workflow");

    private static bool ShouldReturnError(string content)
        => Contains(content, "force error") || Contains(content, "return error");

    private static bool Contains(string content, string value)
        => content.Contains(value, StringComparison.OrdinalIgnoreCase);
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
