namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record WorkflowActivationSource(string Kind, string? SourceId = null)
{
    public const string PublishingKind = "publishing";
    public const string ArtifactReconciliationKind = "artifact-reconciliation";

    public static WorkflowActivationSource Publishing { get; } = new(PublishingKind);

    public static WorkflowActivationSource ArtifactReconciliation(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return new(ArtifactReconciliationKind, sourceId);
    }

    public bool IsSameOwnerAs(WorkflowActivationSource other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return StringComparer.Ordinal.Equals(Kind, other.Kind) && StringComparer.Ordinal.Equals(SourceId, other.SourceId);
    }

    public string Describe() => SourceId is null ? Kind : $"{Kind}:{SourceId}";
}

public sealed record WorkflowActivationSlot(
    string SlotId,
    string WorkflowDefinitionId,
    string SlotName,
    string? ActiveActivationId,
    WorkflowActivationSource? Source,
    long Revision,
    DateTimeOffset UpdatedAt);

public static class WorkflowActivationSlotIdentity
{
    public static string Create(string workflowDefinitionId, string slotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        return $"activation-slot:{workflowDefinitionId.Length}:{workflowDefinitionId}:{slotName.Length}:{slotName}";
    }
}

public enum WorkflowActivationOwnershipIntent
{
    RespectExistingOwner,
    TakeOver
}

public enum WorkflowActivationConflict
{
    None,
    RevisionMismatch,
    ForeignSource
}

public sealed record WorkflowActivationTransition(
    bool Succeeded,
    WorkflowActivationSlot Slot,
    string? ReplacedActivationId = null,
    WorkflowActivationConflict Conflict = WorkflowActivationConflict.None,
    string? Diagnostic = null,
    WorkflowActivationSource? ReplacedSource = null);

public sealed record WorkflowActivationSlotRequest(
    string WorkflowDefinitionId,
    string SlotName,
    string ActivationId,
    WorkflowActivationSource Source,
    long ExpectedRevision,
    DateTimeOffset UpdatedAt,
    WorkflowActivationOwnershipIntent OwnershipIntent = WorkflowActivationOwnershipIntent.RespectExistingOwner);
