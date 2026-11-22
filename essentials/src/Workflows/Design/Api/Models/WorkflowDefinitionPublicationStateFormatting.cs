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
        IReadOnlyDictionary<string, WorkflowDefinitionPublicationSummary>? summaries,
        IReadOnlyDictionary<string, string>? versionsById = null)
    {
        if (summaries is null || !summaries.TryGetValue(view.Id, out var summary))
            return view;
        var publishedVersion = summary.PublishedVersionId is { } versionId &&
                              versionsById is not null &&
                              versionsById.TryGetValue(versionId, out var version)
            ? version
            : null;
        return view with
        {
            PublicationState = ToWire(summary.State),
            PublishedVersionId = summary.PublishedVersionId,
            PublishedVersion = publishedVersion
        };
    }
}
