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
}
