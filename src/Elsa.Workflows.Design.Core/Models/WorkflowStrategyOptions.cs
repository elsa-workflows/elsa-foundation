using Elsa.Primitives.Models;

namespace Elsa.Workflows.Design.Core.Models;

public sealed class WorkflowStrategyOptions
{
    /// <summary>
    /// The type of <see cref="IWorkflowActivationStrategy"/> to apply when new instances are requested to be created.
    /// </summary>
    public TypeInformation? ActivationStrategyType { get; set; }

    /// <summary>
    /// The type of <see cref="IIncidentStrategy"/> to use when a fault occurs in the workflow.
    /// </summary>
    public TypeInformation? IncidentStrategyType { get; set; }

    /// <summary>
    /// The options for committing workflow state.
    /// </summary>
    public TypeInformation? CommitStrategyType { get; set; }
}
