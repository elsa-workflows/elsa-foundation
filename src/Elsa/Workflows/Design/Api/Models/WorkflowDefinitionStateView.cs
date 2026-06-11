using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDefinitionStateView(
    IEnumerable<VariableDefinition>? Variables = null,
    ActivityNode? RootActivity = null,
    IEnumerable<InputDefinition>? Inputs = null,
    IEnumerable<OutputDefinition>? Outputs = null,
    WorkflowActivityOptions? WorkflowActivityOptions = null,
    WorkflowStrategyOptions? StrategyOptions = null
);
