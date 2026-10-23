using System.Text.Json.Serialization;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDefinitionView(
    string Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    DateTimeOffset? DeletedAt = null,
    string? DraftId = null,
    string? LatestVersionId = null,
    string? LatestVersion = null,
    int VersionCount = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PublicationState = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DraftSourceVersionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DraftSourceVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PublishedVersionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PublishedVersion = null
);

public sealed record WorkflowDefinitionListView(IReadOnlyCollection<WorkflowDefinitionView> Items);
