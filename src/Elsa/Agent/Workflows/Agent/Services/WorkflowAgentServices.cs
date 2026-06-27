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

        if (ShouldReturnClarification(message.Content))
        {
            var clarification = CreateClarification(message);
            yield return new AgentStreamEvent(
                messageId,
                AgentStreamEventKind.ClarificationRequested,
                clarification.Question,
                null,
                null,
                DateTimeOffset.UtcNow,
                AgentResultKind.Clarification,
                clarification);
            yield return new AgentStreamEvent(messageId, AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
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

    private static WorkflowClarificationResult CreateClarification(AgentProviderMessage message)
    {
        var workflowContext = message.Context.FirstOrDefault(x => string.Equals(x.ContentType, "workflow.definition", StringComparison.OrdinalIgnoreCase));
        var workflowDefinitionId = GetReference(workflowContext, "workflowDefinitionId") ?? "workflow-draft";

        return new(
            "clarify-workflow-target",
            "Which workflow branch should Weaver update?",
            [
                new("active-draft", "Active draft", "active-draft", "Apply the answer to the workflow currently open in the designer."),
                new("selected-activity", "Selected activity", "selected-activity", "Scope the answer to the selected activity and nearby connections."),
                new("new-workflow", "New workflow", "new-workflow", "Create a separate workflow graph instead of changing the current draft.")
            ],
            $"{message.SessionId}:clarification:workflow-target",
            new Dictionary<string, string>
            {
                ["workflowDefinitionId"] = workflowDefinitionId,
                ["surface"] = "designer"
            });
    }

    private static string? GetReference(AgentContextAttachment? attachment, string key)
        => attachment?.References.TryGetValue(key, out var value) == true && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static bool ShouldReturnClarification(string content)
        => Contains(content, "clarify")
            || Contains(content, "ambiguous")
            || Contains(content, "which branch")
            || Contains(content, "ask a question");

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

public sealed class DefaultWorkflowActivityCatalogProvider : IWorkflowActivityCatalogProvider
{
    public Task<IReadOnlyCollection<WorkflowAgentActivityCatalogItem>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<WorkflowAgentActivityCatalogItem>>(
        [
            new("Elsa.Workflows.WriteLine", "Write line", true, ["write", "line", "console", "log", "text"]),
            new("Elsa.Workflows.Sequence", "Sequence", true, ["sequence", "flow", "container", "multiple"]),
            new("Elsa.Email.SendEmail", "Send email", true, ["email", "mail", "send", "notification"]),
            new("Elsa.Http.SendHttpRequest", "Send HTTP request", true, ["http", "webhook", "request", "api"]),
            new("Elsa.Timers.Delay", "Delay", true, ["delay", "timer", "wait", "schedule"]),
            new("Elsa.Secrets.LegacySecretActivity", "Legacy secret activity", false, ["secret", "credential", "unavailable"])
        ]);
}

public sealed class DefaultWorkflowAgentContextProvider(
    IWorkflowRevisionProvider revisionProvider,
    IWorkflowActivityCatalogProvider activityCatalogProvider) : IWorkflowAgentContextProvider, IAgentContextProvider
{
    private const int MaxActivityCatalogItems = 5;

    public string ScopeKind => "workflow";

    public async ValueTask<IReadOnlyCollection<AgentContextAttachment>> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.Inputs.TryGetValue("workflowDefinitionId", out var workflowDefinitionId) || string.IsNullOrWhiteSpace(workflowDefinitionId))
            throw new ArgumentException("Workflow context collection requires a 'workflowDefinitionId' input.", nameof(request));

        request.Inputs.TryGetValue("workflowVersionId", out var workflowVersionId);
        request.Inputs.TryGetValue("prompt", out var prompt);
        request.Inputs.TryGetValue("selectedNodeId", out var selectedNodeId);
        request.Inputs.TryGetValue("selectedActivityType", out var selectedActivityType);
        var context = await GetContextAsync(new(request.SessionId, workflowDefinitionId, workflowVersionId, prompt, selectedNodeId, selectedActivityType), cancellationToken);

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
                $"Workflow definition '{context.WorkflowDefinitionId}' at revision '{context.Revision}' with {context.Activities.Count} activity summaries, {context.ActivityCatalog.Count} catalog items, and selection hint '{context.Selection.NodeId ?? "none"}'. Redactions: {string.Join("; ", context.Redactions)}",
                new
                {
                    workflowId = context.WorkflowDefinitionId,
                    version = context.WorkflowVersionId ?? "draft",
                    revision = context.Revision,
                    summary = context.Summary,
                    activities = context.Activities,
                    connections = Array.Empty<object>(),
                    selection = context.Selection,
                    diagnostics = context.Diagnostics,
                    designerConstraints = context.DesignerConstraints,
                    permissions = context.Permissions,
                    activityCatalog = context.ActivityCatalog
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
        var activities = Array.Empty<WorkflowAgentActivitySummary>();
        var activityCatalog = await SelectActivityCatalogAsync(request, activities, cancellationToken);
        var diagnostics = CreateDiagnostics(request, activityCatalog);

        return new(
            request.WorkflowDefinitionId,
            request.WorkflowVersionId,
            revision,
            $"Draft workflow {request.WorkflowDefinitionId}",
            activities,
            diagnostics,
            [
                "Secrets, credentials, and provider tokens are excluded from workflow authoring context.",
                "Runtime payloads, incidents, unrelated workflow versions, and arbitrary workspace files are excluded.",
                "Unavailable activities are excluded from Activity Catalog subsets."
            ],
            new WorkflowAgentSelectionHint(request.SelectedNodeId, request.SelectedActivityType, "studio-hint"),
            new WorkflowAgentDesignerConstraints(MaxActivityCatalogItems, Enum.GetValues<WorkflowGraphOperationKind>()),
            new WorkflowAgentPermissionSummary(
                CanDirectApply: false,
                CanProposeChange: true,
                ["workflow.explain", "workflow.troubleshoot", "workflow.propose-change"]),
            activityCatalog);
    }

    private async Task<IReadOnlyCollection<WorkflowAgentActivityCatalogItem>> SelectActivityCatalogAsync(
        WorkflowAgentContextRequest request,
        IReadOnlyCollection<WorkflowAgentActivitySummary> activities,
        CancellationToken cancellationToken)
    {
        var availableItems = (await activityCatalogProvider.ListAsync(cancellationToken))
            .Where(x => x.IsAvailable)
            .ToList();
        var selectedAvailableItem = availableItems.FirstOrDefault(x => string.Equals(x.Type, request.SelectedActivityType, StringComparison.OrdinalIgnoreCase));
        if (selectedAvailableItem is not null)
            return [selectedAvailableItem];

        var tokens = Tokenize($"{request.Prompt} {request.SelectedActivityType} {string.Join(' ', activities.Select(x => x.Type))}");

        if (tokens.Count == 0)
            return availableItems.Take(MaxActivityCatalogItems).ToList();

        var selected = availableItems
            .Select(x => new { Item = x, Score = Score(x, tokens, request.SelectedActivityType) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxActivityCatalogItems)
            .Select(x => x.Item)
            .ToList();

        return selected;
    }

    private static IReadOnlyCollection<WorkflowAgentDiagnosticSummary> CreateDiagnostics(WorkflowAgentContextRequest request, IReadOnlyCollection<WorkflowAgentActivityCatalogItem> activityCatalog)
    {
        if (!string.IsNullOrWhiteSpace(request.Prompt) && activityCatalog.Count == 0)
            return [new("Warning", "No available Activity Catalog items matched the prompt or selected draft context.")];

        return [];
    }

    private static int Score(WorkflowAgentActivityCatalogItem item, IReadOnlyCollection<string> tokens, string? selectedActivityType)
    {
        var score = string.Equals(item.Type, selectedActivityType, StringComparison.OrdinalIgnoreCase) ? 100 : 0;
        var haystack = Tokenize($"{item.Type} {item.DisplayName} {string.Join(' ', item.Keywords)}");

        foreach (var token in tokens)
        {
            if (haystack.Contains(token))
                score += 10;
        }

        return score;
    }

    private static HashSet<string> Tokenize(string value)
        => value
            .Split([' ', '.', '-', '_', ':', '/', '\\', ',', ';', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .Where(x => x.Length > 2 && !IsStopWord(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsStopWord(string value)
        => value is "elsa" or "workflow" or "workflows" or "activity" or "activities" or "draft" or "node" or "use" or "the";
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

public sealed class DefaultWorkflowGraphOperationBatchRiskClassifier : IWorkflowGraphOperationBatchRiskClassifier
{
    private const int MaxDirectApplyOperations = 12;

    public WorkflowGraphOperationBatchRiskClassification Classify(WorkflowGraphOperationBatchRiskClassificationRequest request)
    {
        var reasons = new HashSet<WorkflowGraphOperationBatchRiskReason>();
        var batch = request.Batch;
        var context = request.Context;

        if (!context.Permissions.CanDirectApply)
            reasons.Add(WorkflowGraphOperationBatchRiskReason.PermissionDenied);

        if (!string.Equals(batch.SchemaVersion, WorkflowGraphOperationBatchSchema.CurrentVersion, StringComparison.Ordinal))
            reasons.Add(WorkflowGraphOperationBatchRiskReason.InvalidBatch);

        if (!string.Equals(batch.WorkflowDefinitionId, context.WorkflowDefinitionId, StringComparison.Ordinal))
            reasons.Add(WorkflowGraphOperationBatchRiskReason.InvalidBatch);

        if (string.IsNullOrWhiteSpace(batch.BaseRevision) || !string.Equals(batch.BaseRevision, context.Revision, StringComparison.Ordinal))
            reasons.Add(WorkflowGraphOperationBatchRiskReason.StaleRevision);

        if (batch.Operations.Count == 0)
            reasons.Add(WorkflowGraphOperationBatchRiskReason.InvalidBatch);

        if (batch.Operations.Count > MaxDirectApplyOperations)
            reasons.Add(WorkflowGraphOperationBatchRiskReason.HighComplexity);

        var knownActivityReferences = context.Activities
            .Select(x => x.Id)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableActivityTypes = context.ActivityCatalog
            .Where(x => x.IsAvailable)
            .Select(x => x.Type)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var operation in batch.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.Id))
                reasons.Add(WorkflowGraphOperationBatchRiskReason.InvalidBatch);

            if (IsDestructive(operation.Kind))
                reasons.Add(WorkflowGraphOperationBatchRiskReason.DestructiveOperation);

            if (!IsSupportedForDirectApply(operation.Kind))
                reasons.Add(WorkflowGraphOperationBatchRiskReason.Uncertain);

            if (!HasRequiredParameters(operation, knownActivityReferences, availableActivityTypes, reasons))
                reasons.Add(WorkflowGraphOperationBatchRiskReason.InvalidBatch);
        }

        if (reasons.Count == 0)
            return new(
                WorkflowGraphOperationBatchRiskDecision.DirectApply,
                AgentRisk.ReadOnly,
                AgentResultKind.WorkflowGraphOperationBatch,
                [WorkflowGraphOperationBatchRiskReason.LowRisk],
                "Batch is low risk and may be applied directly to the active designer working state.");

        var decision = reasons.Contains(WorkflowGraphOperationBatchRiskReason.UnavailableActivity)
            ? WorkflowGraphOperationBatchRiskDecision.Clarification
            : WorkflowGraphOperationBatchRiskDecision.Proposal;

        return new(
            decision,
            AgentRisk.ReviewRequired,
            decision == WorkflowGraphOperationBatchRiskDecision.Clarification ? AgentResultKind.Clarification : AgentResultKind.Proposal,
            reasons.ToList(),
            decision == WorkflowGraphOperationBatchRiskDecision.Clarification
                ? "Batch cannot be applied directly until missing activity capabilities are clarified."
                : "Batch failed closed and must become a proposal instead of direct apply.");
    }

    private static bool HasRequiredParameters(
        WorkflowGraphOperation operation,
        ISet<string> knownActivityReferences,
        ISet<string> availableActivityTypes,
        ISet<WorkflowGraphOperationBatchRiskReason> reasons)
    {
        var parameters = operation.Parameters;
        switch (operation.Kind)
        {
            case WorkflowGraphOperationKind.AddActivity:
            {
                var activityId = GetString(parameters, "activityId") ?? operation.TemporaryReferences.FirstOrDefault();
                var activityType = GetString(parameters, "activityType") ?? GetString(parameters, "activityVersionId");
                if (string.IsNullOrWhiteSpace(activityId) || string.IsNullOrWhiteSpace(activityType))
                    return false;

                if (!availableActivityTypes.Contains(activityType))
                    reasons.Add(WorkflowGraphOperationBatchRiskReason.UnavailableActivity);

                knownActivityReferences.Add(activityId);
                foreach (var temporaryReference in operation.TemporaryReferences)
                    knownActivityReferences.Add(temporaryReference);
                return true;
            }

            case WorkflowGraphOperationKind.SetRoot:
            case WorkflowGraphOperationKind.SetDesignerPosition:
            case WorkflowGraphOperationKind.SetActivityProperty:
                return IsKnownActivityReference(GetString(parameters, "activityId"), knownActivityReferences);

            case WorkflowGraphOperationKind.ConnectActivities:
                return IsKnownActivityReference(GetString(parameters, "sourceActivityId"), knownActivityReferences)
                    && IsKnownActivityReference(GetString(parameters, "targetActivityId"), knownActivityReferences);

            case WorkflowGraphOperationKind.UpdateActivity:
                return IsKnownActivityReference(GetString(parameters, "activityId"), knownActivityReferences)
                    && parameters.TryGetValue("patch", out var patch)
                    && patch is not null;

            default:
                return true;
        }
    }

    private static bool IsKnownActivityReference(string? value, ISet<string> knownActivityReferences)
        => !string.IsNullOrWhiteSpace(value) && knownActivityReferences.Contains(value);

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key)
        => parameters.TryGetValue(key, out var value) ? Convert.ToString(value) : null;

    private static bool IsSupportedForDirectApply(WorkflowGraphOperationKind kind)
        => kind is WorkflowGraphOperationKind.AddActivity
            or WorkflowGraphOperationKind.UpdateActivity
            or WorkflowGraphOperationKind.ConnectActivities
            or WorkflowGraphOperationKind.SetRoot
            or WorkflowGraphOperationKind.SetDesignerPosition
            or WorkflowGraphOperationKind.SetActivityProperty;

    private static bool IsDestructive(WorkflowGraphOperationKind kind)
        => kind is WorkflowGraphOperationKind.RemoveActivity or WorkflowGraphOperationKind.DisconnectActivities;
}

public sealed class DefaultWorkflowAuthoringAuditService(IAgentAuditSink auditSink) : IWorkflowAuthoringAuditService
{
    public Task EmitAsync(WorkflowAuthoringAuditRequest request, CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["workflowDefinitionId"] = request.WorkflowDefinitionId,
            ["capabilityId"] = request.CapabilityId,
            ["providerId"] = request.ProviderId,
            ["operationSummary"] = request.OperationSummary,
            ["outcome"] = request.Outcome,
            ["resultKind"] = request.ResultKind.ToString(),
            ["modelId"] = request.ModelId ?? string.Empty,
            ["runId"] = request.RunId ?? string.Empty
        };

        return auditSink.EmitAsync(new(
            Guid.NewGuid().ToString("N"),
            AgentAuditEventKind.WorkflowAuthoringInteraction,
            request.SessionId,
            request.ActorId,
            $"Weaver workflow authoring interaction {request.Outcome}.",
            DateTimeOffset.UtcNow,
            metadata), cancellationToken);
    }
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
