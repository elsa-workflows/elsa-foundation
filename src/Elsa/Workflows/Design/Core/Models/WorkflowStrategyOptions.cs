namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// Workflow strategy selections. Each strategy is identified by its stable type alias (the shared
/// <c>TypeAliasConvention</c>), never an assembly-qualified name.
/// </summary>
public sealed class WorkflowStrategyOptions
{
    /// <summary>
    /// The alias of the <see cref="IWorkflowActivationStrategy"/> to apply when new instances are requested to be created.
    /// </summary>
    public string? ActivationStrategyType { get; set; }

    /// <summary>
    /// The alias of the <see cref="IIncidentStrategy"/> to use when a fault occurs in the workflow.
    /// </summary>
    public string? IncidentStrategyType { get; set; }

    /// <summary>
    /// The alias of the strategy for committing workflow state.
    /// </summary>
    public string? CommitStrategyType { get; set; }

    /// <summary>
    /// The authored per-workflow checkpoint-persistence cadence (ADR 0032 R5). Null means the workflow authors no
    /// cadence and the host default applies. When set, it is compiled into the executable and overrides the host
    /// default at runtime; the mandatory-boundary set is never relaxable by any cadence.
    /// </summary>
    public WorkflowCheckpointCadenceOptions? CheckpointCadence { get; set; }
}
