using Elsa.Mapping.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Api.Mapping;

public sealed class WorkflowDefinitionStateMappings : IObjectMapping<WorkflowDefinitionState, WorkflowDefinitionStateView>, IObjectMapping<WorkflowDefinitionStateView, WorkflowDefinitionState>
{
    public WorkflowDefinitionStateView Map(WorkflowDefinitionState source)
    {
        return new(
            source.Variables,
            source.ActivityConnections,
            source.Activities,
            source.Inputs,
            source.Outputs,
            source.WorkflowActivityOptions,
            source.StrategyOptions,
            source.MetaData
        );
    }

    public WorkflowDefinitionState Map(WorkflowDefinitionStateView source)
    {
        return new(
            source.Variables ?? [],
            source.ActivityConnections ?? [],
            source.Activities ?? [],
            source.Inputs ?? [],
            source.Outputs ?? [],
            source.WorkflowActivityOptions,
            source.StrategyOptions,
            source.MetaData
        );
    }
}
