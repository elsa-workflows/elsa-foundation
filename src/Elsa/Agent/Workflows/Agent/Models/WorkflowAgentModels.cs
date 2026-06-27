using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Workflows.Models;

public sealed record WorkflowAgentContextRequest(
    string SessionId,
    string WorkflowDefinitionId,
    string? WorkflowVersionId,
    string? Prompt = null,
    string? SelectedNodeId = null,
    string? SelectedActivityType = null);

public sealed record WorkflowAgentContext(
    string WorkflowDefinitionId,
    string? WorkflowVersionId,
    string Revision,
    string Summary,
    IReadOnlyCollection<WorkflowAgentActivitySummary> Activities,
    IReadOnlyCollection<WorkflowAgentDiagnosticSummary> Diagnostics,
    IReadOnlyCollection<string> Redactions,
    WorkflowAgentSelectionHint Selection,
    WorkflowAgentDesignerConstraints DesignerConstraints,
    WorkflowAgentPermissionSummary Permissions,
    IReadOnlyCollection<WorkflowAgentActivityCatalogItem> ActivityCatalog);

public sealed record WorkflowAgentActivitySummary(
    string Id,
    string Type,
    string DisplayName);

public sealed record WorkflowAgentSelectionHint(
    string? NodeId,
    string? ActivityType,
    string Source);

public sealed record WorkflowAgentDiagnosticSummary(
    string Severity,
    string Message);

public sealed record WorkflowAgentDesignerConstraints(
    int MaxActivityCatalogItems,
    IReadOnlyCollection<WorkflowGraphOperationKind> SupportedOperations);

public sealed record WorkflowAgentPermissionSummary(
    bool CanDirectApply,
    bool CanProposeChange,
    IReadOnlyCollection<string> Capabilities);

public sealed record WorkflowAgentActivityCatalogItem(
    string Type,
    string DisplayName,
    bool IsAvailable,
    IReadOnlyCollection<string> Keywords);

public static class WorkflowGraphOperationBatchSchema
{
    public const string CurrentVersion = "elsa.workflow-graph-operation-batch.v1";
}

public enum WorkflowGraphOperationKind
{
    AddActivity,
    UpdateActivity,
    RemoveActivity,
    ConnectActivities,
    DisconnectActivities,
    SetRoot,
    SetDesignerPosition,
    SetActivityProperty
}

public sealed record WorkflowGraphOperationBatch(
    string SchemaVersion,
    string WorkflowDefinitionId,
    string? BaseRevision,
    IReadOnlyCollection<WorkflowGraphOperation> Operations,
    IReadOnlyDictionary<string, string> Metadata);

public enum WorkflowGraphOperationBatchRiskDecision
{
    DirectApply,
    Clarification,
    Proposal
}

public enum WorkflowGraphOperationBatchRiskReason
{
    LowRisk,
    PermissionDenied,
    StaleRevision,
    InvalidBatch,
    DestructiveOperation,
    HighComplexity,
    UnavailableActivity,
    Uncertain
}

public sealed record WorkflowGraphOperationBatchRiskClassification(
    WorkflowGraphOperationBatchRiskDecision Decision,
    AgentRisk Risk,
    AgentResultKind ResultKind,
    IReadOnlyCollection<WorkflowGraphOperationBatchRiskReason> Reasons,
    string Summary)
{
    public bool CanDirectApply => Decision == WorkflowGraphOperationBatchRiskDecision.DirectApply;
}

public sealed record WorkflowGraphOperationBatchRiskClassificationRequest(
    WorkflowGraphOperationBatch Batch,
    WorkflowAgentContext Context);

public sealed record WorkflowGraphOperation(
    string Id,
    WorkflowGraphOperationKind Kind,
    IReadOnlyDictionary<string, object?> Parameters,
    IReadOnlyCollection<string> TemporaryReferences,
    string? Summary);

public sealed record WorkflowClarificationResult(
    string Id,
    string Question,
    IReadOnlyCollection<WorkflowClarificationOption> Options,
    string? ContinuationToken,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record WorkflowClarificationOption(
    string Id,
    string Label,
    string Value,
    string? Description);

public sealed record WorkflowChangeProposalRequest(
    string SessionId,
    string ActorId,
    string WorkflowDefinitionId,
    string BaseRevision,
    string Title,
    string Summary,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Operations,
    IReadOnlyCollection<string> Risks,
    string? Rollback);
