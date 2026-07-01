using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Composition.Design.Reconciliation;

/// <summary>
/// Provider-neutral projection of a workflow definition version marked usable-as-activity, carrying
/// exactly what a catalog row needs. <see cref="Inputs"/>/<see cref="Outputs"/> reuse the activity design
/// I/O shapes, so <see cref="WorkflowActivityReconciliationSource"/> mirrors them with no further mapping.
/// Contains no Workflows Design types, so the port surface stays free of that dependency.
/// </summary>
public sealed record UsableAsActivityWorkflow(
    string DefinitionId,
    string VersionId,
    string Version,
    string Name,
    string? Description,
    string? Category,
    IEnumerable<InputDefinition> Inputs,
    IEnumerable<OutputDefinition> Outputs);
