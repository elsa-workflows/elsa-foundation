using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Models;

public sealed record WorkflowDefinitionState(
    IEnumerable<VariableDefinition> Variables, 
    IEnumerable<ActivityConnection> ActivityConnections, 
    IEnumerable<ActivityState> Activities, 
    IEnumerable<InputDefinition> Inputs, 
    IEnumerable<OutputDefinition> Outputs, 
    WorkflowActivityOptions? WorkflowActivityOptions,
    WorkflowStrategyOptions? StrategyOptions, 
    WorkflowMetadata? MetaData) 
    
    : IWorkflowDefinitionState
{    
    IEnumerable<IInputDefinition> IWorkflowDefinitionState.Inputs => Inputs;
    IEnumerable<IOutputDefinition> IWorkflowDefinitionState.Outputs => Outputs;
    IEnumerable<IActivityConnection> IWorkflowDefinitionState.ActivityConnections => ActivityConnections;
    IEnumerable<IActivityState> IWorkflowDefinitionState.Activities => Activities;
}
