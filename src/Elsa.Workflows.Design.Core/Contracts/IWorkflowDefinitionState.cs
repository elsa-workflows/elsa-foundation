using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

public interface IWorkflowDefinitionState
{
    /// <summary>
    /// A set of workflow variables that are accessible throughout the workflow.
    /// </summary>
    IEnumerable<VariableDefinition> Variables { get; }

    /// <summary>
    /// 
    /// </summary>
    IEnumerable<IActivityConnection> ActivityConnections { get; }

    /// <summary>
    /// 
    /// </summary>
    IEnumerable<IActivityState> Activities { get; }

    /// <summary>
    /// A set of input definitions.
    /// </summary>
    IEnumerable<IInputDefinition> Inputs { get; }

    /// <summary>
    /// A set of output definitions.
    /// </summary>
    IEnumerable<IOutputDefinition> Outputs { get; }

    /// <summary>
    /// 
    /// </summary>
    WorkflowStrategyOptions? StrategyOptions { get; }

    /// <summary>
    /// 
    /// </summary>
    WorkflowMetadata? MetaData { get; }

    WorkflowActivityOptions? WorkflowActivityOptions { get; }
}
