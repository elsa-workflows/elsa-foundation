using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Agent.Workflows.Contracts;
using Elsa.Agent.Workflows.Models;

namespace Elsa.Agent.Workflows.Services;

public sealed class WorkflowAgentCapabilityProvider : IAgentCapabilityProvider
{
    public ValueTask<IReadOnlyCollection<AgentCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyCollection<AgentCapability>>([
            new("workflow.explain", WorkflowAgentSnapshot.ModuleId, "Explain open workflow", "Explains the admitted workflow working-state graph.",
                AgentCapabilityKind.Answer, AgentRisk.ReadOnly, ["/workflows"], ["workflows/design:read"], ["workflow.definition", "workflow.request"]) { Modes = ["explain"] },
            new("workflow.propose-change", WorkflowAgentSnapshot.ModuleId, "Propose workflow layout change", "Reviews the position of an existing activity in the open workflow. Saving and publishing remain explicit.",
                AgentCapabilityKind.Proposal, AgentRisk.ReviewRequired, ["/workflows"], ["workflows/design:manage"], ["workflow.definition"]) { Modes = ["build"] }
        ]);
}

public sealed class DefaultWorkflowActivityCatalogProvider : IWorkflowActivityCatalogProvider
{
    public Task<IReadOnlyCollection<WorkflowAgentActivityCatalogItem>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<WorkflowAgentActivityCatalogItem>>([]);
}

public sealed class DefaultWorkflowAgentContextProvider(
    IWorkflowRevisionProvider revisionProvider,
    IWorkflowActivityCatalogProvider activityCatalogProvider) : IWorkflowAgentContextProvider, IAgentContextProvider
{
    public AgentContextProviderDescriptor Descriptor { get; } = new(
        "workflows.context", WorkflowAgentSnapshot.ModuleId, "workflow",
        ["workflow.definition", "workflow.request", "workflow.guidance"], ["1"], ["workflow-definition"], Required: false);

    public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
    {
        var attachments = new List<AgentContextAttachment>();
        var diagnostics = new List<AgentContributionDiagnostic>();
        foreach (var caller in request.CallerAttachments.Where(item =>
                     item.ModuleId == WorkflowAgentSnapshot.ModuleId || item.ContentType?.StartsWith("workflow.", StringComparison.Ordinal) == true))
        {
            if (caller.ContentType == "workflow.request" && caller.SchemaVersion == "1")
            {
                if (TryAdmitRequest(caller, out var admitted)) attachments.Add(admitted!);
                else diagnostics.Add(new(Descriptor.Id, "agent.workflow.request_invalid", "The workflow request must contain bounded request-kind and reference metadata.", true));
                continue;
            }
            if (caller.ContentType != "workflow.definition" || caller.SchemaVersion != "1")
            {
                diagnostics.Add(new(Descriptor.Id, "agent.workflow.context_unsupported", "This workflow context kind or schema is not supported by the current module contribution.", true));
                continue;
            }
            if (!WorkflowAgentSnapshot.TryRead(caller, out var snapshot, out var error))
            {
                diagnostics.Add(new(Descriptor.Id, "agent.workflow.context_invalid", error, true));
                continue;
            }
            if (request.Client is not null && request.Client != snapshot!.Client || request.Target is { } requested && !WorkflowAgentSnapshot.SameTarget(requested, snapshot!.Target))
            {
                diagnostics.Add(new(Descriptor.Id, "agent.workflow.context_target_mismatch", "The workflow snapshot does not match the requested target and revision.", true));
                continue;
            }
            attachments.Add(new($"workflow:{snapshot!.Target.ResourceId}:{snapshot.Target.Revision}", "workflow.definition",
                snapshot.Target.ResourceId, "Open workflow working state", "workflow.definition", AdmittedSensitivity(caller.Sensitivity),
                "selection", $"Workflow '{snapshot.Target.ResourceId}' at revision '{snapshot.Target.Revision}' contains {snapshot.Graph.Activities.Count} activity summaries.",
                snapshot.Content, new Dictionary<string, string> { ["workflowDefinitionId"] = snapshot.Target.ResourceId!, ["revision"] = snapshot.Target.Revision! })
            {
                ModuleId = WorkflowAgentSnapshot.ModuleId, SchemaVersion = "1", Target = snapshot.Target,
                Origin = AgentContextOrigin.ClientSnapshot, Client = snapshot.Client, AgentScopes = ["/workflows"]
            });
        }
        attachments.Add(new("workflows:installed-guidance", "workflows.module", null, "Workflow module guidance", "workflow.guidance",
            AgentContextSensitivity.Public, "module", "The workflow module can read the admitted working-state graph and propose a reviewed layout change for an existing activity. Activity creation, property edits, broad graph batches, save and publish are unavailable in this contribution. Apply changes only through the declared tool and report local working-state receipts separately from saved resources.", null,
            new Dictionary<string, string>())
        {
            ModuleId = WorkflowAgentSnapshot.ModuleId, SchemaVersion = "1", Origin = AgentContextOrigin.InstalledGuidance,
            AgentScopes = []
        });
        return ValueTask.FromResult(new AgentContextContribution(attachments, diagnostics));
    }

    // A caller classification can restrict module-owned data; it cannot certify prior redaction.
    private static AgentContextSensitivity AdmittedSensitivity(AgentContextSensitivity caller) =>
        caller == AgentContextSensitivity.SecretRedacted ? AgentContextSensitivity.Secret :
        caller < AgentContextSensitivity.Internal ? AgentContextSensitivity.Internal : caller;

    private static bool TryAdmitRequest(AgentContextAttachment caller, out AgentContextAttachment? admitted)
    {
        admitted = null;
        JsonElement content;
        try { content = caller.Content is JsonElement json ? json : JsonSerializer.SerializeToElement(caller.Content); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException) { return false; }
        var kind = WorkflowAgentSnapshot.Text(content, "requestKind");
        var reference = WorkflowAgentSnapshot.Text(content, "referenceId");
        if (caller.ModuleId != WorkflowAgentSnapshot.ModuleId || string.IsNullOrWhiteSpace(caller.Id) || caller.Id.Length > 256 || content.ValueKind != JsonValueKind.Object ||
            content.GetRawText().Length > 16384 || string.IsNullOrWhiteSpace(kind) || kind.Length > 80 || reference is null || reference.Length > 256)
            return false;
        static string? Field(JsonElement body, string name, int limit)
        {
            var value = WorkflowAgentSnapshot.Text(body, name);
            return value?[..Math.Min(value.Length, limit)];
        }
        var data = JsonSerializer.SerializeToElement(new
        {
            requestKind = kind, referenceId = reference, intent = Field(content, "intent", 2000), name = Field(content, "name", 256),
            description = Field(content, "description", 2000), rootActivityVersionId = Field(content, "rootActivityVersionId", 256)
        });
        admitted = new($"workflow:request:{caller.Id}", "workflow.request", reference, "Workflow request context", "workflow.request",
            AdmittedSensitivity(caller.Sensitivity), "request", "Module-admitted workflow request and reference metadata; this does not identify live working state.", data,
            new Dictionary<string, string>())
        { ModuleId = WorkflowAgentSnapshot.ModuleId, SchemaVersion = "1", Origin = AgentContextOrigin.ClientSnapshot, Target = null, Client = null, AgentScopes = [] };
        return true;
    }

    public async Task<WorkflowAgentContext> GetContextAsync(WorkflowAgentContextRequest request, CancellationToken cancellationToken = default)
    {
        var graph = request.Graph;
        var revision = graph?.Revision ?? await revisionProvider.GetCurrentRevisionAsync(request.WorkflowDefinitionId, cancellationToken);
        return new(request.WorkflowDefinitionId, request.WorkflowVersionId, revision, $"Draft workflow {request.WorkflowDefinitionId}",
            graph?.Activities ?? [], [], ["Activity property values, credentials and runtime payloads are excluded."],
            new(request.SelectedNodeId, request.SelectedActivityType, "client-snapshot"),
            new(0, [WorkflowGraphOperationKind.SetDesignerPosition]), new(false, false, ["workflow.explain"]),
            await activityCatalogProvider.ListAsync(cancellationToken));
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
                AgentResultKind.ModuleResult,
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
        => kind is WorkflowGraphOperationKind.SetDesignerPosition;

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
            AgentProviderPrimitives.NewId(),
            AgentAuditEventKind.ModuleInteraction,
            request.SessionId,
            request.ActorId,
            $"Weaver workflow authoring interaction {request.Outcome}.",
            DateTimeOffset.UtcNow,
            metadata), cancellationToken);
    }
}

public sealed class DefaultWorkflowChangeProposalService : IWorkflowChangeProposalService
{
    public Task<AgentResult<AgentActionProposal>> ProposeAsync(WorkflowChangeProposalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(AgentResult<AgentActionProposal>.Failure("agent.workflow.operation_unsupported",
            "Broad workflow graph changes are unavailable. Use the reviewed layout-change tool for an existing activity.", 409));
}
