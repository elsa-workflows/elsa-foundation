using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Api.Projections;

/// <summary>
/// Explicit projections between the Workflows design entities/state and the API view models.
/// Replaces the reflective object-mapper with plain, composed functions over the read contracts.
/// </summary>
public static class WorkflowViewProjections
{
    public static WorkflowDefinitionView ToView(this IWorkflowDefinition definition) =>
        new(definition.Id, definition.Name, definition.Description, definition.CreatedAt, definition.LastModifiedAt);

    public static WorkflowDefinitionStateView ToStateView(this WorkflowDefinitionState state) =>
        new(
            state.Variables,
            state.RootActivity,
            state.Inputs,
            state.Outputs,
            state.StrategyOptions);

    public static WorkflowDefinitionState ToState(this WorkflowDefinitionStateView view) =>
        new(
            view.Variables ?? [],
            view.RootActivity,
            view.Inputs ?? [],
            view.Outputs ?? [],
            view.StrategyOptions);

    public static WorkflowDefinitionVersionDetailsView ToDetailsView(this IWorkflowDefinitionVersion version, IEnumerable<IDesignMetadataRecord>? layout = null) =>
        new(
            version.Id,
            version.Version,
            version.Definition.ToView(),
            version.State.ToStateView(),
            (layout ?? []).Select(WorkflowDefinitionLayoutRecordView.From).ToArray());
}
