using Elsa.Mapping.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Api.Mapping;

public sealed class StateMappings : IObjectMapping<WorkflowDefinitionState, WorkflowDefinitionStateView>, IObjectMapping<WorkflowDefinitionStateView, WorkflowDefinitionState>
{
    /// <summary>
    /// Maps the source state to a view model
    /// </summary>
    /// <param name="source"></param>
    /// <returns></returns>
    public ValueTask<WorkflowDefinitionStateView> Map(WorkflowDefinitionState source, CancellationToken cancellationToken)
    {
        var result = new WorkflowDefinitionStateView(
            source.Variables,
            source.ActivityConnections,
            source.Activities,
            source.Inputs,
            source.Outputs,
            source.WorkflowActivityOptions,
            source.StrategyOptions
        );

        return new(result);
    }

    /// <summary>
    /// Maps the view back to a source state model
    /// </summary>
    /// <param name="source"></param>
    /// <returns></returns>
    public ValueTask<WorkflowDefinitionState> Map(WorkflowDefinitionStateView source, CancellationToken cancellationToken)
    {
        var result = new WorkflowDefinitionState(
            source.Variables ?? [],
            source.ActivityConnections ?? [],
            source.Activities ?? [],
            source.Inputs ?? [],
            source.Outputs ?? [],
            source.WorkflowActivityOptions,
            source.StrategyOptions
        );

        return new(result);
    }
}
