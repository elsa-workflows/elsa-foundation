namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Requests activation of exact stored native or model material under its logical authored definition.
/// The coordinator verifies the material pin/incarnation; the source reference owns definition/version provenance.
/// </summary>
public sealed record WorkflowMaterialActivationCommand(
    string WorkflowDefinitionId,
    RuntimeExecutionMaterial Material,
    WorkflowExecutableSourceReference Reference,
    string SlotName,
    string ActivationId,
    WorkflowActivationSource Source,
    long ExpectedRevision,
    WorkflowActivationOwnershipIntent OwnershipIntent = WorkflowActivationOwnershipIntent.RespectExistingOwner);

/// <summary>Retracts a logical definition's slot without inventing a native root for model material.</summary>
public sealed record WorkflowMaterialDeactivationCommand(
    string WorkflowDefinitionId,
    RuntimeExecutionMaterial Material,
    string SlotName,
    WorkflowActivationSource Source,
    long ExpectedRevision);
