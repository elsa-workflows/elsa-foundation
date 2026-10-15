using Elsa.Workflows.Design.Persistence.Core.Contracts;

namespace Elsa.Workflows.Design.Api.Models;

/// <summary>Wire strings for <see cref="WorkflowDefinitionView.PublicationState"/>.</summary>
public static class WorkflowDefinitionPublicationStateFormatting
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Retracted = "retracted";
    public const string Retired = "retired";

    public static string ToWire(WorkflowDefinitionPublicationState state) => state switch
    {
        WorkflowDefinitionPublicationState.Published => Published,
        WorkflowDefinitionPublicationState.Retracted => Retracted,
        WorkflowDefinitionPublicationState.Retired => Retired,
        _ => Draft
    };

    public static WorkflowDefinitionView WithPublicationState(
        this WorkflowDefinitionView view,
        IReadOnlyDictionary<string, WorkflowDefinitionPublicationState>? states)
    {
        if (states is null || !states.TryGetValue(view.Id, out var state))
            return view;
        return view with { PublicationState = ToWire(state) };
    }
}
