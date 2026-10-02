namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Deterministic identity of the source reference minted for an activation.</summary>
public static class WorkflowActivationReferenceIdentity
{
    public static string Create(string activationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        return $"activation-ref:{activationId}";
    }
}

/// <summary>How an activation or deactivation request was resolved.</summary>
public enum WorkflowActivationOutcome
{
    Activated,
    AlreadyActive,
    Conflict,
    Failed,
    Deactivated,
    AlreadyInactive
}

/// <summary>Which step of the activation lifecycle failed.</summary>
public enum WorkflowActivationStep
{
    None,
    SourceReferenceMint,
    ProjectionPreparation,
    SlotTransition,
    ProjectionActivation,
    TriggerObserverNotification
}

/// <summary>Where one activation's serving projection stands in a projection store.</summary>
public enum WorkflowActivationProjectionState
{
    /// <summary>The store holds no projection for the activation.</summary>
    Missing,

    /// <summary>The projection is prepared and has never served: its activation may still be on its way to the slot.</summary>
    Prepared,

    /// <summary>The projection serves.</summary>
    Active,

    /// <summary>The projection served and was switched off by the activation that replaced it.</summary>
    Replaced
}

/// <summary>Requests that an executable become live in a named activation slot.</summary>
public sealed record WorkflowActivationCommand(
    WorkflowExecutable Executable,
    WorkflowExecutableSourceReference Reference,
    string SlotName,
    string ActivationId,
    WorkflowActivationSource Source,
    long ExpectedRevision,
    WorkflowActivationOwnershipIntent OwnershipIntent = WorkflowActivationOwnershipIntent.RespectExistingOwner);

/// <summary>Requests that the activation currently serving a named slot be retracted.</summary>
public sealed record WorkflowDeactivationCommand(
    WorkflowExecutable Executable,
    string SlotName,
    WorkflowActivationSource Source,
    long ExpectedRevision);

/// <summary>Outcome of an activation or deactivation lifecycle request.</summary>
public sealed record WorkflowActivationResult(
    bool Succeeded,
    WorkflowActivationOutcome Outcome,
    WorkflowActivationSlot Slot,
    WorkflowExecutableSourceReference? Reference = null,
    string? ReplacedActivationId = null,
    WorkflowActivationConflict Conflict = WorkflowActivationConflict.None,
    string? Diagnostic = null,
    WorkflowActivationStep FailedStep = WorkflowActivationStep.None,
    string? CompensationDiagnostic = null);
