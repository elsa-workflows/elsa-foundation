namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityDefinitionView(
    string Id,
    string ActivityTypeKey,
    string Category,
    string? DisplayName,
    string? Description
);
