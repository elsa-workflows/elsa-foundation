using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Core.Models;

public sealed record WorkflowDefinitionState(
    IEnumerable<VariableDefinition> Variables,
    IEnumerable<ActivityConnection> ActivityConnections,
    IEnumerable<ActivityNode> Activities,
    IEnumerable<InputDefinition> Inputs,
    IEnumerable<OutputDefinition> Outputs,
    WorkflowActivityOptions? WorkflowActivityOptions,
    WorkflowStrategyOptions? StrategyOptions
);
